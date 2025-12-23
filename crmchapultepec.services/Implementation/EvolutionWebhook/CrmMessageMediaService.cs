using crmchapultepec.data.Repositories.EvolutionWebhook;
using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.EvolutionWebhook
{
    public class CrmMessageMediaService: ICrmMessageMediaService
    {
        private readonly CrmMessageMediaRepository _rmMessageMediaRepository;

        public CrmMessageMediaService(CrmMessageMediaRepository rmMessageMediaRepository)
        {
            _rmMessageMediaRepository = rmMessageMediaRepository;
        }

        public async Task<CrmMessageMedia?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _rmMessageMediaRepository.GetByIdAsync(id, ct);
        }

        public async Task<List<CrmMessageMedia>> GetAllAsync(CancellationToken ct = default)
        {
            return await _rmMessageMediaRepository.GetAllAsync(ct);
        }

        public Task<byte[]?> GetDecryptedDocumentAsync(CrmMessageMedia media, CancellationToken ct = default)        
             => _rmMessageMediaRepository.GetDecryptedDocumentAsync(media, ct);

        public Task<byte[]?> DecryptMedia(CrmMessageMedia media, CancellationToken ct = default)
        {
            return _rmMessageMediaRepository.DecryptMediaAsync(media, ct);
        }

        public async Task<CrmMessageMedia?> GetByMessageIdAsync(int messageId, CancellationToken ct = default)
        {
            // Buscamos en la lista de todos el primero que coincida con el MessageId
            // Nota: Si tu repositorio tiene un método directo de búsqueda en SQL, úsalo.
            // Si no, podemos filtrar así:
            var all = await _rmMessageMediaRepository.GetAllAsync(ct);
            return all.FirstOrDefault(x => x.MessageId == messageId);
        }
    }
}
