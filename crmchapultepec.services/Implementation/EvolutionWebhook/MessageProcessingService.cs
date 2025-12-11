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
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task ProcessAsync(IncomingMessageDto dto, CancellationToken ct)
        {
            _log.LogInformation("Processing incoming message {threadId} from {sender}", dto.threadId, dto.sender);

            // serializar raw y calcular hash para idempotencia
            var raw = JsonSerializer.Serialize(dto);
            var rawHash = ComputeSha256Hex(raw);

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // 1) Idempotencia: si ya tenemos mensaje con same ExternalId o RawHash, no duplicar
            bool exists = false;
            if (!string.IsNullOrWhiteSpace(dto.threadId))
            {
                // buscar thread y último mensaje por rawHash o external
                exists = await db.CrmMessages
                    .AsNoTracking()
                    .AnyAsync(m => m.RawHash == rawHash || (m.ExternalId != null && m.ExternalId == dto.threadId), ct);
            }
            else
            {
                exists = await db.CrmMessages.AsNoTracking().AnyAsync(m => m.RawHash == rawHash, ct);
            }

            if (exists)
            {
                _log.LogInformation("Duplicate message detected (rawHash) for thread {threadId}, skipping.", dto.threadId);
                return;
            }

            // 2) Ubica o crea Thread (simple upsert)
            var thread = await db.CrmThreads.SingleOrDefaultAsync(t => t.ThreadId == dto.threadId, ct);
            if (thread == null)
            {
                thread = new CrmThread
                {
                    ThreadId = dto.threadId,
                    BusinessAccountId = dto.businessAccountId,
                    CreatedUtc = DateTime.UtcNow,
                    LastMessageUtc = DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime
                };
                db.CrmThreads.Add(thread);
                await db.SaveChangesAsync(ct); // save to get thread.Id for FK
            }
            else
            {
                thread.LastMessageUtc = DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime;
                db.CrmThreads.Update(thread);
                await db.SaveChangesAsync(ct);
            }

            // 3) Crear mensaje
            var msgEntity = new CrmMessage
            {
                ThreadRefId = thread.Id,
                Sender = dto.sender,
                DisplayName = dto.displayName,
                Text = dto.text,
                TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(dto.timestamp).UtcDateTime,
                DirectionIn = dto.directionIn,
                RawPayload = raw,
                RawHash = rawHash,
                ExternalId = null, // si Evolution trae messageId asigna aquí
                CreatedUtc = DateTime.UtcNow
            };

            db.CrmMessages.Add(msgEntity);
            await db.SaveChangesAsync(ct);

            // 4) PipelineHistory
            if (dto.ai is not null)
            {
                db.PipelineHistories.Add(new PipelineHistory
                {
                    ThreadRefId = thread.Id,
                    PipelineName = dto.ai.pipelineName ?? "Unassigned",
                    StageName = dto.ai.stageName ?? "Nuevos",
                    Source = "webhook_ai",
                    CreatedUtc = DateTime.UtcNow
                });
            }
            else
            {
                db.PipelineHistories.Add(new PipelineHistory
                {
                    ThreadRefId = thread.Id,
                    PipelineName = "Unassigned",
                    StageName = "Nuevos",
                    Source = "system",
                    CreatedUtc = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync(ct);

            // 5) Notificar UI (SignalR)
            using var scope = _sp.CreateScope();
            var hubContext = scope.ServiceProvider.GetService<IHubContext<CrmHub>>();
            if (hubContext != null)
            {
                // agrupar por businessAccountId para control de permisos en frontend
                await hubContext.Clients.Group(dto.businessAccountId).SendAsync("NewMessage", new
                {
                    ThreadId = thread.ThreadId,
                    Sender = msgEntity.Sender,
                    Text = msgEntity.Text,
                    MessageId = msgEntity.Id
                }, ct);
            }

            _log.LogInformation("Message {id} processed and saved (EF)", msgEntity.Id);
        }

    }
}
