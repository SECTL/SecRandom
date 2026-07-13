using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class StudentCandidateTests
{
    [Theory]
    [InlineData("2026001", "", true)]
    [InlineData("", "Alice", true)]
    [InlineData("", "", false)]
    [InlineData(" ", "\t", false)]
    public void IsCandidate_RequiresAtLeastOneStudentNumberOrName(string id, string name, bool expected)
    {
        var student = new Student { Id = id, Name = name };

        Assert.Equal(expected, student.IsCandidate);
    }

    [Fact]
    public void IsCandidate_ExcludesDisabledStudent()
    {
        var student = new Student { Id = "2026001", Exists = false };

        Assert.False(student.IsCandidate);
    }
}
