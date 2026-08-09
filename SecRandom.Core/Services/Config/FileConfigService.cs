using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Core.Services;

public static partial class CoreRuntimeServiceCollectionExtensions
{
    private sealed class FileConfigService(ILogger<FileConfigService> logger) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback)
        {
            var filePath = fallback.ConfigFilePath;
            logger.LogInformation("Checking config file existence: {Path}", filePath);
            return File.Exists(filePath);
        }

        public override T LoadConfig<T>(T fallback)
        {
            var filePath = fallback.ConfigFilePath;
            logger.LogInformation("Loading config file: {Path}", filePath);
            if (!File.Exists(filePath))
            {
                SaveConfig(fallback);
                return fallback;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                if (!IsPlainJsonObject(json))
                    return fallback;

                var loaded = JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
                ApplyProfileName(loaded, fallback);
                return loaded;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to load config file, using fallback without rewriting: {Path}", filePath);
                return fallback;
            }
        }

        public override void SaveConfig<T>(T config)
        {
            var filePath = config.ConfigFilePath;
            logger.LogInformation("Saving config file: {Path}", filePath);
            EnsureDirectory(filePath);
            // 写临时文件再原子替换，避免进程中途被杀留下截断 JSON。
            var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, JsonOptions));
            File.Move(temporaryPath, filePath, overwrite: true);
        }

        public override void DeleteConfig<T>(T config)
        {
            var filePath = config.ConfigFilePath;
            logger.LogInformation("Deleting config file: {Path}", filePath);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        private static bool IsPlainJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                return root.ValueKind == JsonValueKind.Object
                       && root.EnumerateObject().Any()
                       && !IsEncryptedEnvelope(root);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEncryptedEnvelope(JsonElement root)
        {
            return root.TryGetProperty("version", out _)
                   && root.TryGetProperty("nonce", out _)
                   && root.TryGetProperty("tag", out _)
                   && root.TryGetProperty("ciphertext", out _);
        }

        private static void ApplyProfileName<T>(T loaded, T fallback) where T : ConfigBase
        {
            if (loaded is ProfileConfigBase loadedProfile && fallback is ProfileConfigBase fallbackProfile)
                loadedProfile.Name = fallbackProfile.Name;
        }

        private static void EnsureDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }
    }
}
