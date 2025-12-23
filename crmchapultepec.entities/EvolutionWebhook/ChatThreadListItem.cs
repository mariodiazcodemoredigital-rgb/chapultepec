using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public sealed class ChatThreadListItem
    {
        public string Id { get; set; } = default!;              // ThreadId
        public string CustomerName { get; set; } = "";
        public string? CustomerPhone { get; set; }
        public string? CustomerEmail { get; set; }
        public string Channel { get; set; } = "";               // si usas enum, mapea a string
        public DateTime LastUpdated { get; set; }               // = LastMessageAt
        public int UnreadCount { get; set; }
        public string? AssignedTo { get; set; }                 // tu AssignedTo (id usuario CRM)
        public int MessagesCount { get; set; }

        // NUEVO: texto del mensaje que coincidió con la búsqueda (si existe)
        public string? MatchPreview { get; set; }               // “snippet” del mensaje encontrado
        public DateTime? MatchAt { get; set; }                  // cuándo fue ese mensaje
    }
}
