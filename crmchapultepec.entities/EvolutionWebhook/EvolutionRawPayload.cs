using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    namespace crmchapultepec.data.Entities.EvolutionWebhook
    {
        public class EvolutionRawPayload
        {
            public int Id { get; set; }

            public string ThreadId { get; set; } = default!;

            public string Source { get; set; } = "evolution";

            public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

            public string PayloadJson { get; set; } = default!;

            public bool Processed { get; set; } = false;

            public string? Notes { get; set; }
        }
    }

}
