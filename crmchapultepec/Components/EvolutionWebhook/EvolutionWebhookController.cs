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
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static Azure.Core.HttpHeader;
using static MudBlazor.Colors;

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

        [HttpGet("view-sticker/{messageId}")]
        public async Task<IActionResult> ViewSticker(int messageId, CancellationToken ct)
        {
            // 1. Buscar la media asociada al mensaje
            var media = await _mediaService.GetByMessageIdAsync(messageId, ct);
            if (media == null) return NotFound();

            // 2. Desencriptar (Esto obtiene los bytes del .webp)
            var bytes = await _mediaService.DecryptMedia(media, ct);
            if (bytes == null) return BadRequest();

            // 3. Retornar como imagen WebP para que el navegador la pinte
            return File(bytes, "image/webp");
        }

        // Endpoint para descargar/desencriptar un archivo por Id
        [HttpGet("download/{messageId}")]
        public async Task<IActionResult> Download(int messageId, CancellationToken ct)
        {
            var media = await _mediaService.GetByIdAsync(messageId, ct);
            if (media == null) return NotFound();

            var bytes = await _mediaService.DecryptMedia(media, ct);
            if (bytes == null) return BadRequest("No se pudo desencriptar el archivo");
            
            var mime = media.MediaType?.ToLower() switch
            {
                "image" or "imagemessage" => "image/jpeg", //  Añadido imagemessage
                "sticker" or "stickermessage" => "image/webp",
                "document" or "documentmessage" => "application/pdf",
                "audio" or "audiomessage" => "audio/ogg", // Importante para notas de voz
                "video" or "videomessage" => "video/mp4",
                _ => "image/jpeg" // Fallback a imagen si es desconocido para intentar renderizar
            };

            // Si es imagen o sticker, lo enviamos SIN el nombre del archivo 
            // para que el navegador lo renderice en el <img>
            if (media.MediaType == "image" || media.MediaType == "sticker")
            {
                return File(bytes, mime);
            }

            // Si es Audio o Video, no forzamos descarga con nombre, permitimos streaming en el navegador
            if (mime.StartsWith("audio/") || mime.StartsWith("video/") || mime.StartsWith("image/"))
            {
                return File(bytes, mime);
            }

            // Si es documento, forzamos que el navegador lo descargue con su nombre original
            if (media.MediaType?.Contains("document") == true)
            {
                return File(bytes, mime, media.FileName ?? "archivo.pdf");
            }

            // Para documentos, mantenemos la descarga
            return File(bytes, mime, media.FileName);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromServices] IWebhookControlService toggle, CancellationToken ct)
        {          

            //  Switch General para apagar los insert de Evolution API
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

                // --- LÓGICA DE UNIFICACIÓN DE LID ---
                string? finalPhone = null;
                string? finalLid = null;

                if (remoteJid != null)
                {
                    if (remoteJid.Contains("@s.whatsapp.net"))
                    {
                        finalPhone = remoteJid.Replace("@s.whatsapp.net", "").Replace("@c.us", "");
                    }
                    else if (remoteJid.Contains("@lid"))
                    {
                        //  REGLA DE ORO: Si es LID, NO es teléfono.
                        finalLid = remoteJid;

                        // Solo si Evolution nos da el senderPn, tenemos el teléfono real
                        if (senderPn != null && senderPn.Contains("@s.whatsapp.net"))
                        {
                            finalPhone = senderPn.Replace("@s.whatsapp.net", "");
                        }
                    }
                }

                // El ThreadId debe ser amigable: si no hay teléfono, usamos el LID literal
                var threadId = !string.IsNullOrEmpty(finalPhone)
                               ? $"wa:{finalPhone}"
                               : $"wa:lid:{finalLid?.Replace("@lid", "")}";

                return new IncomingEvolutionEnvelope
                {
                    ThreadId = threadId,
                    BusinessAccountId = root.GetProperty("instance").GetString() ?? "evolution",
                    CustomerPhone = finalPhone,
                    CustomerDisplayName = pushName,
                    CustomerPlatformId = remoteJid,
                    Text = messageText,
                    LastMessagePreview = messageText?.Length > 200 ? messageText[..200] : messageText,
                    DirectionIn = !fromMe,
                    ExternalTimestamp = timestamp,
                    ExternalMessageId = externalMessageId,
                    UnreadCount = !fromMe ? 1 : 0,
                    RawPayloadJson = rawBody,
                    CustomerLid = finalLid
                };
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to map Evolution payload to Envelope");
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

            // --- LÓGICA DE UNIFICACIÓN Y LIMPIEZA ---
            string? finalPhone = null;
            string? finalLid = null;

            if (remoteJid != null)
            {
                if (remoteJid.Contains("@s.whatsapp.net"))
                {
                    finalPhone = remoteJid.Replace("@s.whatsapp.net", "").Replace("@c.us", "");
                }
                else if (remoteJid.Contains("@lid"))
                {
                    finalLid = remoteJid;
                    // Intentar rescatar el teléfono si viene en senderPn
                    if (senderPn != null && senderPn.Contains("@s.whatsapp.net"))
                    {
                        finalPhone = senderPn.Replace("@s.whatsapp.net", "");
                    }
                }
            }

            TimeZoneInfo mexicoZone;
            try
            {
                mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
            }
            catch
            {
                mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            }

            // 1. Declaramos la variable con un valor por defecto (por si el JSON no trae fecha)
            DateTime messageDate = DateTime.UtcNow;
            DateTime receivedLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mexicoZone);

            if (data.TryGetProperty("messageTimestamp", out var tsElement))
            {
                // 1. Obtener el timestamp del elemento
                var timestamp = ReadUnixTimestamp(tsElement);

                // 2. Convertir el Unix Timestamp a un DateTimeOffset en UTC
                var dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(timestamp);

                // 3. Definir la zona horaria de México (ajusta el ID según tu región si no es CDMX)
                // En Windows es "Central Standard Time (Mexico)", en Linux/Docker suele ser "America/Mexico_City"
                

                // 4. Convertir a la hora local de esa zona
                messageDate = TimeZoneInfo.ConvertTimeFromUtc(dateTimeOffset.UtcDateTime, mexicoZone);
            }

            //  El ThreadId de auditoría debe ser consistente: wa:numero
            // Antes tenías instance:remoteJid, pero es mejor wa:phone para ligarlo fácil
            // El ThreadId de auditoría ahora sigue la misma regla: wa:tel o wa:lid:id
            string auditThreadId = !string.IsNullOrEmpty(finalPhone)
                ? $"wa:{finalPhone}"
                : (finalLid != null ? $"wa:lid:{finalLid.Replace("@lid", "")}" : "wa:unknown");

            var entity = new EvolutionRawPayload
            {
                ThreadId = auditThreadId,
                Source = "evolution",
                PayloadJson = rawBody,
                ReceivedUtc = receivedLocal,
                Processed = false,

                Instance = instance,
                Event = @event,
                MessageType = messageType,
                RemoteJid = remoteJid, // Guardamos el original (@lid si es el caso)
                FromMe = fromMe,
                Sender = sender,
                CustomerPhone = finalPhone, // Guardamos el número limpio
                CustomerDisplayName = pushName,
                MessageDateUtc = messageDate,

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

                //  EXTRAER senderPn PARA VALIDACIÓN DE LID
                string? senderPn = key.TryGetProperty("senderPn", out var spn) ? spn.GetString() : null;

                var fromMe = key.TryGetProperty("fromMe", out var fm) && fm.GetBoolean();
                var externalMessageId = key.TryGetProperty("id", out var kid) ? kid.GetString() : null;

                string? pushName = null;
                if (data.TryGetProperty("pushName", out var pn))
                    pushName = pn.GetString();

                //  DETECCIÓN AVANZADA DE ORIGEN (Prospecto vs Contacto LID)
                bool isFromAd = false;
                if (data.TryGetProperty("contextInfo", out var context))
                {
                    // Verificamos si Meta marcó esto como un saludo automático de anuncio
                    if (context.TryGetProperty("automatedGreetingMessageShown", out var autoGreet))
                        isFromAd = autoGreet.GetBoolean();

                    // Verificamos si existe información de Ad Reply (Facebook/Instagram Ads)
                    if (!isFromAd && context.TryGetProperty("externalAdReply", out var adReply))
                        isFromAd = true;
                }

                //  LÓGICA DE EXTRACCIÓN DE NÚMERO (Validación @s.whatsapp.net)
                //  LÓGICA DE IDENTIDAD (UNIFICADA)
                string? finalPhone = null;
                string? finalLid = null;

                if (remoteJid.Contains("@s.whatsapp.net"))
                {
                    finalPhone = remoteJid.Replace("@s.whatsapp.net", "").Replace("@c.us", "");
                }
                else if (remoteJid.Contains("@lid"))
                {
                    finalLid = remoteJid;
                    if (senderPn != null && senderPn.Contains("@s.whatsapp.net"))
                        finalPhone = senderPn.Replace("@s.whatsapp.net", "");
                }

                // DETERMINACIÓN DEL NOMBRE MOSTRADO
                // Regla: 
                // 1. Si es un mensaje saliente (fromMe), el pushName es el tuyo, así que NO lo usamos para el cliente.
                // 2. Si es entrante y es anuncio -> "Prospecto de Anuncio"
                // 3. Si es entrante y es LID pero no anuncio -> "Contacto LID (Web/Otro)"
                // 4. Si tenemos PushName del cliente (cuando es entrante), usamos ese.

                string? computedDisplayName = pushName;

                if (fromMe)
                {
                    // No queremos que el chat se llame "Mario Diaz" (tu nombre) solo porque tú iniciaste
                    computedDisplayName = isFromAd ? "Prospecto de Anuncio" : "Contacto LID (Pendiente)";
                }
                else
                {
                    // Es un mensaje que VIENE del cliente
                    if (string.IsNullOrEmpty(pushName))
                    {
                        computedDisplayName = isFromAd ? "Prospecto de Anuncio" : "Contacto LID";
                    }
                }

                // El ThreadId del snapshot debe ser consistente con la búsqueda
                var threadId = !string.IsNullOrEmpty(finalPhone)
                    ? $"wa:{finalPhone}"
                    : $"wa:lid:{finalLid?.Replace("@lid", "")}";

                // =========================
                // timestamp (string o number)
                // =========================
                if (!data.TryGetProperty("messageTimestamp", out var tsElement))
                    return null;

                // 1. Definir la zona horaria (igual que en el método anterior)
                TimeZoneInfo mexicoZone;
                try
                {
                    mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
                }
                catch
                {
                    mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
                }

                // 2. Obtener el timestamp y convertir
                var timestamp = ReadUnixTimestamp(tsElement);

                // 3. Convertir de Unix a UTC y luego a la hora local de México
                var utcDateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
                var createdLocal = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, mexicoZone);

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

                            // Accedemos al nodo imageMessage
                            if (!message.TryGetProperty("imageMessage", out var img))
                            {
                                _log.LogWarning("Payload marcado como imageMessage pero no se encontró el nodo interno.");
                                return null;
                            }

                            // Extraer URLs (Evolution a veces pone la URL de descarga directa aquí)
                            var rawUrl = img.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                            var dPath = img.TryGetProperty("directPath", out var dpProp) ? dpProp.GetString() : null;

                            // Si la URL es de web.whatsapp (caduca rápido), preferimos armar la de mmg con el directPath
                            if ((string.IsNullOrEmpty(rawUrl) || rawUrl.Contains("web.whatsapp.net")) && !string.IsNullOrEmpty(dPath))
                                mediaUrl = $"https://mmg.whatsapp.net{dPath}";
                            else
                                mediaUrl = rawUrl;

                            // Metadatos para desencriptar (Cruciales)
                            mediaMime = img.TryGetProperty("mimetype", out var mime) ? mime.GetString() : "image/jpeg";
                            mediaKey = img.TryGetProperty("mediaKey", out var mk) ? mk.GetString() : null;
                            fileSha256 = img.TryGetProperty("fileSha256", out var fsh) ? fsh.GetString() : null;
                            fileEncSha256 = img.TryGetProperty("fileEncSha256", out var feh) ? feh.GetString() : null;
                            directPath = dPath;

                            // Caption (Si la imagen lleva texto abajo)
                            mediaCaption = img.TryGetProperty("caption", out var cap) ? cap.GetString() : null;

                            // Thumbnail (Miniatura en Base64 que viene en el JSON)
                            thumbnailBase64 = img.TryGetProperty("jpegThumbnail", out var thumb) ? thumb.GetString() : null;

                            // Tamaño del archivo
                            if (img.TryGetProperty("fileLength", out var fl))
                            {
                                if (fl.ValueKind == JsonValueKind.String && long.TryParse(fl.GetString(), out var len))
                                    fileLength = len;
                                else if (fl.ValueKind == JsonValueKind.Number)
                                    fileLength = fl.GetInt64();
                            }

                            // Preview para la lista de chats
                            textPreview = !string.IsNullOrEmpty(mediaCaption) ? $"📷 {mediaCaption}" : "📷 Foto";
                            text = mediaCaption; // Guardamos el caption como el texto del mensaje
                            break;
                        }

                    case "audioMessage":
                        {
                            messageKind = MessageKind.Audio; // Asegúrate que tu Enum MessageKind tenga Audio
                            mediaType = "audio";

                            // Acceder al nodo audioMessage
                            // Intentamos obtener el nodo de audio de forma segura
                            // A veces viene dentro de 'message', a veces 'message' es el objeto directamente
                            JsonElement aud;
                            if (message.TryGetProperty("audioMessage", out var audNode))
                            {
                                aud = audNode;
                            }
                            else
                            {
                                aud = message;
                            }

                            var rawUrl = aud.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                            var dPath = aud.TryGetProperty("directPath", out var dpProp) ? dpProp.GetString() : null;

                            if (!string.IsNullOrEmpty(dPath) && (!rawUrl?.Contains("mmg") ?? true))
                                mediaUrl = $"https://mmg.whatsapp.net{dPath}";
                            else
                                mediaUrl = rawUrl;

                            mediaMime = aud.TryGetProperty("mimetype", out var mime) ? mime.GetString() : "audio/ogg; codecs=opus";
                            mediaKey = aud.TryGetProperty("mediaKey", out var mk) ? mk.GetString() : null;
                            fileSha256 = aud.TryGetProperty("fileSha256", out var fsh) ? fsh.GetString() : null;
                            fileEncSha256 = aud.TryGetProperty("fileEncSha256", out var feh) ? feh.GetString() : null;
                            directPath = dPath;

                            if (aud.TryGetProperty("fileLength", out var fl))
                                fileLength = fl.ValueKind == JsonValueKind.Number ? fl.GetInt64() : (long.TryParse(fl.GetString(), out var len) ? len : 0);

                            textPreview = "🎤 Nota de voz";
                            break;
                        }

                    

                    case "videoMessage":
                        {
                            messageKind = MessageKind.Video; // Asegúrate de tener 'Video' en tu Enum MessageKind
                            mediaType = "video";

                            if (!message.TryGetProperty("videoMessage", out var vid)) return null;

                            // Extraer URL (Prioridad al DirectPath para evitar expiración)
                            var rawUrl = vid.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                            var dPath = vid.TryGetProperty("directPath", out var dpProp) ? dpProp.GetString() : null;

                            if (!string.IsNullOrEmpty(dPath))
                                mediaUrl = $"https://mmg.whatsapp.net{dPath}";
                            else
                                mediaUrl = rawUrl;

                            // Metadatos cruciales
                            mediaMime = vid.TryGetProperty("mimetype", out var mime) ? mime.GetString() : "video/mp4";
                            mediaKey = vid.TryGetProperty("mediaKey", out var mk) ? mk.GetString() : null;
                            fileSha256 = vid.TryGetProperty("fileSha256", out var fsh) ? fsh.GetString() : null;
                            fileEncSha256 = vid.TryGetProperty("fileEncSha256", out var feh) ? feh.GetString() : null;
                            directPath = dPath;

                            // Caption del video
                            mediaCaption = vid.TryGetProperty("caption", out var cap) ? cap.GetString() : null;

                            if (vid.TryGetProperty("fileLength", out var fl))
                                fileLength = fl.ValueKind == JsonValueKind.Number ? fl.GetInt64() : 0;

                            textPreview = !string.IsNullOrEmpty(mediaCaption) ? $"🎥 {mediaCaption}" : "🎥 Video";
                            text = mediaCaption;
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

                            // Intentamos obtener el nodo de diferentes formas por si Evolution cambia la estructura
                            JsonElement stk;
                            if (message.TryGetProperty("stickerMessage", out var s1)) stk = s1;
                            else stk = message; // Fallback si el nodo ya es el sticker

                            var rawUrl = stk.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                            var dPath = stk.TryGetProperty("directPath", out var dpProp) ? dpProp.GetString() : null;

                            //mediaUrl = stk.TryGetProperty("url", out var url) ? url.GetString() : null;
                            // Si la url apunta a web o está vacía, pero tenemos directPath
                            if ((string.IsNullOrEmpty(rawUrl) || rawUrl.Contains("web.whatsapp.net")) && !string.IsNullOrEmpty(dPath))
                            {
                                // El host estándar para archivos multimedia es mmg.whatsapp.net
                                mediaUrl = $"https://mmg.whatsapp.net{dPath}";
                            }
                            else
                            {
                                mediaUrl = rawUrl;
                            }

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
                    case "reactionMessage":
                        {
                            messageKind = MessageKind.Text; // O puedes crear MessageKind.Reaction si prefieres
                            var reaction = message.GetProperty("reactionMessage");

                            // El emoji enviado
                            text = reaction.TryGetProperty("text", out var emoji) ? emoji.GetString() : "";

                            // ID del mensaje al que se reacciona (útil para mostrarlo en la burbuja correcta)
                            var targetMessageId = reaction.GetProperty("key").GetProperty("id").GetString();

                            textPreview = $"Reaccionó {text} a un mensaje";

                            // Opcional: Puedes guardar el ID del mensaje original en una columna de "Notas" 
                            // o "ReplyTo" si tu tabla lo permite.
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
                    ThreadId = threadId,
                    BusinessAccountId = instance,

                    Sender = (fromMe && !string.IsNullOrEmpty(senderPn)) ? senderPn : (senderRoot ?? remoteJid),
                    CustomerPhone = finalPhone, //  Número real extraído
                    CustomerLid = finalLid,
                    CustomerDisplayName = computedDisplayName, //  Nombre inteligente
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
                    CreatedAtUtc = createdLocal
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

            //var thread = await db.CrmThreads
            //    .FirstOrDefaultAsync(t => t.ThreadId == snap.ThreadId, ct);

            // 1. BUSQUEDA DUAL: Intentamos encontrar el hilo por LID o por el nuevo JID Real
            var thread = await db.CrmThreads.FirstOrDefaultAsync(t =>
                (snap.CustomerLid != null && t.CustomerLid == snap.CustomerLid) ||
                (!string.IsNullOrEmpty(snap.CustomerPhone) && t.CustomerPhone == snap.CustomerPhone), ct);

            if (thread != null)
            {
                //  REGLA DE ORO: Si ya tenemos el nombre real del cliente (PushName), hay que usarlo.
                // Solo actualizamos si el nombre en DB es genérico o si el snap trae un nombre nuevo/mejor.
                bool isGenericName = thread.CustomerDisplayName == "Contacto LID (Pendiente)" ||
                                     thread.CustomerDisplayName == "Contacto LID" ||
                                     thread.CustomerDisplayName == "Prospecto de Anuncio";

                if (isGenericName && !string.IsNullOrEmpty(snap.CustomerDisplayName))
                {
                    thread.CustomerDisplayName = snap.CustomerDisplayName;
                }

                //  ESCENARIO DE CONVERSIÓN/UNIFICACIÓN
                // Si el hilo en DB no tiene teléfono (era solo LID) 
                // pero el mensaje de ahora SÍ trae un teléfono real (snap.CustomerPhone)
                if (string.IsNullOrEmpty(thread.CustomerPhone) && !string.IsNullOrEmpty(snap.CustomerPhone))
                {
                    // "Promovemos" el hilo de LID a WhatsApp Real
                    thread.ThreadId = snap.ThreadId;      // wa:lid:xxx -> wa:521xxx
                    thread.CustomerPhone = snap.CustomerPhone;
                    thread.ThreadKey = snap.CustomerPhone;
                    thread.MainParticipant = snap.CustomerPhone;
                    thread.CustomerPlatformId = snap.CustomerPhone + "@s.whatsapp.net"; // Reconstruimos el JID

                    // Si además trae un nombre, asegurarnos de que lo tome
                    if (!string.IsNullOrEmpty(snap.CustomerDisplayName))
                    {
                        thread.CustomerDisplayName = snap.CustomerDisplayName;
                    }
                }

                // Actualización normal de mensajes
                thread.LastMessageUtc = snap.CreatedAtUtc;
                thread.LastMessagePreview = snap.TextPreview;
                if (snap.DirectionIn) thread.UnreadCount += 1;

                await db.SaveChangesAsync();
                return thread;
            }

            // 2. CREACIÓN DE HILO NUEVO
            // Si no se encontró nada, creamos el registro inicial
            bool isLidOnly = string.IsNullOrEmpty(snap.CustomerPhone) && !string.IsNullOrEmpty(snap.CustomerLid);

            thread = new CrmThread
            {
                ThreadId = snap.ThreadId,
                BusinessAccountId = snap.BusinessAccountId,
                Channel = 1, // WhatsApp
                             // Estos campos no deben tener el LID si quieres que la DB esté limpia
                ThreadKey = !string.IsNullOrEmpty(snap.CustomerPhone) ? snap.CustomerPhone : snap.CustomerLid,
                MainParticipant = !string.IsNullOrEmpty(snap.CustomerPhone) ? snap.CustomerPhone : snap.CustomerLid,
                CustomerDisplayName = snap.CustomerDisplayName,
                // 🚩 CAMPOS LIMPIOS
                CustomerPhone = snap.CustomerPhone,
                CustomerLid = snap.CustomerLid,

                // PlatformId: Si hay teléfono usamos el JID estándar, si no, el LID
                CustomerPlatformId = !string.IsNullOrEmpty(snap.CustomerPhone)
                             ? snap.CustomerPhone + "@s.whatsapp.net"
                             : snap.CustomerLid,

                CreatedUtc = snap.CreatedAtUtc,
                LastMessageUtc = snap.CreatedAtUtc,
                LastMessagePreview = snap.TextPreview,
                UnreadCount = snap.DirectionIn ? 1 : 0,
                Status = 0
                
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

            // 1. Obtener la zona horaria de México (puedes mover esto al inicio del método)
            TimeZoneInfo mexicoZone;
            try
            {
                mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
            }
            catch
            {
                mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            }

            // 2. Convertir la hora actual (NOW) a hora de México
            var nowMexico = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mexicoZone);

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
                CreatedUtc = nowMexico
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

            // LÓGICA DE REACCIONES A LOS MENSAJES
            if (snap.MessageType == "reactionMessage")
            {
                // 1. Extraer el ID del mensaje al que se reacciona
                using var doc = JsonDocument.Parse(snap.RawPayloadJson);
                var reactionNode = doc.RootElement.GetProperty("data").GetProperty("message").GetProperty("reactionMessage");
                
                // Obtenemos el ID del mensaje original
                var targetExternalId = reactionNode.GetProperty("key").GetProperty("id").GetString();

                // Obtenemos el emoji
                var emoji = reactionNode.TryGetProperty("text", out var textProp) ? textProp.GetString() : "";

                // 2. Buscar el mensaje original en la base de datos
                // Usamos ExternalId o WaMessageId según como lo guardes
                var originalMessage = await db.CrmMessages
                    .FirstOrDefaultAsync(m => m.ExternalId == targetExternalId && m.ThreadRefId == thread.Id, ct);

                if (originalMessage != null)
                {
                    // 3. Actualizar la reacción (si es un string vacío "" significa que quitaron la reacción)
                    originalMessage.Reaction = string.IsNullOrEmpty(emoji) ? null : emoji;

                    // 4. Actualizar el thread para que la lista muestre el preview de la reacción
                    thread.LastMessageUtc = DateTime.UtcNow;
                    thread.LastMessagePreview = $"Reaccionó {emoji}";

                    await db.SaveChangesAsync(ct);

                    // 5. Notificar a SignalR (Enviamos el mensaje original actualizado)
                    await NotifySignalRAsync(thread, originalMessage);
                }
                return; // Salimos para no insertar un mensaje nuevo
            }

            if (await MessageExistsAsync(snap, thread.Id, ct))
                return;

            var rawHash = ComputeSha256(snap.RawPayloadJson);     

            // 1. Obtener la zona horaria de México (puedes mover esto al inicio del método)
            TimeZoneInfo mexicoZone;
            try
            {
                mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
            }
            catch
            {
                mexicoZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            }

            // 2. Convertir la hora actual (NOW) a hora de México
            var nowMexico = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mexicoZone);

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

                CreatedUtc = nowMexico
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
