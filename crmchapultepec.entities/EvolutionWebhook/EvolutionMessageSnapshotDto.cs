using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class EvolutionMessageSnapshotDto
    {
        public string ThreadId { get; set; } = default!;
        public string BusinessAccountId { get; set; } = null!;
        public string Sender { get; set; } = default!;
        public string? CustomerDisplayName { get; set; }
        public string CustomerPhone { get; set; } = default!;
        public bool DirectionIn { get; set; }
        public MessageKind MessageKind { get; set; }
        public string TextPreview { get; set; } = default!;
        public string? Text { get; set; }
        public string? MediaUrl { get; set; }
        public string? MediaMime { get; set; }
        public string? MediaCaption { get; set; }
        public string? ExternalMessageId { get; set; }
        public long ExternalTimestamp { get; set; }
        public string? Source { get; set; }
        public string MessageType { get; set; } = default!;
        public string RawPayloadJson { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }
        
        
    }

}
