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


        private async Task ProcessAsync(IncomingMessageDto dto, CancellationToken ct)
        {
            using var scope = _sp.CreateScope();
            var toggle = scope.ServiceProvider.GetRequiredService<IWebhookControlService>();

            if (!await toggle.IsEvolutionEnabledAsync(ct))
                return;

            if (dto == null || string.IsNullOrWhiteSpace(dto.threadId))
                return;

            var repo = scope.ServiceProvider.GetRequiredService<IEvolutionWebhookService>();

            // 🔹 Buscar thread YA EXISTENTE (creado por snapshot)
            var thread = await repo.GetThreadByExternalIdAsync(dto.threadId, ct);
            if (thread == null)
            {
                _log.LogWarning("Thread not found for DTO {threadId}", dto.threadId);
                return;
            }

            // 🔹 Pipeline / AI
            await repo.InsertPipelineHistoryAsync(
                new PipelineHistory
                {
                    ThreadRefId = thread.Id,
                    PipelineName = dto.ai?.pipelineName ?? "Default",
                    StageName = dto.ai?.stageName ?? "Inbox",
                    Source = "worker",
                    CreatedUtc = DateTime.UtcNow
                }, ct);

            _log.LogInformation("Worker processed DTO for thread {threadId}", dto.threadId);
        }



    }
}
