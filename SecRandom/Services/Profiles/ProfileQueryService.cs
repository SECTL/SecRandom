using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Profiles;

/// <summary>
///     非破坏性的档案快照读取。名单仍然是 JSON 文件；历史记录住在各自的 SQLite 库里，所以历史一律委托
///     <see cref="IHistoryQueryService" />，这里不再自己解析 JSON，否则读到的会是迁移之前的旧快照。
/// </summary>
public sealed class ProfileQueryService(IHistoryQueryService historyQuery) : IProfileQueryService
{
    public StudentList? LoadStudentList(string name) => Load<StudentList>("list", "roll_call_list", name);
    public PrizeList? LoadPrizeList(string name) => Load<PrizeList>("list", "lottery_list", name);
    public StudentHistory? LoadStudentHistory(string name) => historyQuery.LoadStudentHistory(name);
    public PrizeHistory? LoadPrizeHistory(string name) => historyQuery.LoadPrizeHistory(name);

    private static T? Load<T>(string category, string directoryName, string name) where T : ProfileConfigBase
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var directory = Path.Combine(Utils.DataRoot, category, directoryName);
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
