using crmchapultepec.data.Data;
using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.entities.EvolutionWebhook.crmchapultepec.data.Entities.EvolutionWebhook;
using crmchapultepec.services.Hubs;
using crmchapultepec.services.Implementation.EvolutionWebhook;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace crmchapultepec.Components.EvolutionWebhook
{
    [Route("api/webhook/evolution")]
    [ApiController]
    public class EvolutionWebhookController : ControllerBase
    {
        private readonly ILogger<EvolutionWebhookController> _log;
        private readonly IMessageQueue _queue;
        private readonly IConfiguration _cfg;
        private readonly IHubContext<CrmHub> _hubContext;        
        private readonly ICrmMessageMediaService _mediaService;
        private readonly string? _hmacSecret;
        private readonly string? _inboundToken;
        private readonly string[] _ipWhitelist;

        public EvolutionWebhookController(
        ILogger<EvolutionWebhookController> log,
        IMessageQueue queue,
        IConfiguration cfg,
        IHubContext<CrmHub> hubContext,
        ICrmMessageMediaService mediaService)
        {
            _log = log;
            _queue = queue;
            _cfg = cfg;
            _hubContext = hubContext;
            _mediaService = mediaService;
            _hmacSecret = cfg["Evolution:WebhookHmacSecret"];        // opcional
            _inboundToken = cfg["Evolution:WebhookInboundToken"];  // opcional
            _ipWhitelist = (cfg["Evolution:WebhookIpWhitelist"] ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        [HttpGet("all")]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrmInboxDbContext>>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var medias = await db.CrmMessageMedias
                .Select(m => new MediaDto
                {
                    Id = m.Id,
                    FileName = m.FileName,
                    MediaType = m.MediaType
                    //CreatedAtUtc = m.CreatedAtUtc
                })
                .ToListAsync(ct);

            return Ok(medias);
        }


        // Endpoint para descargar/desencriptar un archivo por Id
        [HttpGet("download/{id}")]
        public async Task<IActionResult> Download(int id, CancellationToken ct)
        {
            var media = await _mediaService.GetByIdAsync(id, ct);
            if (media == null) return NotFound();

            var bytes = await _mediaService.DecryptMedia(media, ct);
            if (bytes == null) return BadRequest("No se pudo desencriptar el archivo");

            // Si no tienes MediaMime, usar un genérico o inferir por la extensión
            var mime = media.MediaType switch
            {
                "image" => "image/jpeg",
                "sticker" => "image/webp",
                "document" => "application/pdf",
                "audio" => "audio/mpeg",
                _ => "application/octet-stream"
            };

            // Si es imagen o sticker, lo enviamos SIN el nombre del archivo 
            // para que el navegador lo renderice en el <img>
            if (media.MediaType == "image" || media.MediaType == "sticker")
            {
                return File(bytes, mime);
            }

            // Para documentos, mantenemos la descarga
            return File(bytes, mime, media.FileName);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromServices] IWebhookControlService toggle, CancellationToken ct)
        {
            // 🔴 Switch General para apagar los insert de Evolution API
            if (!await toggle.IsEvolutionEnabledAsync(ct))
            {
                _log.LogWarning("Evolution webhook recibido pero DESACTIVADO");
                return Ok(new { status = "disabled" }); // SIEMPRE 200
            }

            // 1) Validar IP (opcional y complementario si tienes ips de Evolution)
            if (_ipWhitelist.Length > 0)
            {
                var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
                if (string.IsNullOrEmpty(remoteIp) || !_ipWhitelist.Contains(remoteIp))
                {
                    _log.LogWarning("Webhook rejected: IP {ip} not in whitelist", remoteIp);
                    return Unauthorized();
                }
            }

            // 2) Leer body raw (necesario para HMAC)
            Request.EnableBuffering(); // permite re-leer stream
            using var sr = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await sr.ReadToEndAsync();
            Request.Body.Position = 0;

            // 3) Validar token (fallback)
            if (!string.IsNullOrEmpty(_inboundToken))
            {
                if (!Request.Headers.TryGetValue("X-Webhook-Token", out var tokenHeader) ||
                    tokenHeader != _inboundToken)
                {
                    _log.LogWarning("Webhook rejected: invalid inbound token");
                    return Unauthorized();
                }
            }

            // 4) Validar HMAC (si está configurado)
            if (!string.IsNullOrEmpty(_hmacSecret))
            {
                if (!Request.Headers.TryGetValue("X-Signature", out var signatureHeader))
                {
                    _log.LogWarning("Webhook rejected: missing signature header");
                    return Unauthorized();
                }
                var computed = ComputeHmacSha256(_hmacSecret, body);
                if (!FixedTimeEqualsHex(computed, signatureHeader))
                {
                    _log.LogWarning("Webhook rejected: invalid signature");
                    return Unauthorized();
                }
            }

            // Paso Intermedio , mapear envelope
            var envelope = MapEvolutionToEnvelope(body);

            if (envelope == null)
            {
                _log.LogWarning("Evolution payload inválido, guardando RAW sin thread");
                await SaveRawEvolutionPayloadAsync(body, "unknown", ct);
                return Ok(new { status = "accepted_raw" });
            }

            // 🔹 Guardar RAW PAYLOAD (SIEMPRE)
            var rawId = await SaveRawPayloadAsync(body, ct);

            _log.LogInformation(
                "Evolution raw payload saved. Id={id}, Instance={instance}",
                rawId,
                Request.Headers["X-Instance"]);

            var snapshot = BuildSnapshot(body);

            if (snapshot == null)
            {
                _log.LogWarning("Could not build snapshot from evolution payload");
                return Ok(new { status = "accepted_raw" });
            }
            
            //Persistir (GUARDA) la informacion del snapshot creada anteriormente y guardarla en las tablas de Thread y Messages
            await PersistSnapshotAsync(snapshot);


            // 5) Reutilizar el envelope ya mapeado anteriormente
            // No necesitamos llamar a MapEvolutionToIncoming()
            if (envelope == null)
            {
                _log.LogWarning("No hay un envelope válido para encolar.");
                return Ok(new { status = "accepted_raw" });
            }

            // Convertimos el envelope a IncomingMessageDto (el objeto que entiende tu cola)
            // Usamos tu constructor de IncomingMessageDto con los datos ya procesados del envelope
            var incoming = new IncomingMessageDto(
                 threadId: envelope.ThreadId,
                 businessAccountId: envelope.BusinessAccountId,
                 sender: envelope.CustomerPhone ?? "unknown",
                 displayName: envelope.CustomerDisplayName ?? "",
                 text: envelope.Text ?? "",
                 timestamp: envelope.ExternalTimestamp,
                 directionIn: envelope.DirectionIn, // Aquí usamos el valor calculado (!fromMe)
                 ai: null,
                 action: "initial",
                 reason: "incoming_from_evolution",
                 title: (envelope.CustomerDisplayName ?? envelope.CustomerPhone)?.Split(' ').FirstOrDefault() ?? "Nuevo"
            );

            // 6) ACK rápido: contestar antes de processamento pesado
            //    Encolar procesamiento y retornar 200 Accepted (o 200 OK)
            try
            {
                await _queue.EnqueueAsync(incoming);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to enqueue webhook message");
                // 500 for queue problems
                return StatusCode(500, "enqueue_failed");
            }

            // Devolver 200 lo antes posible: Evolution espera status 200/2xx
            return Ok(new { status = "accepted" });
        }

        // Método de mapeo (añádelo dentro del controller)
        private IncomingEvolutionEnvelope? MapEvolutionToEnvelope(string rawBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;

                JsonElement dataElem = root.TryGetProperty("data", out var d) ? d : root;

                string? remoteJid = null;
                string? senderPn = null;
                string? pushName = null;
                string? messageText = null;
                string? externalMessageId = null;
                bool fromMe = false;
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (dataElem.TryGetProperty("key", out var key))
                {
                    if (key.TryGetProperty("remoteJid", out var rj)) remoteJid = rj.GetString();
                    if (key.TryGetProperty("id", out var id)) externalMessageId = id.GetString();
                    if (key.TryGetProperty("fromMe", out var fm)) fromMe = fm.GetBoolean();
                    if (key.TryGetProperty("senderPn", out var spn)) senderPn = spn.GetString();
                }

                if (root.TryGetProperty("pushName", out var pn))
                    pushName = pn.GetString();

                if (dataElem.TryGetProperty("messageTimestamp", out var ts) && ts.TryGetInt64(out var tsv))
                    timestamp = tsv;

                if (dataElem.TryGetProperty("message", out var msg))
                {
                    if (msg.TryGetProperty("conversation", out var c))
                        messageText = c.GetString();
                    else if (msg.TryGetProperty("extendedTextMessage", out var e) &&
                             e.TryGetProperty("text", out var t))
                        messageText = t.GetString();
                }

                // 2. LÓGICA DE EXTRACCIÓN DEL NÚMERO (Tu validación)
                // Buscamos en orden de prioridad el que contenga "@s.whatsapp.net"
                string? targetJid = null;

                if (remoteJid != null && remoteJid.Contains("@s.whatsapp.net"))
                    targetJid = remoteJid;
                else if (senderPn != null && senderPn.Contains("@s.whatsapp.net"))
                    targetJid = senderPn;
                else
                    targetJid = remoteJid; // Fallback al que sea que tengamos (podría ser el @lid)

                // Limpiar para obtener solo los dígitos
                var phone = targetJid?
                    .Replace("@s.whatsapp.net", "")
                    .Replace("@lid", "")
                    .Replace("@c.us", "");

                var threadId = $"wa:{phone}";

                return new IncomingEvolutionEnvelope
                {
                    ThreadId = threadId,
                    BusinessAccountId = root.GetProperty("instance").GetString() ?? "evolution",
                    CustomerPhone = phone,
                    CustomerDisplayName = pushName,
                    CustomerPlatformId = remoteJid,
                    Text = messageText,
                    LastMessagePreview = messageText?.Length > 200 ? messageText[..200] : messageText,
                    DirectionIn = !fromMe,
                    ExternalTimestamp = timestamp,
                    ExternalMessageId = externalMessageId,
                    UnreadCount = !fromMe ? 1 : 0,
                    RawPayloadJson = rawBody
                };
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to map Evolution payload to Envelope");
                return null;
            }
        }

        private IncomingMessageDto? MapEvolutionToIncoming(string rawBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;

                // Evolution trae el objeto bajo `data` (según tu log)
                JsonElement dataElem;
                if (root.TryGetProperty("data", out var d))
                    dataElem = d;
                else
                    dataElem = root; // fallback (por si ya es el message)

                // Buscar keys
                string? remoteJid = null;
                string? participant = null;
                string? instance = null;
                string? messageText = null;
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                bool directionIn = true;
                string? pushName = null;

                string? externalMessageId = null;
                bool fromMe = false;

                string? mediaUrl = null;
                string? mediaMime = null;
                

                if (dataElem.TryGetProperty("key", out var keyElem))
                {
                    if (keyElem.TryGetProperty("remoteJid", out var rj)) 
                        remoteJid = rj.GetString();
                    if (keyElem.TryGetProperty("id", out var mid))
                        externalMessageId = mid.GetString();
                    if (keyElem.TryGetProperty("participant", out var p)) 
                        participant = p.GetString();
                    if (keyElem.TryGetProperty("fromMe", out var fm))
                        fromMe = fm.GetBoolean();
                }

                if (root.TryGetProperty("instance", out var inst)) instance = inst.GetString();
                if (root.TryGetProperty("pushName", out var pn)) pushName = pn.GetString();

                // mensaje: puede estar en data.message.conversation o data.message.extendedTextMessage.text, etc.
                if (dataElem.TryGetProperty("message", out var msgElem))
                {
                    if (msgElem.TryGetProperty("conversation", out var conv))
                        messageText = conv.GetString();
                    else if (msgElem.TryGetProperty("extendedTextMessage", out var ext) && ext.TryGetProperty("text", out var extTxt))
                        messageText = extTxt.GetString();
                    else
                    {
                        // Buscar cualquier primer string value within message
                        foreach (var prop in msgElem.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.String)
                            {
                                messageText = prop.Value.GetString();
                                break;
                            }
                        }
                    }

                    //if (msgElem.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var tsv))
                    //    timestamp = tsv;
                    if (dataElem.TryGetProperty("messageTimestamp", out var mts) && mts.TryGetInt64(out var mtsv))
                    {
                        timestamp = mtsv;
                    }

                    if (msgElem.TryGetProperty("imageMessage", out var img))
                    {
                        mediaUrl = img.GetProperty("url").GetString();
                        mediaMime = img.GetProperty("mimetype").GetString();
                    }
                    else if (msgElem.TryGetProperty("documentMessage", out var docMsg))
                    {
                        mediaUrl = docMsg.GetProperty("url").GetString();
                        mediaMime = docMsg.GetProperty("mimetype").GetString();
                    }


                }

                // sender: en varios logs aparece "sender" separado del key.remoteJid
                string? sender = null;
                if (root.TryGetProperty("sender", out var s)) sender = s.GetString();
                if (string.IsNullOrEmpty(sender) && !string.IsNullOrEmpty(remoteJid))
                    sender = remoteJid;

                // Build a threadId similar to how you used before, fallback to remoteJid
                var threadId = $"{instance ?? "evolution"}:{/* businessAccountId? */ instance ?? "unknown"}:{remoteJid ?? sender ?? "unknown"}";

                var incoming = new IncomingMessageDto(
                    threadId: threadId,
                    businessAccountId: instance ?? "evolution",
                    sender: sender ?? remoteJid ?? "unknown",
                    displayName: pushName ?? "",
                    text: messageText ?? "",
                    timestamp: timestamp,
                    directionIn = !fromMe,
                    ai: null,
                    action: "initial",
                    reason: "incoming_from_evolution",
                    title: (pushName ?? sender)?.Split(' ').FirstOrDefault() ?? "Nuevo"
                );

                return incoming;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to map Evolution payload to IncomingMessageDto");
                return null;
            }
        }


        // Helpers
        private static string ComputeHmacSha256(string secret, string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string ComputeSha256(string raw)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes);
        }

        private static bool FixedTimeEqualsHex(string aHex, string bHex)
        {
            try
            {
                var a = Convert.FromHexString(aHex);
                var b = Convert.FromHexString(bHex.ToString());
                return CryptographicOperations.FixedTimeEquals(a, b);
            }
            catch
            {
                return false;
            }
        }

        private static long ReadUnixTimestamp(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetInt64();

            if (element.ValueKind == JsonValueKind.String &&
                long.TryParse(element.GetString(), out var value))
                return value;

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }


        //HELPER
        //Guardado cuando esta incorrecto el payloadraw
        private async Task SaveRawEvolutionPayloadAsync(
        string rawBody,
        string threadId,
        CancellationToken ct)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrmInboxDbContext>>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            db.EvolutionRawPayloads.Add(new EvolutionRawPayload
            {
                ThreadId = threadId,
                PayloadJson = rawBody,
                ReceivedUtc = DateTime.UtcNow,
                Source = "evolution",
                Processed = false
            });

            await db.SaveChangesAsync(ct);
        }

        // Guardado correcto del payloadraws
        private async Task<int> SaveRawPayloadAsync(string rawBody, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var data = root.TryGetProperty("data", out var d) ? d : root;

            string? instance = root.TryGetProperty("instance", out var i) ? i.GetString() : null;
            string? @event = root.TryGetProperty("event", out var e) ? e.GetString() : null;
            string? sender = root.TryGetProperty("sender", out var s) ? s.GetString() : null;
            string? messageType = data.TryGetProperty("messageType", out var mt) ? mt.GetString() : null;
            string? pushName = data.TryGetProperty("pushName", out var pn) ? pn.GetString() : null;

            bool? fromMe = null;
            string? remoteJid = null;
            string? senderPn = null; //  Nueva variable para el número real

            if (data.TryGetProperty("key", out var key))
            {
                if (key.TryGetProperty("fromMe", out var fm))
                    fromMe = fm.GetBoolean();

                if (key.TryGetProperty("remoteJid", out var rj))
                    remoteJid = rj.GetString();

                //  Extraemos también el senderPn si existe en la key
                if (key.TryGetProperty("senderPn", out var spn))
                    senderPn = spn.GetString();
            }

            //  LÓGICA DE UNIFICACIÓN (Igual a la de MapEvolutionToEnvelope)
            // Buscamos cuál de los campos contiene el número real (@s.whatsapp.net)
            string? targetJidForPhone = null;
            if (remoteJid != null && remoteJid.Contains("@s.whatsapp.net"))
                targetJidForPhone = remoteJid;
            else if (senderPn != null && senderPn.Contains("@s.whatsapp.net"))
                targetJidForPhone = senderPn;
            else
                targetJidForPhone = remoteJid; // Fallback

            // Extraer solo el número
            string? customerPhone = targetJidForPhone?
                .Replace("@s.whatsapp.net", "")
                .Replace("@lid", "")
                .Replace("@c.us", "");

            DateTime? messageDateUtc = null;
            if (data.TryGetProperty("messageTimestamp", out var tsElement))
            {
                var timestamp = ReadUnixTimestamp(tsElement);
                messageDateUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            }

            //  El ThreadId de auditoría debe ser consistente: wa:numero
            // Antes tenías instance:remoteJid, pero es mejor wa:phone para ligarlo fácil
            var threadId = $"wa:{customerPhone ?? "unknown"}";

            var entity = new EvolutionRawPayload
            {
                ThreadId = threadId,
                Source = "evolution",
                PayloadJson = rawBody,
                ReceivedUtc = DateTime.UtcNow,
                Processed = false,

                Instance = instance,
                Event = @event,
                MessageType = messageType,
                RemoteJid = remoteJid, // Guardamos el original (@lid si es el caso)
                FromMe = fromMe,
                Sender = sender,
                CustomerPhone = customerPhone, // Guardamos el número limpio
                CustomerDisplayName = pushName,
                MessageDateUtc = messageDateUtc,

                Notes = HttpContext.Connection.RemoteIpAddress?.ToString()
            };

            using var scope = HttpContext.RequestServices.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrmInboxDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            db.EvolutionRawPayloads.Add(entity);
            await db.SaveChangesAsync(ct);

            return entity.Id;
        }


        // Construye el Dto que se guardara en la tabla
        private EvolutionMessageSnapshotDto? BuildSnapshot(string rawBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;
                var data = root.TryGetProperty("data", out var d) ? d : root;

                // =========================
                // Identidad base
                // =========================
                if (!root.TryGetProperty("instance", out var instProp))
                    return null;

                var instance = instProp.GetString()!;
                var senderRoot = root.TryGetProperty("sender", out var s) ? s.GetString() : null;

                // key
                if (!data.TryGetProperty("key", out var key))
                    return null;

                if (!key.TryGetProperty("remoteJid", out var rj))
                    return null;

                var remoteJid = rj.GetString()!;

                // 🚩 EXTRAER senderPn PARA VALIDACIÓN DE LID
                string? senderPn = key.TryGetProperty("senderPn", out var spn) ? spn.GetString() : null;

                var fromMe = key.TryGetProperty("fromMe", out var fm) && fm.GetBoolean();
                var externalMessageId = key.TryGetProperty("id", out var kid) ? kid.GetString() : null;

                string? pushName = null;
                if (data.TryGetProperty("pushName", out var pn))
                    pushName = pn.GetString();

                // 🚩 LÓGICA DE EXTRACCIÓN DE NÚMERO (Validación @s.whatsapp.net)
                string? targetJidForPhone = null;
                if (remoteJid.Contains("@s.whatsapp.net"))
                    targetJidForPhone = remoteJid;
                else if (senderPn != null && senderPn.Contains("@s.whatsapp.net"))
                    targetJidForPhone = senderPn;
                else
                    targetJidForPhone = remoteJid;

                var customerPhone = targetJidForPhone?
                    .Replace("@s.whatsapp.net", "")
                    .Replace("@lid", "")
                    .Replace("@c.us", "");

                // =========================
                // timestamp (string o number)
                // =========================
                if (!data.TryGetProperty("messageTimestamp", out var tsElement))
                    return null;

                var timestamp = ReadUnixTimestamp(tsElement);
                var createdUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

                // =========================
                // Source
                // =========================
                var source =
                    data.TryGetProperty("source", out var src) ? src.GetString() :
                    root.TryGetProperty("source", out var src2) ? src2.GetString() :
                    null;

                // =========================
                // Mensaje
                // =========================
                var messageType =
                    data.TryGetProperty("messageType", out var mt) ? mt.GetString() :
                    data.TryGetProperty("type", out var t) ? t.GetString() :
                    "unknown";

                if (!data.TryGetProperty("message", out var message))
                {
                    _log.LogWarning("Payload without message node");
                    return null;
                }

                string textPreview;
                string? text = null;
                string? mediaUrl = null;
                string? mediaMime = null;
                string? mediaCaption = null;
                MessageKind messageKind;

                string? mediaKey = null;
                string? fileSha256 = null;
                string? fileEncSha256 = null;
                string? directPath = null;
                long? mediaKeyTimestamp = null;
                string? fileName = null;
                long? fileLength = null;
                int? pageCount = null;
                string? thumbnailBase64 = null;
                string? mediaType = null;

                switch (messageType)
                {
                    case "conversation":
                        messageKind = MessageKind.Text;
                        text = message.GetProperty("conversation").GetString();
                        textPreview = text ?? "";
                        break;

                    case "imageMessage":
                        {
                            messageKind = MessageKind.Image;
                            mediaType = "image";
                            var img = message.GetProperty("imageMessage");
                            mediaUrl = img.GetProperty("url").GetString();
                            mediaMime = img.GetProperty("mimetype").GetString();
                            mediaCaption = img.TryGetProperty("caption", out var ic) ? ic.GetString() : null;
                            mediaKey = img.GetProperty("mediaKey").GetString();
                            fileSha256 = img.GetProperty("fileSha256").GetString();
                            fileEncSha256 = img.GetProperty("fileEncSha256").GetString();
                            directPath = img.GetProperty("directPath").GetString();
                            mediaKeyTimestamp = img.TryGetProperty("mediaKeyTimestamp", out var mts) ? mts.GetInt64() : null;
                            fileLength = img.TryGetProperty("fileLength", out var fl) ? fl.GetInt64() : null;
                            thumbnailBase64 = img.TryGetProperty("jpegThumbnail", out var jt) ? jt.GetString() : null;
                            textPreview = "[Imagen]";
                            break;
                        }

                    case "audioMessage":
                        {
                            messageKind = MessageKind.Audio;
                            mediaType = "audio";
                            var aud = message.GetProperty("audioMessage");
                            mediaUrl = aud.GetProperty("url").GetString();
                            mediaMime = aud.GetProperty("mimetype").GetString();
                            mediaKey = aud.GetProperty("mediaKey").GetString();
                            fileSha256 = aud.GetProperty("fileSha256").GetString();
                            fileEncSha256 = aud.GetProperty("fileEncSha256").GetString();
                            directPath = aud.GetProperty("directPath").GetString();
                            mediaKeyTimestamp = aud.TryGetProperty("mediaKeyTimestamp", out var mts) ? mts.GetInt64() : null;
                            fileLength = aud.TryGetProperty("fileLength", out var fl) ? fl.GetInt64() : null;
                            textPreview = "[Audio]";
                            break;
                        }

                    case "documentMessage":
                        {
                            messageKind = MessageKind.Document;
                            mediaType = "document";
                            var docu = message.GetProperty("documentMessage");
                            mediaUrl = docu.GetProperty("url").GetString();
                            mediaMime = docu.GetProperty("mimetype").GetString();
                            mediaCaption = docu.TryGetProperty("title", out var title) ? title.GetString() : null;
                            mediaKey = docu.TryGetProperty("mediaKey", out var mk) ? mk.GetString() : null;
                            fileSha256 = docu.TryGetProperty("fileSha256", out var fsh) ? fsh.GetString() : null;
                            fileEncSha256 = docu.TryGetProperty("fileEncSha256", out var feh) ? feh.GetString() : null;
                            directPath = docu.TryGetProperty("directPath", out var dp) ? dp.GetString() : null;
                            if (docu.TryGetProperty("mediaKeyTimestamp", out var mts))
                                mediaKeyTimestamp = ReadUnixTimestamp(mts);
                            fileName = docu.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
                            if (docu.TryGetProperty("fileLength", out var fl))
                            {
                                if (fl.ValueKind == JsonValueKind.String && long.TryParse(fl.GetString(), out var len))
                                    fileLength = len;
                                else if (fl.ValueKind == JsonValueKind.Number)
                                    fileLength = fl.GetInt64();
                            }
                            pageCount = docu.TryGetProperty("pageCount", out var pc) && pc.ValueKind == JsonValueKind.Number ? pc.GetInt32() : null;
                            thumbnailBase64 = docu.TryGetProperty("jpegThumbnail", out var jt) ? jt.GetString() : null;
                            textPreview = "[Documento]";
                            break;
                        }

                    case "stickerMessage":
                        {
                            messageKind = MessageKind.Sticker;
                            mediaType = "sticker";
                            var stk = message.GetProperty("stickerMessage");
                            mediaUrl = stk.TryGetProperty("url", out var url) ? url.GetString() : null;
                            mediaMime = stk.TryGetProperty("mimetype", out var mime) ? mime.GetString() : "image/webp";
                            mediaKey = stk.TryGetProperty("mediaKey", out var mk) ? mk.GetString() : null;
                            fileSha256 = stk.TryGetProperty("fileSha256", out var fsh) ? fsh.GetString() : null;
                            fileEncSha256 = stk.TryGetProperty("fileEncSha256", out var feh) ? feh.GetString() : null;
                            directPath = stk.TryGetProperty("directPath", out var dp) ? dp.GetString() : null;
                            if (stk.TryGetProperty("mediaKeyTimestamp", out var mts))
                            {
                                if (mts.ValueKind == JsonValueKind.String && long.TryParse(mts.GetString(), out var ts))
                                    mediaKeyTimestamp = ts;
                                else if (mts.ValueKind == JsonValueKind.Number)
                                    mediaKeyTimestamp = mts.GetInt64();
                            }
                            if (stk.TryGetProperty("fileLength", out var fl))
                            {
                                if (fl.ValueKind == JsonValueKind.String && long.TryParse(fl.GetString(), out var len))
                                    fileLength = len;
                                else if (fl.ValueKind == JsonValueKind.Number)
                                    fileLength = fl.GetInt64();
                            }
                            mediaCaption = null;
                            textPreview = "[Sticker]";
                            break;
                        }

                    default:
                        messageKind = MessageKind.Text;
                        textPreview = "[Mensaje]";
                        break;
                }

                // =========================
                // Construcción del snapshot
                // =========================
                return new EvolutionMessageSnapshotDto
                {
                    ThreadId = $"{instance}:{remoteJid}",
                    BusinessAccountId = instance,

                    Sender = senderRoot ?? remoteJid,
                    CustomerPhone = customerPhone, // 🚩 Número real extraído
                    CustomerDisplayName = pushName,
                    DirectionIn = !fromMe,
                    MessageKind = messageKind,
                    MessageType = messageType,
                    Text = text,
                    TextPreview = textPreview,
                    MediaUrl = mediaUrl,
                    MediaMime = mediaMime,
                    MediaCaption = mediaCaption,
                    MediaType = mediaType,

                    MediaKey = mediaKey,
                    FileSha256 = fileSha256,
                    FileEncSha256 = fileEncSha256,
                    DirectPath = directPath,
                    MediaKeyTimestamp = mediaKeyTimestamp,
                    FileName = fileName,
                    FileLength = fileLength,
                    PageCount = pageCount,
                    ThumbnailBase64 = thumbnailBase64,

                    ExternalMessageId = externalMessageId,
                    ExternalTimestamp = timestamp,
                    Source = source,
                    RawPayloadJson = rawBody,
                    CreatedAtUtc = createdUtc
                };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "BuildSnapshot failed");
                return null;
            }
        }

        //Ya con estos guarda la informacion en las tablas de thread y messages (OBTIENE Y/O CREA EL THREAD)
        private async Task<CrmThread> GetOrCreateThreadAsync(EvolutionMessageSnapshotDto snap, CancellationToken ct = default)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrmInboxDbContext>>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var thread = await db.CrmThreads
                .FirstOrDefaultAsync(t => t.ThreadId == snap.ThreadId, ct);

            if (thread != null)
            {
                thread.LastMessageUtc = snap.CreatedAtUtc;
                thread.LastMessagePreview = snap.TextPreview;

                if (snap.DirectionIn)
                    thread.UnreadCount += 1;

                await db.SaveChangesAsync();
                return thread;
    
            }

            thread = new CrmThread
            {
                ThreadId = snap.ThreadId,
                BusinessAccountId = snap.BusinessAccountId,
                Channel = 1, // WhatsApp
                ThreadKey = snap.CustomerPhone,
                CustomerDisplayName = snap.CustomerDisplayName,
                CustomerPhone = snap.CustomerPhone,
                CustomerPlatformId = snap.CustomerPhone,
                CreatedUtc = snap.CreatedAtUtc,
                LastMessageUtc = snap.CreatedAtUtc,
                LastMessagePreview = snap.TextPreview,
                UnreadCount = snap.DirectionIn ? 1 : 0,
                Status = 0,
                MainParticipant = snap.CustomerPhone
            };

            db.CrmThreads.Add(thread);
            await db.SaveChangesAsync(ct);

            return thread;
        }

        // Valida que el mensaje no exista en la tabla, Regresa el mensaje si existe
        private async Task<bool> MessageExistsAsync(EvolutionMessageSnapshotDto snap, int threadId, CancellationToken ct = default)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrmInboxDbContext>>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            if (!string.IsNullOrEmpty(snap.ExternalMessageId))
            {
                return await db.CrmMessages.AnyAsync(m =>
                    m.ThreadRefId == threadId &&
                    m.ExternalId == snap.ExternalMessageId, ct);
            }

            var hash = ComputeSha256(snap.RawPayloadJson);

            return await db.CrmMessages.AnyAsync(m =>
                m.ThreadRefId == threadId &&
                m.RawHash == hash, ct);
        }

        //Inserta Valores  Media en la tabla (CrmMessagesMedias)
        private async Task InsertMediaAsync(CrmMessage msg, EvolutionMessageSnapshotDto s, CancellationToken ct = default)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrmInboxDbContext>>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            if (!msg.HasMedia) return;

            var media = new CrmMessageMedia
            {
                MessageId = msg.Id,
                MediaType = s.MessageType,
                MimeType = s.MediaMime,
                MediaUrl = s.MediaUrl,
                MediaKey = s.MediaKey,
                FileSha256 = s.FileSha256,
                FileEncSha256 = s.FileEncSha256,
                DirectPath = s.DirectPath,
                MediaKeyTimestamp = s.MediaKeyTimestamp,
                FileName = s.FileName,
                FileLength = s.FileLength,
                PageCount = s.PageCount,
                ThumbnailBase64 = s.ThumbnailBase64,
                CreatedUtc = DateTime.UtcNow
            };

            db.CrmMessageMedias.Add(media);
            await db.SaveChangesAsync();
        }

        //Notifica SignalR "NewMessage"
        private async Task NotifySignalRAsync(CrmThread thread, CrmMessage msg)
        {
            if (string.IsNullOrEmpty(thread.BusinessAccountId))
                return;

            // Si en el log ves que llega vacío o diferente, ahí está el error.
            var grupoDestino = thread.BusinessAccountId ?? "ChapultepecEvo";

            await _hubContext.Clients
                .Group(grupoDestino)
                .SendAsync("NewMessage", new
                {

                    ThreadId = thread.ThreadId,
                    ThreadDbId = thread.Id,
                    MessageId = msg.Id,
                    Sender = msg.Sender,
                    DisplayName = msg.DisplayName,
                    Text = msg.Text,
                    MessageKind = msg.MessageKind,
                    MediaUrl = msg.MediaUrl,
                    CreatedUtc = msg.TimestampUtc,
                    DirectionIn = msg.DirectionIn
                });
        }

        //Guarda en la tabla  Mensajes (CrmMessages), manda a llamar si tiene Medias (inserta medias) /Notifica SignalR
        private async Task PersistSnapshotAsync(EvolutionMessageSnapshotDto snap, CancellationToken ct = default)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrmInboxDbContext>>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var thread = await GetOrCreateThreadAsync(snap, ct);

            if (await MessageExistsAsync(snap, thread.Id, ct))
                return;

            var rawHash = ComputeSha256(snap.RawPayloadJson);

            var message = new CrmMessage
            {
                ThreadRefId = thread.Id,
                Sender = snap.Sender,
                DisplayName = snap.CustomerDisplayName,
                Text = snap.Text,
                TimestampUtc = snap.CreatedAtUtc,
                ExternalTimestamp = snap.ExternalTimestamp,
                DirectionIn = snap.DirectionIn,

                MediaUrl = snap.MediaUrl,
                MediaMime = snap.MediaMime,
                MediaCaption = snap.MediaCaption,
                MediaType = snap.MessageType,

                RawPayload = snap.RawPayloadJson,
                ExternalId = snap.ExternalMessageId,
                WaMessageId = snap.ExternalMessageId,
                RawHash = rawHash,

                MessageKind = (int)snap.MessageKind,
                HasMedia = snap.MediaUrl != null,

                CreatedUtc = DateTime.UtcNow
            };

            db.CrmMessages.Add(message);

            // =========================
            // Update Thread state
            // =========================
            thread.LastMessageUtc = snap.CreatedAtUtc;
            thread.LastMessagePreview = snap.TextPreview;

            if (snap.DirectionIn)
                thread.UnreadCount += 1;

            await db.SaveChangesAsync(ct);

            //var msg = await InsertMessageAsync(thread, snap, ct);
            await InsertMediaAsync(message, snap, ct);

            // Notifica SignalR
            await NotifySignalRAsync(thread, message);
        }



    }
}
