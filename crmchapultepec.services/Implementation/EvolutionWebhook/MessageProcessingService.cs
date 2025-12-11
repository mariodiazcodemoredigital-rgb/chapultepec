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

        private static string ComputeSha256Hex(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input ?? "");
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }


        private async Task ProcessAsync(IncomingMessageDto dto, CancellationToken ct)
        {
            // Protección temprana y logging detallado
            _log.LogInformation("Processing incoming message ({threadId}) from {sender}", dto?.threadId, dto?.sender);
            string raw = "{}";
            try { raw = JsonSerializer.Serialize(dto); } catch { raw = "{}"; }

            // calcular hash para idempotencia
            var rawHash = ComputeSha256Hex(raw);

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                // Idempotencia: comprobar por RawHash y (si existe) ExternalId
                bool exists = await db.CrmMessages
                    .AsNoTracking()
                    .AnyAsync(m => m.RawHash == rawHash
                                   || (!string.IsNullOrEmpty(m.ExternalId) && m.ExternalId == (dto.threadId ?? "")), ct);

                if (exists)
                {
                    _log.LogInformation("Duplicate message detected (rawHash) for thread {threadId}, skipping.", dto?.threadId);
                    return;
                }

                // Validaciones mínimas: si dto o campos faltan -> guardar en dead-letters y salir
                if (dto == null)
                {
                    _log.LogWarning("DTO null, saving to dead letters.");
                    db.MessageDeadLetters.Add(new MessageDeadLetter { RawPayload = raw, Error = "dto_null", Source = "worker", OccurredUtc = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                    return;
                }

                if (string.IsNullOrWhiteSpace(dto.threadId) || string.IsNullOrWhiteSpace(dto.sender))
                {
                    _log.LogWarning("DTO missing required fields (threadId/sender). Storing in dead-letters.");
                    db.MessageDeadLetters.Add(new MessageDeadLetter { RawPayload = raw, Error = "missing_thread_or_sender", Source = "worker", OccurredUtc = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                    return;
                }

                // Obtener o crear thread (usando threadId)
                var thread = await db.CrmThreads.SingleOrDefaultAsync(t => t.ThreadId == dto.threadId, ct);
                if (thread == null)
                {
                    thread = new CrmThread
                    {
                        ThreadId = dto.threadId,
                        BusinessAccountId = dto.businessAccountId, // puede ser null, ok
                        CreatedUtc = DateTime.UtcNow,
                        LastMessageUtc = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, dto.timestamp)).UtcDateTime
                    };
                    db.CrmThreads.Add(thread);
                    await db.SaveChangesAsync(ct); // salvo para obtener thread.Id
                }
                else
                {
                    // Actualizar LastMessageUtc si timestamp válido
                    if (dto.timestamp > 0)
                    {
                        thread.LastMessageUtc = DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime;
                        db.CrmThreads.Update(thread);
                        await db.SaveChangesAsync(ct);
                    }
                }

                // Crear mensaje (llenar con valores por defecto en caso de null)
                var tsUtc = DateTime.UtcNow;
                try
                {
                    if (dto.timestamp > 0)
                        tsUtc = DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime;
                }
                catch { tsUtc = DateTime.UtcNow; }

                var msgEntity = new CrmMessage
                {
                    ThreadRefId = thread.Id,
                    Sender = dto.sender ?? "unknown",
                    DisplayName = dto.displayName,
                    Text = dto.text,
                    TimestampUtc = tsUtc,
                    DirectionIn = dto.directionIn,
                    RawPayload = raw,
                    RawHash = rawHash,
                    ExternalId = null,
                    CreatedUtc = DateTime.UtcNow
                };

                db.CrmMessages.Add(msgEntity);
                await db.SaveChangesAsync(ct);

                // PipelineHistory: usar dto.ai si existe, si no fallback
                var pipelineName = dto.ai?.pipelineName ?? "Unassigned";
                var stageName = dto.ai?.stageName ?? "Nuevos";
                db.PipelineHistories.Add(new PipelineHistory
                {
                    ThreadRefId = thread.Id,
                    PipelineName = pipelineName,
                    StageName = stageName,
                    Source = dto.ai != null ? "webhook_ai" : "system",
                    CreatedUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);

                // SignalR notify: sólo si hay hub y businessAccountId para agrupar
                try
                {
                    using var scope = _sp.CreateScope();
                    var hubContext = scope.ServiceProvider.GetService<IHubContext<CrmHub>>();
                    if (hubContext != null && !string.IsNullOrEmpty(dto.businessAccountId))
                    {
                        await hubContext.Clients.Group(dto.businessAccountId).SendAsync("NewMessage", new
                        {
                            ThreadId = thread.ThreadId,
                            Sender = msgEntity.Sender,
                            Text = msgEntity.Text,
                            MessageId = msgEntity.Id
                        }, ct);
                    }
                    else
                    {
                        _log.LogDebug("HubContext null or businessAccountId empty; skipping SignalR notify.");
                    }
                }
                catch (Exception exHub)
                {
                    // No queremos que un fallo de SignalR rompa el flujo
                    _log.LogWarning(exHub, "SignalR notify failed for thread {threadId}", dto.threadId);
                }

                _log.LogInformation("Message {id} processed and saved (EF). ThreadId={threadId}, Sender={sender}", msgEntity.Id, thread.ThreadId, msgEntity.Sender);
            }
            catch (Exception ex)
            {
                // En caso de error persistente, guardar en dead-letters con stacktrace y dto
                _log.LogError(ex, "Error processing message ({threadId})", dto?.threadId);

                try
                {
                    await using var db = await _dbFactory.CreateDbContextAsync(ct);
                    db.MessageDeadLetters.Add(new MessageDeadLetter
                    {
                        RawPayload = raw,
                        Error = ex.ToString(),
                        Source = "worker",
                        OccurredUtc = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex2)
                {
                    _log.LogError(ex2, "Failed to save dead-letter for message ({threadId})", dto?.threadId);
                }
            }
        }


    }
}
