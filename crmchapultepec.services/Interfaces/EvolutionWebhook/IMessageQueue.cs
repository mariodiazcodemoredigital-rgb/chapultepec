using crmchapultepec.entities.EvolutionWebhook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Interfaces.EvolutionWebhook
{
    public interface IMessageQueue
    {
        ValueTask EnqueueAsync(IncomingMessageDto message);
    }
}
