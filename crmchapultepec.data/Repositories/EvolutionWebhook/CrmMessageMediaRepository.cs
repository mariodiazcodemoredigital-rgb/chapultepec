using crmchapultepec.data.Data;
using crmchapultepec.entities.EvolutionWebhook;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace crmchapultepec.data.Repositories.EvolutionWebhook
{
    public class CrmMessageMediaRepository
    {
        private readonly IDbContextFactory<CrmInboxDbContext> _dbFactory;
        private readonly HttpClient _httpClient;

        public CrmMessageMediaRepository(IDbContextFactory<CrmInboxDbContext> dbFactory, HttpClient httpClient)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            _httpClient = httpClient;
        }

        // =====================================
        // Obtener un registro por Id
        // =====================================
        public async Task<CrmMessageMedia?> GetByIdAsync(int messageId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.CrmMessageMedias
                .FirstOrDefaultAsync(m => m.MessageId == messageId);
        }

        public async Task<List<CrmMessageMedia>> GetAllAsync(CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.CrmMessageMedias
                .OrderByDescending(m => m.Id) // opcional, ordenamos por Id
                .ToListAsync(ct);
        }

        //public async Task<byte[]?> GetDecryptedDocumentAsync(CrmMessageMedia media, CancellationToken ct = default)
        //{
        //    if (media == null || string.IsNullOrEmpty(media.MediaUrl) || string.IsNullOrEmpty(media.MediaKey))
        //        return null;

        //    // 1. Descargar archivo .enc
        //    var encryptedBytes = await _httpClient.GetByteArrayAsync(media.MediaUrl, ct);

        //    // 2. Desencriptar usando tu método de WhatsApp (ejemplo)
        //    var decryptedBytes = WhatsAppDecrypt(encryptedBytes, media.MediaKey);

        //    return decryptedBytes;
        //}

        //// Aquí va tu método real de desencriptado (simplificado)
        //private byte[] WhatsAppDecrypt(byte[] data, string mediaKey)
        //{
        //    // Lógica de desencriptado basada en mediaKey
        //    // Por ahora simulamos retornando el mismo array
        //    return data;
        //}

        // =====================================
        // Desencriptar media directamente en memoria
        // =====================================
        public async Task<byte[]?> DecryptMediaAsync(CrmMessageMedia media, CancellationToken ct = default)
        {
            if (media == null || string.IsNullOrEmpty(media.MediaKey))
                return null;

            // 1. Determinar la URL con mayor probabilidad de éxito
            // Si tenemos DirectPath, es preferible construirla desde mmg.whatsapp.net
            string urlToDownload = media.MediaUrl;
            if (!string.IsNullOrWhiteSpace(media.DirectPath))
            {
                urlToDownload = $"https://mmg.whatsapp.net{media.DirectPath}";
            }

            if (string.IsNullOrEmpty(urlToDownload)) return null;

            try
            {
                // 2. Configurar headers para simular navegador (evita Forbidden)
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                // 3. Descarga con validación de respuesta
                using var response = await _httpClient.GetAsync(urlToDownload, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    // Si falló con el DirectPath, y la MediaUrl es diferente, intentamos el fallback a MediaUrl
                    if (urlToDownload != media.MediaUrl && !string.IsNullOrEmpty(media.MediaUrl))
                    {
                        Console.WriteLine("DirectPath falló, intentando MediaUrl original...");
                        return await DecryptMediaAsync(new CrmMessageMedia
                        {
                            MediaUrl = media.MediaUrl,
                            MediaKey = media.MediaKey,
                            MediaType = media.MediaType
                        }, ct);
                    }

                    Console.WriteLine($"WhatsApp rechazó la descarga: {response.StatusCode}");
                    return null;
                }

                var encrypted = await response.Content.ReadAsByteArrayAsync(ct);

                // 4. Desencriptar
                return DecryptWhatsAppMedia(
                    encrypted,
                    media.MediaKey,
                    media.MediaType
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error crítico en DecryptMediaAsync: {ex.Message}");
                return null;
            }
        }

        static byte[] HKDF(byte[] ikm, byte[] info, int length)
        {
            using var hmac = new HMACSHA256(new byte[32]);
            var prk = hmac.ComputeHash(ikm);

            var okm = new byte[length];
            var t = Array.Empty<byte>();
            var offset = 0;
            byte counter = 1;

            using var hmac2 = new HMACSHA256(prk);

            while (offset < length)
            {
                var input = t.Concat(info).Concat(new[] { counter }).ToArray();
                t = hmac2.ComputeHash(input);
                var toCopy = Math.Min(t.Length, length - offset);
                Array.Copy(t, 0, okm, offset, toCopy);
                offset += toCopy;
                counter++;
            }

            return okm;
        }

        public byte[] DecryptWhatsAppMedia(
        byte[] encrypted,
        string mediaKeyBase64,
        string mediaType)
        {
            var mediaKey = Convert.FromBase64String(mediaKeyBase64);

            var normalizedType = mediaType switch
            {
                "imageMessage" => "image",
                "videoMessage" => "video",
                "audioMessage" => "audio",
                "documentMessage" => "document",
                "stickerMessage" => "sticker",
                _ => mediaType
            };


            var info = normalizedType switch
            {
                "image" => Encoding.UTF8.GetBytes("WhatsApp Image Keys"),
                "video" => Encoding.UTF8.GetBytes("WhatsApp Video Keys"),
                "audio" => Encoding.UTF8.GetBytes("WhatsApp Audio Keys"),
                "document" => Encoding.UTF8.GetBytes("WhatsApp Document Keys"),
                "sticker" => Encoding.UTF8.GetBytes("WhatsApp Image Keys"),
                _ => throw new Exception($"Unsupported media type: {mediaType}")
            };


            var expanded = HKDF(mediaKey, info, 112);

            var iv = expanded[..16];
            var cipherKey = expanded[16..48];
            var macKey = expanded[48..80];

            // 🔥 SEPARAR CONTENIDO
            var cipherText = encrypted[..^10];
            var mac = encrypted[^10..];

            // (Opcional pero recomendado) Validar MAC
            using var hmac = new HMACSHA256(macKey);

            // Concatenamos IV + CipherText para la validación
            var dataToValidate = new byte[iv.Length + cipherText.Length];
            Buffer.BlockCopy(iv, 0, dataToValidate, 0, iv.Length);
            Buffer.BlockCopy(cipherText, 0, dataToValidate, iv.Length, cipherText.Length);

            var computedMac = hmac.ComputeHash(dataToValidate).Take(10).ToArray();

            if (!computedMac.SequenceEqual(mac))
                throw new CryptographicException("Invalid MAC");

            // 🔓 AES-CBC DECRYPT
            using var aes = Aes.Create();
            aes.Key = cipherKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        }



    }
}
