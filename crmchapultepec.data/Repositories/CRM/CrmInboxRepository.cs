using crmchapultepec.data.Data;
using crmchapultepec.entities.Entities.CRM;
using crmchapultepec.entities.EvolutionWebhook;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using static crmchapultepec.entities.EvolutionWebhook.EvolutionSendDto;

namespace crmchapultepec.data.Repositories.CRM
{
    public class CrmInboxRepository
    {
        private readonly IDbContextFactory<CrmInboxDbContext> _dbFactory;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _cfg;
        
        public CrmInboxRepository(IDbContextFactory<CrmInboxDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // Usuario actual simulado (o inyectado si tienes Auth)
        public string CurrentUser { get; } = "you";

        // Evento para refrescar UI
        public event Action? Changed;
        private void NotifyChanged() => Changed?.Invoke();

        // ---------------------------------------------------------
        // 1. OBTENER CONTEOS (Sidebar)
        // ---------------------------------------------------------
        public async Task<(int todos, int mios, int sinAsignar, int equipo)> GetCountsAsync(CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Filtramos solo los que no están "cerrados" si tuvieras estatus cerrado. 
            // Asumo Status = 0 es abierto.
            var baseQuery = db.CrmThreads.AsNoTracking();

            var todos = await baseQuery.CountAsync(ct);
            var mios = await baseQuery.CountAsync(t => t.AssignedTo == CurrentUser, ct);

            // Sin asignar: nulo o vacío
            var sinAsignar = await baseQuery.CountAsync(t => t.AssignedTo == null || t.AssignedTo == "", ct);

            // Equipo: asignado a alguien que no soy yo
            var equipo = await baseQuery.CountAsync(t => t.AssignedTo != null && t.AssignedTo != "" && t.AssignedTo != CurrentUser, ct);

            return (todos, mios, sinAsignar, equipo);
        }


        // ---------------------------------------------------------
        // 2. OBTENER LISTA DE HILOS (ThreadList)
        // ---------------------------------------------------------
        public async Task<IReadOnlyList<CrmThread>> GetThreadsAsync(InboxFilter filter, string? search = null, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            IQueryable<CrmThread> q = db.CrmThreads.AsNoTracking();

            // Aplicar Filtro de Pestañas
            q = filter switch
            {
                InboxFilter.Mios => q.Where(t => t.AssignedTo == CurrentUser),
                InboxFilter.SinAsignar => q.Where(t => t.AssignedTo == null || t.AssignedTo == ""),
                InboxFilter.Equipo => q.Where(t => t.AssignedTo != null && t.AssignedTo != "" && t.AssignedTo != CurrentUser),
                _ => q // Todos
            };

            // Aplicar Búsqueda (Search bar)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(t =>
                    (t.CustomerDisplayName != null && t.CustomerDisplayName.ToLower().Contains(s)) ||
                    (t.CustomerPhone != null && t.CustomerPhone.Contains(s)) ||
                    (t.CustomerEmail != null && t.CustomerEmail.ToLower().Contains(s)) ||
                    (t.LastMessagePreview != null && t.LastMessagePreview.ToLower().Contains(s))
                );
            }

            // Ordenar: Lo más reciente arriba (usando LastMessageUtc, fallback a CreatedUtc)
            q = q.OrderByDescending(t => t.LastMessageUtc ?? t.CreatedUtc);

            // Traemos los datos (puedes poner .Take(50) para paginar)
            return await q.ToListAsync(ct);
        }

        // ---------------------------------------------------------
        // 3. OBTENER DETALLE DE UN HILO (ChatView)
        // ---------------------------------------------------------
        public async Task<CrmThread?> GetThreadByIdAsync(string threadId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var thread = await db.CrmThreads
                .AsNoTracking()
                .Include(t => t.Messages) // Eager loading de mensajes
                    .ThenInclude(m => m.Media) // Si quieres cargar info del media
                .FirstOrDefaultAsync(t => t.ThreadId == threadId, ct);

            if (thread == null) return null;

            // --- CORRECCIÓN CRÍTICA: ROMPER REFERENCIA CIRCULAR ---
            if (thread.Messages != null)
            {
                foreach (var msg in thread.Messages)
                {
                    // Cortamos el ciclo infinito. El hijo ya no apunta al padre.
                    msg.Thread = null!;
                }

                // Ordenamos en memoria
                thread.Messages = thread.Messages
                    .OrderBy(m => m.TimestampUtc)
                    .ToList();
            }

            return thread;
        }

