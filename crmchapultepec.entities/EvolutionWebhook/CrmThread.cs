using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class CrmThread
    {
        public int Id { get; set; }
        public string ThreadId { get; set; } = null!; // ej: "1:Ventas:+5255..."
        public string? BusinessAccountId { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastMessageUtc { get; set; }
        public ICollection<CrmMessage> Messages { get; set; } = new List<CrmMessage>();
    }
}
