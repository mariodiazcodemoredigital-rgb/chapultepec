using crmchapultepec.entities.EvolutionWebhook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Interfaces.EvolutionWebhook
{
    public interface IEvolutionWebhookService
    {
        Task<bool> MessageExistsByRawHashAsync(string rawHash, CancellationToken ct = default);
        Task<int> CreateOrUpdateThreadAsync(string threadId, string? businessAccountId, long timestamp, CancellationToken ct = default);
        Task<int> InsertMessageAsync(CrmMessage message, CancellationToken ct = default);
        Task InsertPipelineHistoryAsync(PipelineHistory pipeline, CancellationToken ct = default);
        Task SaveDeadLetterAsync(MessageDeadLetter deadLetter, CancellationToken ct = default);
    }
}
