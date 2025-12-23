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

        // Método para que el cliente se una a su grupo de negocio
        public async Task JoinGroup(string businessAccountId)
        {
            if (!string.IsNullOrEmpty(businessAccountId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, businessAccountId);
                Console.WriteLine($"Cliente {Context.ConnectionId} unido al grupo: {businessAccountId}");
            }
        }
    }
}
