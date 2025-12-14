using crmchapultepec.data.Repositories.EvolutionWebhook;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.EvolutionWebhook
{
    public class WebhookControlService : IWebhookControlService
    {
        private readonly WebhookControlRepository _repo;
        private bool? _cached;
        private DateTime _lastRead;

        public WebhookControlService(WebhookControlRepository repo)
        {
            _repo = repo;
        }

        public async Task<bool> IsEvolutionEnabledAsync(CancellationToken ct = default)
        {
            if (_cached.HasValue && (DateTime.UtcNow - _lastRead).TotalSeconds < 10)
                return _cached.Value;

            _cached = await _repo.IsEnabledAsync("EvolutionWebhook", ct);
            _lastRead = DateTime.UtcNow;
            return _cached.Value;
        }

        public async Task SetEvolutionEnabledAsync(bool enabled, CancellationToken ct = default)
        {
            await _repo.SetEnabledAsync("EvolutionWebhook", enabled, ct);
            _cached = enabled;
            _lastRead = DateTime.UtcNow;
        }
    }

}
