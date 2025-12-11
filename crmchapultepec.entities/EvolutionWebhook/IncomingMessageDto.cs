using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.entities.EvolutionWebhook
{
    public record IncomingMessageDto(
        string threadId,
        string businessAccountId,
        string sender,
        string displayName,
        string text,
        long timestamp,
        bool directionIn,
        AiInfo? ai,
        string action,
        string reason,
        string title
    );

    public record AiInfo(int channel, string pipelineName, string stageName);
}
