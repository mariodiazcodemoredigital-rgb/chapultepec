using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class MediaDto
    {
        public int Id { get; set; }
        public string? FileName { get; set; } = default!;
        public string? MediaType { get; set; } // usar MediaType en lugar de MediaMime
    }

}
