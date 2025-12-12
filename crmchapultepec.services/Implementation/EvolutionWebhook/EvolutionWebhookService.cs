using crmchapultepec.data.Repositories.EvolutionWebhook;
using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.EvolutionWebhook
{
    public class EvolutionWebhookService : IEvolutionWebhookService
    {
        private readonly EvolutionWebhookRepository _evolutionWebhookRepository;

        public EvolutionWebhookService(EvolutionWebhookRepository evolutionWebhookRepository)
        {
            _evolutionWebhookRepository = evolutionWebhookRepository;
        }
        public Task<int> CreateOrUpdateThreadAsync(string threadId, string? businessAccountId, long timestamp, CancellationToken ct = default)        
            => _evolutionWebhookRepository.CreateOrUpdateThreadAsync(threadId, businessAccountId, timestamp, ct);        

        public Task<int> InsertMessageAsync(CrmMessage message, CancellationToken ct = default)
        => _evolutionWebhookRepository.InsertMessageAsync(message,ct);

        public Task InsertPipelineHistoryAsync(PipelineHistory pipeline, CancellationToken ct = default)
          => _evolutionWebhookRepository.InsertPipelineHistoryAsync(pipeline, ct);

        public Task<bool> MessageExistsByRawHashAsync(string rawHash, CancellationToken ct = default)
         => _evolutionWebhookRepository.MessageExistsByRawHashAsync(rawHash, ct);

        public Task SaveDeadLetterAsync(MessageDeadLetter deadLetter, CancellationToken ct = default)
        => _evolutionWebhookRepository.SaveDeadLetterAsync(deadLetter, ct);
    }
}
