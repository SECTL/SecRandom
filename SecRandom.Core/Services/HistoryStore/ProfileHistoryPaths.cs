using SecRandom.Core.Enums;
using SecRandom.Shared;

namespace SecRandom.Core.Services.HistoryStore;

/// <summary>
///     历史记录档案在 <c>data/history</c> 下的路径规则。历史记录从「一个档案一个 JSON」换成了「一个档案一个小数据库」，
///     但 <c>ProfileConfigBase.ConfigFilePath</c> 仍保留 JSON 路径作为档案身份（改名/删除/旧版本归档都按它算），所以
///     凡是要真正读写、判断存在、枚举、改名或删除的地方都必须走这里，不能再自己拼 <c>{name}.json</c>。
/// </summary>
internal static class ProfileHistoryPaths
{
    internal static string DirectoryName(HistoryGroupKind kind) => kind switch
    {
        HistoryGroupKind.RollCall => "roll_call_history",
        HistoryGroupKind.Lottery => "lottery_history",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown history kind.")
    };

    internal static string DatabasePath(string profileName, HistoryGroupKind kind) =>
        Utils.GetFilePath("history", DirectoryName(kind), $"{profileName}{Utils.HistoryDatabaseExtension}");

    /// <summary>旧版本写的 JSON 档案：只在首次加载迁移时读一次。</summary>
    internal static string LegacyJsonPath(string profileName, HistoryGroupKind kind) =>
        Utils.GetFilePath("history", DirectoryName(kind), $"{profileName}.json");

    /// <summary>迁移后的旧 JSON 备份，扩展名不再匹配 <c>*.json</c>，因此不会被任何 JSON 读取路径当成档案。</summary>
    internal static string LegacyBackupPath(string profileName, HistoryGroupKind kind) =>
        Utils.GetFilePath("history", DirectoryName(kind), $"{profileName}{Utils.HistoryLegacyBackupExtension}");

    /// <summary>数据库存在，或磁盘上还留着未迁移的 JSON（迁移会在首次加载时发生）。</summary>
    internal static bool Exists(string profileName, HistoryGroupKind kind) =>
        File.Exists(DatabasePath(profileName, kind)) || File.Exists(LegacyJsonPath(profileName, kind));

    /// <summary>
    ///     枚举一份历史目录里的档案名。数据库和未迁移的 JSON 都算，备份文件 <c>.json.migrated</c> 不算。
    /// </summary>
    internal static IReadOnlyList<string> EnumerateProfileNames(HistoryGroupKind kind)
    {
        var directory = Utils.GetDirectoryPath("history", DirectoryName(kind));
        if (!Directory.Exists(directory))
            return [];

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.EndsWith(Utils.HistoryDatabaseExtension, StringComparison.OrdinalIgnoreCase))
                names.Add(fileName[..^Utils.HistoryDatabaseExtension.Length]);
            else if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                names.Add(Path.GetFileNameWithoutExtension(fileName));
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>改名档案：数据库、未迁移的 JSON 与迁移备份都要跟着换名，避免同名档案被下一次加载迁移覆盖。</summary>
    internal static void Rename(string oldName, string newName, HistoryGroupKind kind)
    {
        MoveIfExists(DatabasePath(oldName, kind), DatabasePath(newName, kind));
        MoveIfExists(LegacyJsonPath(oldName, kind), LegacyJsonPath(newName, kind));
        MoveIfExists(LegacyBackupPath(oldName, kind), LegacyBackupPath(newName, kind));
    }

    /// <summary>删除档案（含迁移备份）。</summary>
    internal static void Delete(string profileName, HistoryGroupKind kind)
    {
        DeleteIfExists(Utils.GetFilePath("history", DirectoryName(kind),
            $"{profileName}{Utils.HistoryDatabaseExtension}-wal"));
        DeleteIfExists(Utils.GetFilePath("history", DirectoryName(kind),
            $"{profileName}{Utils.HistoryDatabaseExtension}-shm"));
        DeleteIfExists(DatabasePath(profileName, kind));
        DeleteIfExists(LegacyJsonPath(profileName, kind));
        DeleteIfExists(LegacyBackupPath(profileName, kind));
    }

    private static void MoveIfExists(string source, string target)
    {
        if (!File.Exists(source))
            return;

        // 目标已存在时覆盖：改名到同类档案名不该丢数据，而保留旧目标会留下两份互相矛盾的历史。
        File.Move(source, target, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
