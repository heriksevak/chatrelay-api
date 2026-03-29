// ============================================================
//  ChatRelay — EncryptionService
//  AES-256-GCM encryption for sensitive credentials
//  (AiSensy API keys, Meta access tokens, App secrets)
// ============================================================

using System.Security.Cryptography;
using System.Text;

namespace ChatRelay.API.Services;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService(IConfiguration config)
    {
        var keyString = config["Encryption:Key"]
            ?? throw new InvalidOperationException(
                "Encryption:Key is missing from configuration");

        // Key must be exactly 32 bytes for AES-256
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes  = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Combine IV + ciphertext and base64 encode
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        var fullBytes   = Convert.FromBase64String(cipherText);
        var iv          = new byte[16];
        var cipherBytes = new byte[fullBytes.Length - 16];

        Buffer.BlockCopy(fullBytes, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullBytes, iv.Length, cipherBytes, 0, cipherBytes.Length);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV  = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}