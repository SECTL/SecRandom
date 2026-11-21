using System.Text.Json;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Shared;

namespace SecRandom.Core.Services.Orchestration;

/// <summary>
///     File-backed store for arranged draw lists. One JSON per profile plus one runtime cursor per profile.
/// </summary>
/// <remarks>
///     The profile and its runtime live in different files on purpose: the settings page rewrites the profile while
///     a draw advances the cursor, so the two must never overwrite each other. Every write is a temporary file plus
///     an atomic replace, matching <c>FileConfigService.SaveConfig</c>.
/// </remarks>
public interface IOrchestrationStore
{
    IReadOnlyList<OrchestrationProfile> LoadAll();

    OrchestrationProfile? Load(string id);

    void Save(OrchestrationProfile profile);

    void Delete(string id);

    /// <summary>Reads the cursor of <paramref name="profile" />, clamped into that profile's round range.</summary>
    OrchestrationRuntime GetRuntime(OrchestrationProfile profile);

    void SaveRuntime(OrchestrationRuntime runtime);

    IReadOnlyList<OrchestrationProfile> FindActive(OrchestrationTarget target, string listName);
}

public sealed class OrchestrationStore : IOrchestrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<OrchestrationProfile> LoadAll()
    {
        var directory = ProfileDirectory();
        if (!Directory.Exists(directory))
            return [];

        var profiles = new List<OrchestrationProfile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var profile = ReadProfile(path);
            if (profile is not null)
                profiles.Add(profile);
        }

        profiles.Sort((left, right) =>
        {
            var byTarget = left.Target.CompareTo(right.Target);
            if (byTarget != 0)
                return byTarget;
            var byList = string.Compare(left.SourceListName, right.SourceListName, StringComparison.CurrentCulture);
            return byList != 0 ? byList : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        });
        return profiles;
    }

    public OrchestrationProfile? Load(string id)
    {
        if (!IsValidId(id))
            return null;
        var path = ProfilePath(id);
        return File.Exists(path) ? ReadProfile(path) : null;
    }

    public void Save(OrchestrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!IsValidId(profile.Id))
            throw new ArgumentException("An orchestration profile needs a Guid-based id.", nameof(profile));

        Normalize(profile, migrateLegacyPins: true);
        WriteAtomic(ProfilePath(profile.Id), JsonSerializer.Serialize(profile, JsonOptions));
    }

    /// <summary>
    ///     Brings a profile into the current shape: clamps the round count, normalizes every round's four lists, and
    ///     drops entries for members that no longer exist. Called before every write and after every read, so a
    ///     hand-edited file can never leave a round pointing at a removed record.
    /// </summary>
    /// <param name="profile">Profile to normalize in place.</param>
    /// <param name="migrateLegacyPins">
    ///     Whether format-1 「第几个抽中」 pins should be folded into the round queue. Pass <c>true</c> on write,
    ///     because a written file must never carry the legacy property again.
    /// </param>
    public static void Normalize(OrchestrationProfile profile, bool migrateLegacyPins = true)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.Rules ??= new OrchestrationRules();
        profile.Members ??= [];
        profile.Rounds ??= [];
        profile.RoundCount = Math.Clamp(profile.RoundCount, 1, OrchestrationProfile.MaxRounds);
        profile.Rounds.RemoveAll(round => round.Index is < 1 or > OrchestrationProfile.MaxRounds);
        profile.Rounds.Sort((left, right) => left.Index.CompareTo(right.Index));

        var memberIds = profile.Members.Select(member => member.RecordId).ToHashSet();
        foreach (var round in profile.Rounds)
        {
            round.Order ??= [];
            round.Skipped ??= [];
            round.Guaranteed ??= [];
            round.Weights ??= [];

            if (migrateLegacyPins && round.LegacyPins is { Count: > 0 })
                round.Order = MergeLegacyPins(round);

            round.LegacyPins = null;
            round.Order = KeepKnown(round.Order, memberIds);
            round.Skipped = KeepKnown(round.Skipped, memberIds);
            round.Guaranteed = KeepKnown(round.Guaranteed, memberIds);
            foreach (var recordId in round.Weights.Keys.Where(key => !memberIds.Contains(key)).ToList())
                round.Weights.Remove(recordId);
        }

        profile.FormatVersion = OrchestrationProfile.CurrentFormatVersion;
    }

    /// <summary>
    ///     Folds format-1 pins into the round queue: the pinned members first, in pin order, then whatever the old
    ///     order copy already held. A pinned member used to be seated at an absolute slot in the shrinking
    ///     remainder, which is exactly what the queue replaced, so the position number itself is not preserved —
    ///     only the relative order it expressed.
    /// </summary>
    private static List<Guid> MergeLegacyPins(OrchestrationRound round)
    {
        var seen = new HashSet<Guid>();
        var merged = new List<Guid>();
        foreach (var recordId in round.LegacyPins!
                     .Where(pin => pin.Position >= 1)
                     .OrderBy(pin => pin.Position)
                     .Select(pin => pin.RecordId))
            if (seen.Add(recordId))
                merged.Add(recordId);

        foreach (var recordId in round.Order)
            if (seen.Add(recordId))
                merged.Add(recordId);

        return merged;
    }

    private static List<Guid> KeepKnown(IEnumerable<Guid> recordIds, IReadOnlySet<Guid> memberIds)
    {
        var seen = new HashSet<Guid>();
        var kept = new List<Guid>();
        foreach (var recordId in recordIds)
            if (memberIds.Contains(recordId) && seen.Add(recordId))
                kept.Add(recordId);
        return kept;
    }

    public void Delete(string id)
    {
        if (!IsValidId(id))
            return;
        SafeDelete(ProfilePath(id));
        SafeDelete(RuntimePath(id));
    }

    public OrchestrationRuntime GetRuntime(OrchestrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var path = RuntimePath(profile.Id);
        OrchestrationRuntime? runtime = null;
        if (File.Exists(path))
        {
            try
            {
                runtime = JsonSerializer.Deserialize<OrchestrationRuntime>(File.ReadAllText(path), JsonOptions);
            }
            catch (JsonException)
            {
                // A damaged cursor only costs one round; the schedule itself stays valid.
                runtime = null;
            }
        }

        return new OrchestrationRuntime
        {
            ProfileId = profile.Id,
            Round = Math.Clamp(runtime?.Round ?? 1, 1, profile.EffectiveRoundCount),
            UpdatedAt = runtime?.UpdatedAt ?? default
        };
    }

    public void SaveRuntime(OrchestrationRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!IsValidId(runtime.ProfileId))
            throw new ArgumentException("An orchestration runtime needs a Guid-based profile id.", nameof(runtime));

        runtime.UpdatedAt = DateTime.UtcNow;
        WriteAtomic(RuntimePath(runtime.ProfileId), JsonSerializer.Serialize(runtime, JsonOptions));
    }

    public IReadOnlyList<OrchestrationProfile> FindActive(OrchestrationTarget target, string listName) =>
        LoadAll()
            .Where(profile => profile.IsActive
                              && profile.Target == target
                              && (string.IsNullOrWhiteSpace(listName)
                                  || string.Equals(profile.SourceListName, listName, StringComparison.Ordinal)))
            .ToList();

    public static string NewId() => Guid.NewGuid().ToString("N");

    private static string ProfileDirectory() => Utils.GetFilePath("config", "orchestration");

    private static string ProfilePath(string id) =>
        Utils.GetFilePath("config", "orchestration", $"{id}.json");

    private static string RuntimePath(string id) =>
        Utils.GetFilePath("config", "orchestration", "runtime", $"{id}.json");

    private static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Guid.TryParseExact(id, "N", out _);

    private static OrchestrationProfile? ReadProfile(string path)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<OrchestrationProfile>(File.ReadAllText(path), JsonOptions);
            if (profile is null)
                return null;

            // The file name is the identity when a hand-edited file lost its id; a file whose name is not a
            // profile id was never ours, so ignore it instead of inventing an identity for it.
            if (!IsValidId(profile.Id))
                profile.Id = Path.GetFileNameWithoutExtension(path);
            Normalize(profile);
            return profile;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteAtomic(string path, string json)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover file is harmless: the next Save overwrites it.
        }
    }
}