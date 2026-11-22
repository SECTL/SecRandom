using SecRandom.Core.Enums;
using SecRandom.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace SecRandom.Core.Services.Profiles;

/// <summary>
/// Path rules for the roster stores. The current format has one SQLite file per draw kind;
/// the per-profile paths below are kept only as migration inputs from the intermediate build.
/// </summary>
internal static class ProfileRosterPaths
{
    internal static string DatabasePath(ProfileRosterKind kind) =>
        Utils.GetFilePath("list", kind switch
        {
            ProfileRosterKind.RollCall => $"roll_call{Utils.RosterDatabaseExtension}",
            ProfileRosterKind.Lottery => $"lottery{Utils.RosterDatabaseExtension}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown roster kind.")
        });

    internal static string DatabasePath(string profileName, ProfileRosterKind kind)
    {
        Validate(profileName);
        return DatabasePath(kind);
    }

    internal static bool Exists(string profileName, ProfileRosterKind kind) =>
        ProfileRosterDatabase.Exists(profileName, kind, NullLogger.Instance);

    internal static IReadOnlyList<string> EnumerateProfileNames(ProfileRosterKind kind) =>
        ProfileRosterDatabase.GetProfileNames(kind, NullLogger.Instance);

    internal static void Rename(string oldName, string newName, ProfileRosterKind kind)
    {
        Validate(oldName);
        Validate(newName);
        ProfileRosterDatabase.Rename(oldName, newName, kind, NullLogger.Instance);
    }

    internal static void Delete(string profileName, ProfileRosterKind kind)
    {
        Validate(profileName);
        ProfileRosterDatabase.Delete(profileName, kind, NullLogger.Instance);
    }

    internal static string LegacyDirectory(ProfileRosterKind kind) =>
        Utils.GetDirectoryPath("list", kind switch
        {
            ProfileRosterKind.RollCall => "roll_call_list",
            ProfileRosterKind.Lottery => "lottery_list",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown roster kind.")
        });

    internal static string LegacyDatabasePath(string profileName, ProfileRosterKind kind)
    {
        Validate(profileName);
        return Path.Combine(LegacyDirectory(kind), $"{profileName}{Utils.RosterDatabaseExtension}");
    }

    internal static string LegacyDatabaseBackupPath(string profileName, ProfileRosterKind kind)
    {
        Validate(profileName);
        return Path.Combine(LegacyDirectory(kind), $"{profileName}{Utils.RosterDatabaseMigratedExtension}");
    }

    internal static string LegacyJsonPath(string profileName, ProfileRosterKind kind)
    {
        Validate(profileName);
        return Path.Combine(LegacyDirectory(kind), $"{profileName}.json");
    }

    internal static string LegacyBackupPath(string profileName, ProfileRosterKind kind)
    {
        Validate(profileName);
        return Path.Combine(LegacyDirectory(kind), $"{profileName}{Utils.RosterLegacyBackupExtension}");
    }

    internal static bool HasLegacyFiles(string profileName, ProfileRosterKind kind) =>
        File.Exists(LegacyDatabasePath(profileName, kind)) || File.Exists(LegacyJsonPath(profileName, kind));

    internal static IReadOnlyList<string> EnumerateLegacyProfileNames(ProfileRosterKind kind)
    {
        var directory = LegacyDirectory(kind);
        if (!Directory.Exists(directory))
            return [];

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.EndsWith(Utils.RosterLegacyBackupExtension, StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(Utils.RosterDatabaseMigratedExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            if (fileName.EndsWith(Utils.RosterDatabaseExtension, StringComparison.OrdinalIgnoreCase))
                names.Add(fileName[..^Utils.RosterDatabaseExtension.Length]);
            else if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                names.Add(Path.GetFileNameWithoutExtension(fileName));
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name) && ProfileConfigNameIsValid(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    internal static void RenameLegacy(string oldName, string newName, ProfileRosterKind kind)
    {
        MoveIfExists(LegacyDatabasePath(oldName, kind), LegacyDatabasePath(newName, kind));
        MoveIfExists(LegacyDatabasePath(oldName, kind) + "-wal", LegacyDatabasePath(newName, kind) + "-wal");
        MoveIfExists(LegacyDatabasePath(oldName, kind) + "-shm", LegacyDatabasePath(newName, kind) + "-shm");
        MoveIfExists(LegacyDatabaseBackupPath(oldName, kind), LegacyDatabaseBackupPath(newName, kind));
        MoveIfExists(LegacyJsonPath(oldName, kind), LegacyJsonPath(newName, kind));
        MoveIfExists(LegacyBackupPath(oldName, kind), LegacyBackupPath(newName, kind));
    }

    internal static void DeleteLegacy(string profileName, ProfileRosterKind kind)
    {
        DeleteIfExists(LegacyDatabasePath(profileName, kind) + "-wal");
        DeleteIfExists(LegacyDatabasePath(profileName, kind) + "-shm");
        DeleteIfExists(LegacyDatabasePath(profileName, kind));
        DeleteIfExists(LegacyDatabaseBackupPath(profileName, kind));
        DeleteIfExists(LegacyJsonPath(profileName, kind));
        DeleteIfExists(LegacyBackupPath(profileName, kind));
    }

    private static bool ProfileConfigNameIsValid(string name) =>
        SecRandom.Shared.Abstraction.ProfileConfigBase.IsValidProfileName(name);

    private static void Validate(string name) =>
        SecRandom.Shared.Abstraction.ProfileConfigBase.ValidateProfileName(name);

    private static void MoveIfExists(string source, string target)
    {
        if (!File.Exists(source))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
