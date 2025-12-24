using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class CrmMessage
    {
        public int Id { get; set; }

        // =========================
        // Relación
        // =========================
        public int ThreadRefId { get; set; }
        [JsonIgnore]
        public CrmThread Thread { get; set; } = null!;

        // =========================
        // Datos básicos
        // =========================
        public string Sender { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string? Text { get; set; }

        // Timestamp normalizado (UTC)
        public DateTime TimestampUtc { get; set; }

        // Timestamp externo (unix, WhatsApp)
        public long? ExternalTimestamp { get; set; }

        public bool DirectionIn { get; set; }

        // =========================
        // Media
        // =========================
        public string? MediaUrl { get; set; }
        public string? MediaMime { get; set; }

        // image / audio / document / sticker / video
        public string? MediaType { get; set; }

        // Caption asociado al media (opcional)
        public string? MediaCaption { get; set; }

        // =========================
        // Auditoría / externo
        // =========================
        public string RawPayload { get; set; } = null!;

        // Evolution / WhatsApp ids
        public string? ExternalId { get; set; }
        public string? WaMessageId { get; set; }

        // Hash para idempotencia
        public string RawHash { get; set; } = null!;

        // Tipo normalizado para UI
        public int MessageKind { get; set; }
        // 0 = Text, 1 = Image, 2 = Document, 3 = Audio, 4 = Sticker, 5 = Video

        

        // Sugerencia UI (derivable pero útil)
        public bool HasMedia { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        //Navegacion con CrmMessageMedia
        public CrmMessageMedia? Media { get; set; }

        public string? Reaction { get; set; }
    }

}
