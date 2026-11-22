using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Interfaces;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

/// <summary>
/// Builds and samples a group pool without turning a group into a student record.
/// Group counts are independent of student repeat counts.
/// </summary>
public static class GroupDrawCandidates
{
    public static string HistoryKey(string group) => "group:" + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(group)));
    public sealed record Group(string Name, IReadOnlyList<Student> Members);

    public static IReadOnlyList<Group> Build(IEnumerable<Student> students, string gender = "") =>
        students.Where(student => DrawCandidateFilter.MatchesScope(student, "", gender))
            .Where(student => !string.IsNullOrWhiteSpace(student.Group))
            .GroupBy(student => student.Group, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.CurrentCulture)
            .Select(group => new Group(group.Key, group.ToArray()))
            .ToArray();

    public static IReadOnlyList<Group> Eligible(
        IReadOnlyList<Group> groups, IReadOnlyDictionary<string, int> counts, DrawMode mode, int halfRepeat)
    {
        var threshold = DrawRepeatPolicy.ResolveThreshold(mode, halfRepeat);
        return groups.Where(group => !DrawRepeatPolicy.HasReachedLimit(
            counts.GetValueOrDefault(group.Name), threshold)).ToArray();
    }

    /// <summary>Each eligible group has the same probability, regardless of its member count.</summary>
    public static IReadOnlyList<Group> Sample(IReadOnlyList<Group> eligible, int count, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (count < 0 || count > eligible.Count)
            throw new ArgumentOutOfRangeException(nameof(count));

        var pool = eligible.ToArray();
        for (var i = 0; i < count; i++)
        {
            var selected = i + random.NextInt32(pool.Length - i);
            (pool[i], pool[selected]) = (pool[selected], pool[i]);
        }

        return pool.Take(count).ToArray();
    }

    public static Student? SampleMember(Group group, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return group.Members.Count == 0 ? null : group.Members[random.NextInt32(group.Members.Count)];
    }
}