        // ---------------------------------------------------------
        // 4. ASIGNAR AGENTE
        // ---------------------------------------------------------
        public async Task<bool> AssignAsync(string threadId, string? agentUser, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var rows = await db.CrmThreads
                .Where(t => t.ThreadId == threadId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AssignedTo, agentUser), ct);

            if (rows > 0)
            {
                NotifyChanged();
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------
        // 5. MARCAR COMO LEÍDO
        // ---------------------------------------------------------
        public async Task MarkReadAsync(string threadId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Resetear UnreadCount a 0
            var rows = await db.CrmThreads
                .Where(t => t.ThreadId == threadId && t.UnreadCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UnreadCount, 0), ct);

            if (rows > 0)
            {
                NotifyChanged();
            }
        }

        // ---------------------------------------------------------
        // 6. ENVIAR MENSAJE (AGENTE -> CLIENTE)
        // ---------------------------------------------------------
        public async Task<CrmMessage?> AppendAgentMessageAsync(string threadId, string text, string senderName, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var thread = await db.CrmThreads.FirstOrDefaultAsync(t => t.ThreadId == threadId, ct);
            if (thread == null) return null;
            try
            {
                // 1. OBTENER CONFIGURACIÓN DE EVOLUTION
                var apiUrl = _cfg["Evolution:ApiUrl"];
                var apiKey = _cfg["Evolution:ApiKey"];
                var instance = _cfg["Evolution:InstanceName"];

                // 2. ENVIAR A EVOLUTION API (WHATSAPP REAL)
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("apikey", apiKey);

                var payload = new
                {
                    number = thread.CustomerPhone,
                    text = text,
                    delay = 1200,
                    linkPreview = true
                };

                var response = await _httpClient.PostAsJsonAsync($"{apiUrl}/message/sendText/{instance}", payload, ct);

                if (!response.IsSuccessStatusCode) return null;

                var evolutionResult = await response.Content.ReadFromJsonAsync<EvolutionSendResponse>(cancellationToken: ct);

                // 3. LOGICA DE FECHAS (MEXICO)
                TimeZoneInfo mexicoZone;
                try { mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)"); }
                catch { mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"); }
                var nowMexico = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mexicoZone);

                // 4. GUARDAR EN DB LOCAL
                var msg = new CrmMessage
                {
                    ThreadRefId = thread.Id,
                    Sender = senderName ?? CurrentUser,
                    DisplayName = senderName ?? CurrentUser,
                    Text = text,
                    DirectionIn = false,
                    TimestampUtc = nowMexico,
                    CreatedUtc = nowMexico,
                    MessageKind = 0,
                    ExternalId = evolutionResult?.data?.key?.id,
                    RawPayload = "{}"
                };

                db.CrmMessages.Add(msg);
                thread.LastMessageUtc = nowMexico;
                thread.LastMessagePreview = text;

                await db.SaveChangesAsync(ct);         

                NotifyChanged();
                return msg;
            }
            catch
            {
                return null;
            }
        }


        // ---------------------------------------------------------
        // 7. GUARDAR/EDITAR CONTACTO (Modal)
        // ---------------------------------------------------------
        public async Task<int> UpsertContactAsync(int channel, string businessAccountId, string displayName, string? phone, string? email, string? company, CancellationToken ct = default)
        {
            // NOTA: Aquí adapto la lógica para actualizar la tabla CrmThreads directamente
            // o tu tabla de Contactos si la tienes separada. 
            // Dado que en el código anterior usabas un SP, aquí usaré EF Core sobre CrmThread
            // para simplificar, asumiendo que quieres actualizar los datos del cliente EN EL HILO.

            // Si tienes una tabla CrmContacts separada, ajusta este código para hacer Update/Add ahí.

            // Lógica: Actualizar todos los hilos que coincidan con ese teléfono/email
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Buscamos hilos que coincidan con el identificador principal (phone)
            // Si quieres buscar por PlatformId también, necesitarías pasarlo.
            if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(email)) return 0;

            var query = db.CrmThreads.AsQueryable();

            if (!string.IsNullOrWhiteSpace(phone))
                query = query.Where(t => t.CustomerPhone == phone);
            else if (!string.IsNullOrWhiteSpace(email))
                query = query.Where(t => t.CustomerEmail == email);

            // Ejecutamos Update masivo
            var rows = await query.ExecuteUpdateAsync(s => s
                .SetProperty(t => t.CustomerDisplayName, displayName)
                .SetProperty(t => t.CustomerEmail, email)
                .SetProperty(t => t.CustomerPhone, phone)
                .SetProperty(t => t.CompanyId, company), ct);

            if (rows > 0) NotifyChanged();

            return rows;
        }



    }
}
