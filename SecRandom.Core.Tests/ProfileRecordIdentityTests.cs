using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public class ProfileRecordIdentityTests
{
    [Fact]
    public void NormalizeStudentList_BackfillsMissingRecordIds()
    {
        StudentList list = new()
        {
            Students =
            [
                new Student { Name = "Alice", RecordId = Guid.Empty },
                new Student { Name = "Bob", RecordId = Guid.Empty }
            ]
        };

        var changed = ProfileRecordIdentity.Normalize(list);

        Assert.True(changed);
        Assert.All(list.Students, student => Assert.NotEqual(Guid.Empty, student.RecordId));
        Assert.Equal(2, list.Students.Select(student => student.RecordId).Distinct().Count());
    }

    [Fact]
    public void NewStudent_DefaultsToEmptyRecordIdUntilNormalized()
    {
        Student student = new();

        Assert.Equal(Guid.Empty, student.RecordId);
    }

    [Fact]
    public void NormalizePrizeList_ReplacesDuplicateRecordIds()
    {
        var duplicateId = Guid.NewGuid();
        PrizeList list = new()
        {
            Prizes =
            [
                new Prize { Name = "Book", RecordId = duplicateId },
                new Prize { Name = "Pen", RecordId = duplicateId }
            ]
        };

        var changed = ProfileRecordIdentity.Normalize(list);

        Assert.True(changed);
        Assert.Equal(2, list.Prizes.Select(prize => prize.RecordId).Distinct().Count());
    }

    [Fact]
    public void NewPrize_DefaultsToEmptyRecordIdUntilNormalized()
    {
        Prize prize = new();

        Assert.Equal(Guid.Empty, prize.RecordId);
    }

    [Fact]
    public void GetStudentHistory_UsesRecordIdBeforeLegacyKeys()
    {
        var recordId = Guid.NewGuid();
        Student student = new()
        {
            Name = "Alice",
            Id = "1",
            RecordId = recordId
        };
        StudentHistory history = new()
        {
            Students =
            {
                ["1"] = new History { TotalCount = 2 },
                [recordId.ToString("D")] = new History { TotalCount = 5 }
            }
        };

        var result = ProfileRecordIdentity.GetStudentHistory(history, student);

        Assert.NotNull(result);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public void GetStudentHistory_MigratesUniqueLegacyKeyToRecordId()
    {
        var recordId = Guid.NewGuid();
        Student student = new()
        {
            Name = "Alice",
            Id = string.Empty,
            RecordId = recordId
        };
        History legacyHistory = new() { TotalCount = 3 };
        StudentHistory history = new()
        {
            Students =
            {
                ["Alice"] = legacyHistory
            }
        };

        var result = ProfileRecordIdentity.GetStudentHistory(history, student, key => key == "Alice");

        Assert.Same(legacyHistory, result);
        Assert.Same(legacyHistory, history.Students[recordId.ToString("D")]);
    }

    [Fact]
    public void GetStudentHistory_SkipsAmbiguousLegacyKeys()
    {
        var recordId = Guid.NewGuid();
        Student student = new()
        {
            Name = "Alice",
            RecordId = recordId
        };
        StudentHistory history = new()
        {
            Students =
            {
                ["Alice"] = new History { TotalCount = 3 }
            }
        };

        var result = ProfileRecordIdentity.GetStudentHistory(history, student, _ => false);

        Assert.Null(result);
        Assert.False(history.Students.ContainsKey(recordId.ToString("D")));
    }

    [Fact]
    public void GetPrizeHistory_MigratesLegacyNameToRecordId()
    {
        var recordId = Guid.NewGuid();
        Prize prize = new()
        {
            Name = "Book",
            RecordId = recordId
        };
        History legacyHistory = new() { TotalCount = 4 };
        PrizeHistory history = new()
        {
            Prizes =
            {
                ["Book"] = legacyHistory
            }
        };

        var result = ProfileRecordIdentity.GetPrizeHistory(history, prize, key => key == "Book");

        Assert.Same(legacyHistory, result);
        Assert.Same(legacyHistory, history.Prizes[recordId.ToString("D")]);
    }

    [Fact]
    public void StudentRecordId_DeserializesLegacyCompactGuidString()
    {
        var recordId = Guid.NewGuid();
        var json = $$"""{"record_id":"{{recordId:N}}"}""";

        var student = JsonSerializer.Deserialize<Student>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(student);
        Assert.Equal(recordId, student.RecordId);
    }

    [Fact]
    public void StudentRecordId_DeserializesLegacyEmptyStringAsEmptyGuid()
    {
        const string json = """{"record_id":""}""";

        var student = JsonSerializer.Deserialize<Student>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(student);
        Assert.Equal(Guid.Empty, student.RecordId);
    }
}
