using crmchapultepec.data.Repositories.CRM;
using crmchapultepec.entities.Entities.CRM;
using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.services.Interfaces.CRM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.CRM
{
    public class CRMInboxService : ICRMInboxService
    {
        private readonly CrmInboxRepository _crminboxRepository;

        public CRMInboxService(CrmInboxRepository crminboxRepository)
        {
            _crminboxRepository = crminboxRepository;
        }


        // Propagar evento del repositorio
        public event Action? Changed
        {
            add { _crminboxRepository.Changed += value; }
            remove { _crminboxRepository.Changed -= value; }
        }

        public Task<(int todos, int mios, int sinAsignar, int equipo)> GetCountsAsync(CancellationToken ct = default)
        {
            return _crminboxRepository.GetCountsAsync(ct);
        }

        public Task<IReadOnlyList<CrmThread>> GetThreadsAsync(InboxFilter filter, string? search = null, CancellationToken ct = default)
        {
            return _crminboxRepository.GetThreadsAsync(filter, search, ct);
        }

        public Task<CrmThread?> GetThreadAsync(string threadId, CancellationToken ct = default)
        {
            return _crminboxRepository.GetThreadByIdAsync(threadId, ct);
        }

        public Task<bool> AssignAsync(string threadId, string? agentUser, CancellationToken ct = default)
        {
            return _crminboxRepository.AssignAsync(threadId, agentUser, ct);
        }

        public Task MarkReadAsync(string threadId, CancellationToken ct = default)
        {
            return _crminboxRepository.MarkReadAsync(threadId, ct);
        }

        public Task<CrmMessage?> AppendAgentMessageAsync(string threadId, string text, string senderName, CancellationToken ct = default)
        {
            return _crminboxRepository.AppendAgentMessageAsync(threadId, text, senderName, ct);
        }

        public Task<int> UpsertContactAsync(int channel, string businessAccountId, string displayName, string? email, string? company, string? phone, string? platformId, CancellationToken ct = default)
        {
            // Pasamos null como platformId si no se usa en el repo simplificado
            return _crminboxRepository.UpsertContactAsync(channel, businessAccountId, displayName, phone, email, company, ct);
        }

    }

}
