using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public class EvolutionSendDto
    {
        public class EvolutionSendResponse
        {
            // El campo "data" contiene la información del mensaje enviado
            public EvolutionResponseData? data { get; set; }
        }

        public class EvolutionResponseData
        {
            // "key" contiene el ID único del mensaje en WhatsApp
            public EvolutionKey? key { get; set; }
        }

        public class EvolutionKey
        {
            public string? remoteJid { get; set; }
            public bool fromMe { get; set; }
            public string? id { get; set; } // Este es el ExternalId (ej: BAEE...)
        }
    }
}
