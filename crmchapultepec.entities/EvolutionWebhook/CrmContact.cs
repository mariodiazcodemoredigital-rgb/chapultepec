using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class CrmContact
    {
        public int Id { get; set; }
        public int ThreadRefId { get; set; }
        public CrmThread Thread { get; set; } = null!;
        public int Channel { get; set; }
        public string? BusinessAccountId { get; set; }
        public string ContactKey { get; set; } = null!; // Ej: phone number
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public string? Company { get; set; }
        public string? Phone { get; set; }
        public string? PlatformId { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
