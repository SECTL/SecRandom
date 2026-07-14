using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class ProfileRecordIdentityMigrationTests
{
    [Fact]
    public void GetStudentHistory_MigratesLegacyKeyWithoutKeepingAnAlias()
    {
        var recordId = Guid.NewGuid();
        Student student = new() { Name = "Alice", RecordId = recordId };
        History legacyHistory = new() { TotalCount = 3 };
        StudentHistory history = new()
        {
            Students = { ["Alice"] = legacyHistory }
        };

        var result = ProfileRecordIdentity.GetStudentHistory(history, student, key => key == "Alice");

        Assert.Same(legacyHistory, result);
        Assert.Same(legacyHistory, history.Students[recordId.ToString("D")]);
        Assert.False(history.Students.ContainsKey("Alice"));
    }

    [Fact]
    public void GetStudentHistory_MigratesCompactRecordIdToCanonicalKey()
    {
        var recordId = Guid.NewGuid();
        Student student = new() { RecordId = recordId };
        History compactHistory = new() { TotalCount = 7 };
        StudentHistory history = new()
        {
            Students = { [recordId.ToString("N")] = compactHistory }
        };

        var result = ProfileRecordIdentity.GetStudentHistory(history, student);

        Assert.Same(compactHistory, result);
        Assert.Same(compactHistory, history.Students[recordId.ToString("D")]);
        Assert.False(history.Students.ContainsKey(recordId.ToString("N")));
    }
}
