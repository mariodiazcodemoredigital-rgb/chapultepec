using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class PipelineHistory
    {
        public int Id { get; set; }
        public int ThreadRefId { get; set; }
        public CrmThread Thread { get; set; } = null!;
        public string PipelineName { get; set; } = null!;
        public string StageName { get; set; } = null!;
        public string Source { get; set; } = null!;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
