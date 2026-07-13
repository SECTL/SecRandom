using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Profiles;

public sealed class ProfileQueryService : IProfileQueryService
{
    public StudentList? LoadStudentList(string name) => Load<StudentList>("list", "roll_call_list", name);
    public PrizeList? LoadPrizeList(string name) => Load<PrizeList>("list", "lottery_list", name);
    public StudentHistory? LoadStudentHistory(string name) => Load<StudentHistory>("history", "roll_call_history", name);
    public PrizeHistory? LoadPrizeHistory(string name) => Load<PrizeHistory>("history", "lottery_history", name);

    private static T? Load<T>(string category, string directoryName, string name) where T : ProfileConfigBase
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var directory = Path.Combine(AppContext.BaseDirectory, "data", category, directoryName);
        if (!Directory.Exists(directory))
            return null;

        try
        {
            var path = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(candidate => string.Equals(Path.GetFileNameWithoutExtension(candidate), name, StringComparison.Ordinal));
            if (path is null)
                return null;

            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || IsEncryptedEnvelope(document.RootElement))
                return null;

            var snapshot = JsonSerializer.Deserialize<T>(json, ConfigServiceBase.JsonOptions);
            if (snapshot is null)
                return null;

            snapshot.Name = name;
            return snapshot;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsEncryptedEnvelope(JsonElement root)
    {
        return root.TryGetProperty("version", out _)
            && root.TryGetProperty("nonce", out _)
            && root.TryGetProperty("tag", out _)
            && root.TryGetProperty("ciphertext", out _);
    }
}
