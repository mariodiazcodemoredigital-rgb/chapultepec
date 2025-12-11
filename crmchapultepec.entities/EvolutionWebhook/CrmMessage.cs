using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class CrmMessage
    {
        public int Id { get; set; }
        public int ThreadRefId { get; set; }
        public CrmThread Thread { get; set; } = null!;
        public string Sender { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string? Text { get; set; }
        public DateTime TimestampUtc { get; set; }
        public bool DirectionIn { get; set; }
        public string RawPayload { get; set; } = null!;

        // Campos para idempotencia/externo
        public string? ExternalId { get; set; } // si Evolution envía messageId
        public string RawHash { get; set; } = null!; // hash del payload para detectar duplicados

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
