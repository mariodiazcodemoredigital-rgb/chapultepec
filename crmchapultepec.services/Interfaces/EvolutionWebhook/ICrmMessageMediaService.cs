using crmchapultepec.entities.EvolutionWebhook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Interfaces.EvolutionWebhook
{
    public interface ICrmMessageMediaService
    {
        Task<CrmMessageMedia?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<List<CrmMessageMedia>> GetAllAsync(CancellationToken ct = default);
        Task<byte[]?> GetDecryptedDocumentAsync(CrmMessageMedia media, CancellationToken ct = default);
        Task<byte[]?> DecryptMedia(CrmMessageMedia media, CancellationToken ct = default);
    }
}
