using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Core.Services.Config;

public sealed class EncryptedProfileJsonStore
{
    private static readonly byte[] AesContext = Encoding.UTF8.GetBytes("SecRandom.ProfileJson.v1");

    private readonly ILogger<EncryptedProfileJsonStore> _logger;
    private readonly object _sync = new();

    public EncryptedProfileJsonStore(ILogger<EncryptedProfileJsonStore> logger)
    {
        _logger = logger;
    }

    public bool Exists(ProfileConfigBase config)
    {
        lock (_sync)
        {
            return File.Exists(config.ConfigFilePath);
        }
    }

    public T Load<T>(T fallback) where T : ProfileConfigBase
    {
        lock (_sync)
        {
            var filePath = fallback.ConfigFilePath;
            if (!File.Exists(filePath))
            {
                SaveInternal(fallback);
                return fallback;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                if (TryReadEnvelope(json, out var envelope))
                {
                    var decrypted = Decrypt(Convert.FromBase64String(envelope.Ciphertext), Convert.FromBase64String(envelope.Nonce), Convert.FromBase64String(envelope.Tag));
                    return JsonSerializer.Deserialize<T>(decrypted, ConfigServiceBase.JsonOptions) ?? fallback;
                }

                var plain = JsonSerializer.Deserialize<T>(json, ConfigServiceBase.JsonOptions) ?? fallback;
                SaveInternal(plain);
                return plain;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "无法加载加密名单文件：{Path}", filePath);
                return fallback;
            }
        }
    }

    public void Save(ProfileConfigBase config)
    {
        lock (_sync)
        {
            SaveInternal(config);
        }
    }

    public void Delete(ProfileConfigBase config)
    {
        lock (_sync)
        {
            var filePath = config.ConfigFilePath;
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    public void Rename(ProfileConfigBase config, string newName)
    {
        lock (_sync)
        {
            var oldPath = config.ConfigFilePath;
            var newPath = ReplaceNameInPath(oldPath, newName);

            if (File.Exists(newPath))
                File.Delete(newPath);

            if (File.Exists(oldPath))
                File.Move(oldPath, newPath);
        }
    }

    private void SaveInternal(ProfileConfigBase config)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(config, ConfigServiceBase.JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(DeriveKey(), 16))
        {
            aes.Encrypt(nonce, payload, ciphertext, tag, AesContext);
        }

        var envelope = new EncryptedJsonEnvelope(
            Version: 1,
            Nonce: Convert.ToBase64String(nonce),
            Tag: Convert.ToBase64String(tag),
            Ciphertext: Convert.ToBase64String(ciphertext));

        var filePath = config.ConfigFilePath;
        var json = JsonSerializer.Serialize(envelope, ConfigServiceBase.JsonOptions);
        File.WriteAllText(filePath, json);
    }

    private static bool TryReadEnvelope(string json, out EncryptedJsonEnvelope envelope)
    {
        envelope = null!;

        try
        {
            envelope = JsonSerializer.Deserialize<EncryptedJsonEnvelope>(json, ConfigServiceBase.JsonOptions) ?? null!;
            return envelope is not null && envelope.Version == 1 &&
                   !string.IsNullOrWhiteSpace(envelope.Nonce) &&
                   !string.IsNullOrWhiteSpace(envelope.Tag) &&
                   !string.IsNullOrWhiteSpace(envelope.Ciphertext);
        }
        catch
        {
            return false;
        }
    }

    private static string Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(DeriveKey(), 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, AesContext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey()
    {
        var seed = $"{Environment.UserName}|{Environment.MachineName}|{AppContext.BaseDirectory}|SecRandom.ProfileJson.v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    private static string ReplaceNameInPath(string path, string newName)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        return Path.Combine(directory, $"{newName}.json");
    }

    private sealed record EncryptedJsonEnvelope(int Version, string Nonce, string Tag, string Ciphertext);
}
