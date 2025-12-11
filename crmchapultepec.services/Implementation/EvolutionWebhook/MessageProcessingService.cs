using crmchapultepec.data.Data;
using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.services.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.EvolutionWebhook
{
    public class MessageProcessingService : BackgroundService
    {
        private readonly InMemoryMessageQueue _queue;
        private readonly ILogger<MessageProcessingService> _log;
        private readonly IConfiguration _cfg;
        private readonly IServiceProvider _sp;
        private readonly IDbContextFactory<CrmInboxDbContext> _dbFactory;

        public MessageProcessingService(InMemoryMessageQueue queue, ILogger<MessageProcessingService> log, IConfiguration cfg, IServiceProvider sp)
        {
            _queue = queue;
            _log = log;
            _cfg = cfg;
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var msg in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(msg, stoppingToken);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error processing message {threadId}", msg.threadId);
                    // aquí podrías escribir a una DLQ (dead-letter table) para reintentos humanos
                }
            }
        }

        // Reemplaza ProcessAsync por esta versión defensiva
        private string ComputeSha256HexSafe(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input ?? "");
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task SaveDeadLetterFallbackAsync(string raw, string error, CancellationToken ct)
        {
            try
            {
                // Intentar guardar en archivo como fallback
                var path = _cfg?["WebhookDebug:DeadLetterPath"] ?? "deadletters_fallback.log";
                var entry = new
                {
                    OccurredUtc = DateTime.UtcNow,
                    Error = error,
                    Raw = TryParseJsonOrString(raw),
                };
                var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
                lock (typeof(MessageProcessingService))
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                _log.LogWarning("Saved dead-letter to file fallback: {path}", path);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to write dead-letter to fallback file");
            }
        }

        private object TryParseJsonOrString(string raw)
        {
            try
            {
                return JsonSerializer.Deserialize<JsonElement>(raw);
            }
            catch
            {
                return raw;
            }
        }

        private async Task ProcessAsync(IncomingMessageDto dto, CancellationToken ct)
        {
            // Log inicial con el DTO completo (para análisis)
            string rawDto = "{}";
            try
            {
                rawDto = JsonSerializer.Serialize(dto);
            }
            catch { rawDto = "{}"; }

            _log.LogInformation("Processing incoming message ({threadId}) from {sender}. DTO: {dto}", dto?.threadId, dto?.sender, rawDto);

            var rawHash = ComputeSha256HexSafe(rawDto);

            // Intentamos usar DB; si falla, fallback a archivo
            CrmInboxDbContext? db = null;
            bool dbAvailable = true;
            try
            {
                if (_dbFactory == null)
                {
                    _log.LogWarning("IDbContextFactory<CrmInboxDbContext> is null (DI issue)");
                    dbAvailable = false;
                }
                else
                {
                    try
                    {
                        db = await _dbFactory.CreateDbContextAsync(ct);
                        if (db == null)
                        {
                            _log.LogWarning("CreateDbContextAsync returned null");
                            dbAvailable = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to create DbContext from factory");
                        dbAvailable = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Unexpected error while checking db factory");
                dbAvailable = false;
            }

            // Idempotencia check (if db available), otherwise rely on file fallback (we still continue)
            try
            {
                if (dbAvailable && db != null)
                {
                    // check table existence defensively
                    var canQueryDeadLetters = true;
                    try
                    {
                        // Simple no-op query to ensure model has the DbSet
                        _ = db.MessageDeadLetters != null;
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "DbContext does not appear to have MessageDeadLetters DbSet configured");
                        canQueryDeadLetters = false;
                    }

                    bool exists = false;
                    try
                    {
                        exists = await db.CrmMessages.AsNoTracking().AnyAsync(m => m.RawHash == rawHash, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Error checking idempotency in DB; proceeding without DB idempotency check");
                        exists = false;
                    }

                    if (exists)
                    {
                        _log.LogInformation("Duplicate message detected by rawHash; skipping. ThreadId={threadId}", dto?.threadId);
                        return;
                    }
                }

                // Validate dto minimally
                if (dto == null)
                {
                    _log.LogWarning("Incoming dto is null -> saving dead-letter fallback");
                    await SaveDeadLetterFallbackAsync(rawDto, "dto_null", ct);
                    return;
                }

                if (string.IsNullOrEmpty(dto.threadId) || string.IsNullOrEmpty(dto.sender))
                {
                    _log.LogWarning("dto missing threadId or sender -> saving dead-letter fallback. DTO: {dto}", rawDto);
                    await SaveDeadLetterFallbackAsync(rawDto, "missing_thread_or_sender", ct);
                    return;
                }

                // If DB available, proceed to persist; otherwise fallback to file but still attempt best-effort (no throw)
                if (dbAvailable && db != null)
                {
                    // Start transaction (short lived)
                    using var tx = await db.Database.BeginTransactionAsync(ct);
                    try
                    {
                        // Get or create thread
                        var thread = await db.CrmThreads.SingleOrDefaultAsync(t => t.ThreadId == dto.threadId, ct);
                        if (thread == null)
                        {
                            thread = new CrmThread
                            {
                                ThreadId = dto.threadId,
                                BusinessAccountId = dto.businessAccountId,
                                CreatedUtc = DateTime.UtcNow,
                                LastMessageUtc = (dto.timestamp > 0) ? DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime : DateTime.UtcNow
                            };
                            db.CrmThreads.Add(thread);
                            await db.SaveChangesAsync(ct);
                        }
                        else
                        {
                            if (dto.timestamp > 0) thread.LastMessageUtc = DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime;
                            db.CrmThreads.Update(thread);
                            await db.SaveChangesAsync(ct);
                        }

                        // Create message
                        var tsUtc = DateTime.UtcNow;
                        if (dto.timestamp > 0)
                        {
                            try { tsUtc = DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime; } catch { tsUtc = DateTime.UtcNow; }
                        }

                        var msgEntity = new CrmMessage
                        {
                            ThreadRefId = thread.Id,
                            Sender = dto.sender ?? "unknown",
                            DisplayName = dto.displayName,
                            Text = dto.text,
                            TimestampUtc = tsUtc,
                            DirectionIn = dto.directionIn,
                            RawPayload = rawDto,
                            RawHash = rawHash,
                            ExternalId = null,
                            CreatedUtc = DateTime.UtcNow
                        };

                        db.CrmMessages.Add(msgEntity);
                        await db.SaveChangesAsync(ct);

                        // Pipeline history
                        db.PipelineHistories.Add(new PipelineHistory
                        {
                            ThreadRefId = thread.Id,
                            PipelineName = dto.ai?.pipelineName ?? "Unassigned",
                            StageName = dto.ai?.stageName ?? "Nuevos",
                            Source = dto.ai != null ? "webhook_ai" : "system",
                            CreatedUtc = DateTime.UtcNow
                        });
                        await db.SaveChangesAsync(ct);

                        await tx.CommitAsync(ct);

                        // Notify SignalR safely
                        try
                        {
                            using var scope = _sp.CreateScope();
                            var hubContext = scope.ServiceProvider.GetService<IHubContext<CrmHub>>();
                            if (hubContext != null && !string.IsNullOrEmpty(dto.businessAccountId))
                            {
                                await hubContext.Clients.Group(dto.businessAccountId)
                                    .SendAsync("NewMessage", new { ThreadId = thread.ThreadId, Sender = msgEntity.Sender, Text = msgEntity.Text, MessageId = msgEntity.Id }, ct);
                            }
                        }
                        catch (Exception exHub)
                        {
                            _log.LogWarning(exHub, "SignalR notify failed");
                        }

                        _log.LogInformation("Message processed and saved. MsgId={msgId} Thread={threadId}", msgEntity.Id, thread.ThreadId);
                        return;
                    }
                    catch (Exception exDbOp)
                    {
                        _log.LogError(exDbOp, "Database operation failed while processing dto. Will try to save dead-letter to DB (if possible) or fallback file.");
                        // try save dead-letter in DB if possible
                        try
                        {
                            if (db.MessageDeadLetters != null)
                            {
                                db.MessageDeadLetters.Add(new MessageDeadLetter
                                {
                                    RawPayload = rawDto,
                                    Error = exDbOp.ToString(),
                                    Source = "worker",
                                    Reviewed = false,
                                    OccurredUtc = DateTime.UtcNow,
                                    CreatedUtc = DateTime.UtcNow
                                });
                                await db.SaveChangesAsync(ct);
                                _log.LogInformation("Saved dead-letter in DB after failure.");
                                return;
                            }
                        }
                        catch (Exception exDeadDb)
                        {
                            _log.LogWarning(exDeadDb, "Failed saving dead-letter in DB, falling back to file.");
                        }

                        // Fallback file
                        await SaveDeadLetterFallbackAsync(rawDto, exDbOp.ToString(), ct);
                        return;
                    }
                }
                else
                {
                    // DB not available: fallback to file but still attempt to create a minimal record structure in file for later processing
                    var fallbackError = "db_unavailable";
                    await SaveDeadLetterFallbackAsync(rawDto, fallbackError, ct);
                    return;
                }
            }
            catch (Exception exTop)
            {
                _log.LogError(exTop, "Unhandled exception in ProcessAsync for dto {threadId}", dto?.threadId);
                await SaveDeadLetterFallbackAsync(rawDto, exTop.ToString(), ct);
            }
        }



    }
}
