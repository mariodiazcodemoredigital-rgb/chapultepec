using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class MessageDeadLetter
    {
        public int Id { get; set; }

        // Guarda el payload crudo (JSON)
        public string RawPayload { get; set; } = string.Empty;

        // Texto con la razón/stacktrace del fallo
        public string? Error { get; set; }

        // Opcional: origen (webhook/controller/worker)
        public string? Source { get; set; }

        // Marca si ya fue revisado por un humano (flag de administración)
        public bool Reviewed { get; set; } = false;

        // Fecha en UTC en que ocurrió el error
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        // Fecha de cuando se creó el registro en DB
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
