using crmchapultepec.entities.EvolutionWebhook;
using crmchapultepec.services.Interfaces.EvolutionWebhook;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        private readonly string? _hmacSecret;
        private readonly string? _inboundToken;
        private readonly string[] _ipWhitelist;

        public EvolutionWebhookController(
        ILogger<EvolutionWebhookController> log,
        IMessageQueue queue,
        IConfiguration cfg)
        {
            _log = log;
            _queue = queue;
            _cfg = cfg;
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

                if (dataElem.TryGetProperty("key", out var keyElem))
                {
                    if (keyElem.TryGetProperty("remoteJid", out var rj)) remoteJid = rj.GetString();
                    if (keyElem.TryGetProperty("participant", out var p)) participant = p.GetString();
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

                    if (msgElem.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var tsv))
                        timestamp = tsv;
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
                    directionIn: true,
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

    }
}
