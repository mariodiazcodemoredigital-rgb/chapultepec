using crmchapultepec.data.Data;
using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.entities.EvolutionWebhook.crmchapultepec.data.Entities.EvolutionWebhook;
using crmchapultepec.services.Hubs;
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
        private readonly string? _hmacSecret;
        private readonly string? _inboundToken;
        private readonly string[] _ipWhitelist;

        public EvolutionWebhookController(
        ILogger<EvolutionWebhookController> log,
        IMessageQueue queue,
        IConfiguration cfg,
        IHubContext<CrmHub> hubContext)
        {
            _log = log;
            _queue = queue;
            _cfg = cfg;
            _hubContext = hubContext;
            _hmacSecret = cfg["Evolution:WebhookHmacSecret"];        // opcional
            _inboundToken = cfg["Evolution:WebhookInboundToken"];  // opcional
            _ipWhitelist = (cfg["Evolution:WebhookIpWhitelist"] ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            
            //Persistir la informacion del snapshot creada anteriormente y guardarla en las tablas de Thread y Messages
            await PersistSnapshotAsync(snapshot);

            // 🔹 Guardar RAW PAYLOAD (SIEMPRE)
            //await SaveRawEvolutionPayloadAsync(body, envelope.ThreadId, ct);




            // 5) Parsear payload
            IncomingMessageDto? dto;
            var mapped = MapEvolutionToIncoming(body);
            try
            {   
                if (mapped == null)
                {
                    // opcional: enqueue raw or return accepted_raw
                    _log.LogWarning("Could not map evolution payload to DTO; enqueuing raw or saving dead letter.");
                    // enqueue or handle accordingly
                    return Ok(new { status = "accepted_raw" });
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to deserialize webhook JSON");
                return BadRequest();
            }

            // 6) ACK rápido: contestar antes de processamento pesado
            //    Encolar procesamiento y retornar 200 Accepted (o 200 OK)
            try
            {
                await _queue.EnqueueAsync(mapped);
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

                var phone = remoteJid?
                    .Replace("@s.whatsapp.net", "")
                    .Replace("@lid", "");

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


        //helper
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

            if (data.TryGetProperty("key", out var key))
            {
                if (key.TryGetProperty("fromMe", out var fm))
                    fromMe = fm.GetBoolean();

                if (key.TryGetProperty("remoteJid", out var rj))
                    remoteJid = rj.GetString();
            }

            string? customerPhone = remoteJid?
                .Replace("@s.whatsapp.net", "")
                .Replace("@lid", "");

            DateTime? messageDateUtc = null;
            var tsElement = data.GetProperty("messageTimestamp");
            var timestamp = ReadUnixTimestamp(tsElement);


            //if (data.TryGetProperty("messageTimestamp", out var mts) &&
            //    mts.TryGetInt64(out var ts))
            //{
                messageDateUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            //}

            var threadId = $"{instance}:{remoteJid ?? sender ?? "unknown"}";

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
                RemoteJid = remoteJid,
                FromMe = fromMe,
                Sender = sender,
                CustomerPhone = customerPhone,
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
                var instance = root.GetProperty("instance").GetString()!;
                var senderRoot = root.TryGetProperty("sender", out var s) ? s.GetString() : null;

                var key = data.GetProperty("key");
                var remoteJid = key.GetProperty("remoteJid").GetString()!;
                var fromMe = key.GetProperty("fromMe").GetBoolean();
                var externalMessageId = key.GetProperty("id").GetString();

                var pushName = data.TryGetProperty("pushName", out var pn) ? pn.GetString() : null;

                var tsElement = data.GetProperty("messageTimestamp");
                var timestamp = ReadUnixTimestamp(tsElement);

                //var timestamp = data.GetProperty("messageTimestamp").GetInt64();
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
                var messageType = data.GetProperty("messageType").GetString() ?? "unknown";
                var message = data.GetProperty("message");

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
                            mediaKeyTimestamp = img.TryGetProperty("mediaKeyTimestamp", out var mts)
                                ? mts.GetInt64()
                                : null;

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
                            mediaKeyTimestamp = aud.TryGetProperty("mediaKeyTimestamp", out var mts)
                                ? mts.GetInt64()
                                : null;

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

                            mediaKey = docu.GetProperty("mediaKey").GetString();
                            fileSha256 = docu.GetProperty("fileSha256").GetString();
                            fileEncSha256 = docu.GetProperty("fileEncSha256").GetString();
                            directPath = docu.GetProperty("directPath").GetString();

                            var tsElementTiem = data.GetProperty("mediaKeyTimestamp");
                            mediaKeyTimestamp = ReadUnixTimestamp(tsElementTiem);

                            //mediaKeyTimestamp = docu.TryGetProperty("mediaKeyTimestamp", out var mts)
                            //    ? mts.GetInt64()
                            //    : null;

                            fileName = docu.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
                            fileLength = docu.TryGetProperty("fileLength", out var fl) ? fl.GetInt64() : null;
                            pageCount = docu.TryGetProperty("pageCount", out var pc) ? pc.GetInt32() : null;
                            thumbnailBase64 = docu.TryGetProperty("jpegThumbnail", out var jt) ? jt.GetString() : null;

                            textPreview = "[Documento]";
                            break;
                        }


                    case "stickerMessage":
                        {
                            messageKind = MessageKind.Sticker;
                            mediaType = "sticker";

                            var stk = message.GetProperty("stickerMessage");

                            mediaUrl = stk.GetProperty("url").GetString();
                            mediaMime = stk.GetProperty("mimetype").GetString();

                            mediaKey = stk.GetProperty("mediaKey").GetString();
                            fileSha256 = stk.GetProperty("fileSha256").GetString();
                            fileEncSha256 = stk.GetProperty("fileEncSha256").GetString();
                            directPath = stk.GetProperty("directPath").GetString();
                            mediaKeyTimestamp = stk.TryGetProperty("mediaKeyTimestamp", out var mts)
                                ? mts.GetInt64()
                                : null;

                            fileLength = stk.TryGetProperty("fileLength", out var fl)
                                ? fl.GetInt64()
                                : null;

                            // Stickers no tienen caption
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
                    CustomerPhone = remoteJid.Replace("@s.whatsapp.net", ""),
                    CustomerDisplayName = pushName,

                    DirectionIn = !fromMe,

                    MessageKind = messageKind,
                    MessageType = messageType,

                    Text = text,
                    TextPreview = textPreview,

                    MediaUrl = mediaUrl,
                    MediaMime = mediaMime,
                    MediaCaption = mediaCaption,

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

        //Ya con estos guarda la informacion en las tablas de thread y messages
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

            var msg = await InsertMessageAsync(thread, snap, ct);
            await InsertMediaAsync(msg, snap, ct);

            // Notifica SignalR
            await NotifySignalRAsync(thread, message);
        }




        private async Task<CrmMessage> InsertMessageAsync(CrmThread thread, EvolutionMessageSnapshotDto s, CancellationToken ct = default)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrmInboxDbContext>>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var msg = new CrmMessage
            {
                ThreadRefId = thread.Id,
                Sender = s.Sender,
                DisplayName = s.CustomerDisplayName,
                Text = s.Text,
                TimestampUtc = s.CreatedAtUtc,
                ExternalTimestamp = s.ExternalTimestamp,
                DirectionIn = s.DirectionIn,

                MediaUrl = s.MediaUrl,
                MediaMime = s.MediaMime,
                MediaCaption = s.MediaCaption,
                MediaType = s.MessageType,

                MessageKind = (int)s.MessageKind,
                HasMedia = s.MessageKind != MessageKind.Text,

                ExternalId = s.ExternalMessageId,
                RawPayload = s.RawPayloadJson,
                RawHash = ComputeSha256(s.RawPayloadJson),
                CreatedUtc = DateTime.UtcNow
            };

            db.CrmMessages.Add(msg);
            await db.SaveChangesAsync();

            return msg;
        }

        private async Task InsertMediaAsync(CrmMessage msg,EvolutionMessageSnapshotDto s, CancellationToken ct = default)
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



        private async Task NotifySignalRAsync(CrmThread thread, CrmMessage msg)
        {
            if (string.IsNullOrEmpty(thread.BusinessAccountId))
                return;

            await _hubContext.Clients
                .Group(thread.BusinessAccountId)
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




    }
}
