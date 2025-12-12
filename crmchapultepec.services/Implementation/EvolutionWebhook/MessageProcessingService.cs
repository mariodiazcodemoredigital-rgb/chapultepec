using crmchapultepec.data.Data;
using crmchapultepec.data.Repositories.EvolutionWebhook;
using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.services.Hubs;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
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
   


        //private readonly IDbContextFactory<CrmInboxDbContext> _dbFactory;

        public MessageProcessingService(InMemoryMessageQueue queue, 
                                        ILogger<MessageProcessingService> log, 
                                        IConfiguration cfg, IServiceProvider sp
                                       )
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
            string rawDto = "{}";
            try { rawDto = JsonSerializer.Serialize(dto); } catch { rawDto = "{}"; }

            _log.LogInformation("Processing incoming message ({threadId}) from {sender}. DTO: {dto}", dto?.threadId, dto?.sender, rawDto);

            var rawHash = ComputeSha256HexSafe(rawDto);

            // Crear un scope para usar servicios scoped
            using var scope = _sp.CreateScope();
            var _repo = scope.ServiceProvider.GetRequiredService<IEvolutionWebhookService>();

            try
            {
                // idempotency
                var exists = false;
                try { exists = await _repo.MessageExistsByRawHashAsync(rawHash, ct); } catch (Exception exId) { _log.LogWarning(exId, "Idempotency check failed - continuing"); }

                if (exists)
                {
                    _log.LogInformation("Duplicate message detected (rawHash) - skipping.");
                    return;
                }

                if (dto == null || string.IsNullOrWhiteSpace(dto.threadId) || string.IsNullOrWhiteSpace(dto.sender))
                {
                    _log.LogWarning("DTO invalid - saving dead-letter");
                    await _repo.SaveDeadLetterAsync(new MessageDeadLetter { RawPayload = rawDto, Error = "missing_thread_or_sender", Source = "worker", OccurredUtc = DateTime.UtcNow, CreatedUtc = DateTime.UtcNow }, ct);
                    return;
                }

                // create/update thread
                var threadIntId = await _repo.CreateOrUpdateThreadAsync(dto.threadId, dto.businessAccountId, dto.timestamp, ct);

                // create message entity
                var tsUtc = DateTime.UtcNow;
                try { if (dto.timestamp > 0) tsUtc = DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime; } catch { tsUtc = DateTime.UtcNow; }

                var msgEntity = new CrmMessage
                {
                    ThreadRefId = threadIntId,
                    Sender = dto.sender,
                    DisplayName = dto.displayName,
                    Text = dto.text,
                    TimestampUtc = tsUtc,
                    DirectionIn = dto.directionIn,
                    RawPayload = rawDto,
                    RawHash = rawHash,
                    CreatedUtc = DateTime.UtcNow
                };

                var messageId = await _repo.InsertMessageAsync(msgEntity);

                await _repo.InsertPipelineHistoryAsync(new PipelineHistory { ThreadRefId = threadIntId, PipelineName = dto.ai?.pipelineName ?? "Unassigned", StageName = dto.ai?.stageName ?? "Nuevos", Source = dto.ai != null ? "webhook_ai" : "system", CreatedUtc = DateTime.UtcNow }, ct);

                // SignalR notify
                try
                {
                    using var scopeS = _sp.CreateScope();
                    var hubContext = scopeS.ServiceProvider.GetService<IHubContext<CrmHub>>();
                    if (hubContext != null && !string.IsNullOrEmpty(dto.businessAccountId))
                    {
                        await hubContext.Clients.Group(dto.businessAccountId).SendAsync("NewMessage", new { ThreadId = dto.threadId, Sender = msgEntity.Sender, Text = msgEntity.Text, MessageId = messageId }, ct);
                    }
                }
                catch (Exception exHub)
                {
                    _log.LogWarning(exHub, "SignalR notify failed");
                }

                _log.LogInformation("Message processed and saved. MsgId={msgId} Thread={threadId}", messageId, dto.threadId);
            }
            catch (Exception exTop)
            {
                _log.LogError(exTop, "Unhandled error processing DTO {threadId}", dto?.threadId);
                try
                {
                    await _repo.SaveDeadLetterAsync(new MessageDeadLetter { RawPayload = rawDto, Error = exTop.ToString(), Source = "worker", OccurredUtc = DateTime.UtcNow, CreatedUtc = DateTime.UtcNow }, ct);
                }
                catch (Exception exSave)
                {
                    _log.LogWarning(exSave, "Failed to save dead-letter via repo - falling back to file");
                    await SaveDeadLetterFallbackAsync(rawDto, exTop.ToString(), ct);
                }
            }
        }



    }
}
