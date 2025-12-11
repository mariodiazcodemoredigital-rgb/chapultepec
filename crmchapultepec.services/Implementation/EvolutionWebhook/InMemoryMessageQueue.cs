using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace crmchapultepec.services.Implementation.EvolutionWebhook
{
    public class InMemoryMessageQueue : IMessageQueue, IDisposable
    {
        private readonly Channel<IncomingMessageDto> _channel = Channel.CreateUnbounded<IncomingMessageDto>();

        public ValueTask EnqueueAsync(IncomingMessageDto message) => _channel.Writer.WriteAsync(message);

        public ChannelReader<IncomingMessageDto> Reader => _channel.Reader;

        public void Dispose() => _channel.Writer.Complete();
    }
}
