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
        public async Task<IActionResult> Post()
        {
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
            try
            {
                dto = JsonSerializer.Deserialize<IncomingMessageDto>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (dto == null)
                {
                    _log.LogWarning("Webhook payload could not be parsed");
                    return BadRequest();
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
                await _queue.EnqueueAsync(dto);
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
