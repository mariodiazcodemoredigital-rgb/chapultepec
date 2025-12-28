using crmchapultepec.entities.Entities.CRM;
using crmchapultepec.entities.EvolutionWebhook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Interfaces.CRM
{
    public interface ICRMInboxService
    {
        // Evento para notificar a la UI (Blazor) que hubo cambios
        event Action? Changed;

        // Contadores para el Sidebar
        Task<(int todos, int mios, int sinAsignar, int equipo)> GetCountsAsync(CancellationToken ct = default);

        // Obtener lista filtrada (Bandeja izquierda)
        Task<IReadOnlyList<CrmThread>> GetThreadsAsync(InboxFilter filter, string? search = null, CancellationToken ct = default);

        // Obtener detalle de una conversación (Panel derecho)
        Task<CrmThread?> GetThreadAsync(string threadId, CancellationToken ct = default);

        // Acciones
        Task<bool> AssignAsync(string threadId, string? agentUser, CancellationToken ct = default);
        Task MarkReadAsync(string threadId, CancellationToken ct = default);

        // Enviar mensaje (Agente)
        Task<CrmMessage?> AppendAgentMessageAsync(string threadId, string text, string senderName, CancellationToken ct = default);

        // Guardar/Editar contacto
        Task<int> UpsertContactAsync(int channel, string businessAccountId, string displayName, string? email, string? company, string? phone, string? platformId, CancellationToken ct = default);
    }
}
