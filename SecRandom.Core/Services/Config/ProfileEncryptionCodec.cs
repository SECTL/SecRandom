using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Core.Services.Config;

public static class ProfileEncryptionCodec
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly string KeyFilePath = Utils.GetFilePath("config", "encryption.key");
    private static readonly object KeySync = new();

    public static byte[] Encrypt(byte[] plaintext, string purpose)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(GetOrCreateKey(), TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, GetAssociatedData(purpose));
        }

        return Combine(nonce, tag, ciphertext);
    }

    public static byte[] Decrypt(byte[] payload, string purpose)
    {
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid encrypted payload.");

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var ciphertext = payload[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(GetOrCreateKey(), TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, GetAssociatedData(purpose));
        return plaintext;
    }

    public static bool TryReadEnvelope(string json, out EncryptedJsonEnvelope envelope)
    {
        envelope = null!;

        try
        {
            envelope = JsonSerializer.Deserialize<EncryptedJsonEnvelope>(json, ConfigServiceBase.JsonOptions) ?? null!;
            return envelope is not null &&
                   envelope.Version == 1 &&
                   !string.IsNullOrWhiteSpace(envelope.Nonce) &&
                   !string.IsNullOrWhiteSpace(envelope.Tag) &&
                   !string.IsNullOrWhiteSpace(envelope.Ciphertext);
        }
        catch
        {
            return false;
        }
    }

    public static string CreateEnvelopeJson(byte[] plaintext, string purpose)
    {
        var encrypted = Encrypt(plaintext, purpose);
        var nonce = encrypted[..NonceSize];
        var tag = encrypted[NonceSize..(NonceSize + TagSize)];
        var ciphertext = encrypted[(NonceSize + TagSize)..];

        var envelope = new EncryptedJsonEnvelope(
            Version: 1,
            Nonce: Convert.ToBase64String(nonce),
            Tag: Convert.ToBase64String(tag),
            Ciphertext: Convert.ToBase64String(ciphertext));

        return JsonSerializer.Serialize(envelope, ConfigServiceBase.JsonOptions);
    }

    public static byte[] ReadEnvelopePayload(string json)
    {
        if (!TryReadEnvelope(json, out var envelope))
            throw new CryptographicException("Invalid encrypted envelope.");

        return Combine(
            Convert.FromBase64String(envelope.Nonce),
            Convert.FromBase64String(envelope.Tag),
            Convert.FromBase64String(envelope.Ciphertext));
    }

    public static byte[] DecodeEnvelopeJson(string json, string purpose)
    {
        return Decrypt(ReadEnvelopePayload(json), purpose);
    }

    public static bool TryDecryptLegacyPayload(byte[] payload, string purpose, out byte[] plaintext)
    {
        plaintext = [];

        try
        {
            plaintext = DecryptWithLegacyKey(payload, purpose);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static T LoadJsonFile<T>(
        string filePath,
        T fallback,
        string purpose,
        Func<JsonElement, bool>? legacyJsonValidator = null) where T : ConfigBase
    {
        if (!File.Exists(filePath))
        {
            SaveJsonFile(filePath, fallback, purpose);
            return fallback;
        }

        var json = File.ReadAllText(filePath);

        try
        {
            if (TryReadEnvelope(json, out _))
            {
                try
                {
                    return DeserializeEncryptedJson(json, fallback, purpose);
                }
                catch (CryptographicException)
                {
                    if (TryDeserializeLegacyEncryptedJson(json, fallback, purpose, out var legacy))
                    {
                        SaveJsonFile(filePath, legacy, purpose);
                        return legacy;
                    }

                    throw;
                }
            }

            if (!IsLegacyPlainJsonCandidate(json, legacyJsonValidator))
                return fallback;

            var plain = JsonSerializer.Deserialize<T>(json, ConfigServiceBase.JsonOptions) ?? fallback;
            ApplyProfileName(plain, fallback);
            SaveJsonFile(filePath, plain, purpose);
            return plain;
        }
        catch
        {
            return fallback;
        }
    }

    public static void SaveJsonFile<T>(string filePath, T config, string purpose) where T : ConfigBase
    {
        EnsureDirectory(filePath);
        var payload = JsonSerializer.SerializeToUtf8Bytes(config, ConfigServiceBase.JsonOptions);
        File.WriteAllText(filePath, CreateEnvelopeJson(payload, purpose));
    }

    private static T DeserializeEncryptedJson<T>(string json, T fallback, string purpose) where T : ConfigBase
    {
        var decrypted = DecodeEnvelopeJson(json, purpose);
        var loaded = JsonSerializer.Deserialize<T>(decrypted, ConfigServiceBase.JsonOptions) ?? fallback;
        ApplyProfileName(loaded, fallback);
        return loaded;
    }

    private static bool IsLegacyPlainJsonCandidate(string json, Func<JsonElement, bool>? legacyJsonValidator)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (legacyJsonValidator is null)
                return root.EnumerateObject().Any();

            return legacyJsonValidator(root);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeserializeLegacyEncryptedJson<T>(string json, T fallback, string purpose, out T value)
        where T : ConfigBase
    {
        value = fallback;

        try
        {
            var decrypted = DecryptWithLegacyKey(ReadEnvelopePayload(json), purpose);
            value = JsonSerializer.Deserialize<T>(decrypted, ConfigServiceBase.JsonOptions) ?? fallback;
            ApplyProfileName(value, fallback);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyProfileName<T>(T loaded, T fallback) where T : ConfigBase
    {
        if (loaded is ProfileConfigBase loadedProfile && fallback is ProfileConfigBase fallbackProfile)
            loadedProfile.Name = fallbackProfile.Name;
    }

    private static byte[] GetOrCreateKey()
    {
        lock (KeySync)
        {
            if (File.Exists(KeyFilePath))
                return Convert.FromBase64String(File.ReadAllText(KeyFilePath).Trim());

            var key = RandomNumberGenerator.GetBytes(KeySize);
            EnsureDirectory(KeyFilePath);
            File.WriteAllText(KeyFilePath, Convert.ToBase64String(key));
            return key;
        }
    }

    private static byte[] DecryptWithLegacyKey(byte[] payload, string purpose)
    {
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid encrypted payload.");

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var ciphertext = payload[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(DeriveLegacyKey(purpose), TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, GetAssociatedData(purpose));
        return plaintext;
    }

    private static byte[] DeriveLegacyKey(string purpose)
    {
        var seed = $"{Environment.UserName}|{Environment.MachineName}|{AppContext.BaseDirectory}|{purpose}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    private static byte[] GetAssociatedData(string purpose)
    {
        return Encoding.UTF8.GetBytes(purpose);
    }

    private static byte[] Combine(byte[] nonce, byte[] tag, byte[] ciphertext)
    {
        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);
        return result;
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public sealed record EncryptedJsonEnvelope(int Version, string Nonce, string Tag, string Ciphertext);
}
