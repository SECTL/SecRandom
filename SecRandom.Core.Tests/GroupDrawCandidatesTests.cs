using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Interfaces;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public class GroupDrawCandidatesTests
{
    [Fact]
    public void BuildUsesOnlyEligibleMembersAndSelectedGender()
    {
        Student[] students =
        [
            new() { Name = "A", Group = "1", Gender = "F" },
            new() { Name = "B", Group = "1", Gender = "M" },
            new() { Name = "C", Group = "2", Gender = "F", Exists = false },
            new() { Name = "D", Group = "", Gender = "F" },
            new() { Name = "E", Group = "3", Gender = "F" }
        ];

        var groups = GroupDrawCandidates.Build(students, "F");

        Assert.Equal(new[] { "1", "3" }, groups.Select(group => group.Name));
        Assert.Equal("A", Assert.Single(groups[0].Members).Name);
    }

    [Fact]
    public void RepeatLimitsAreAppliedToGroupsRatherThanMembers()
    {
        var groups = GroupDrawCandidates.Build([
            new Student { Name = "A", Group = "1" },
            new Student { Name = "B", Group = "1" },
            new Student { Name = "C", Group = "2" }
        ]);
        Dictionary<string, int> counts = new() { ["1"] = 1 };

        Assert.Equal("2", Assert.Single(GroupDrawCandidates.Eligible(groups, counts, DrawMode.NoRepeat, 1)).Name);
        Assert.Equal(2, GroupDrawCandidates.Eligible(groups, counts, DrawMode.HalfRepeat, 2).Count);
        Assert.Equal(2, GroupDrawCandidates.Eligible(groups, counts, DrawMode.Repeat, 1).Count);
    }

    [Fact]
    public void SamplesGroupsWithoutReplacementAndMemberFromSelectedGroup()
    {
        var groups = GroupDrawCandidates.Build([
            new Student { Name = "A", Group = "1" },
            new Student { Name = "B", Group = "1" },
            new Student { Name = "C", Group = "2" }
        ]);
        var random = new FixedRandom(1, 0, 1);

        var selected = GroupDrawCandidates.Sample(groups, 2, random);

        Assert.Equal(new[] { "2", "1" }, selected.Select(group => group.Name));
        Assert.Equal("B", GroupDrawCandidates.SampleMember(selected[1], random)!.Name);
        Assert.Equal(new[] { 2, 1, 2 }, random.Bounds);
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupDrawCandidates.Sample(groups, 3, random));
    }

    private sealed class FixedRandom(params int[] values) : IRandomSource
    {
        private int _index;
        public List<int> Bounds { get; } = [];
        public int NextInt32(int maxExclusive)
        {
            Bounds.Add(maxExclusive);
            return values[_index++];
        }

        public double NextDouble() => throw new NotSupportedException();
    }
}
