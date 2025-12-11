using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.services.Hubs
{
    public class CrmHub : Hub
    {
        // puedes añadir métodos para que clientes llamen, por ejemplo:
        public Task SendToGroup(string group, string method, object payload)
            => Clients.Group(group).SendAsync(method, payload);
    }
}
