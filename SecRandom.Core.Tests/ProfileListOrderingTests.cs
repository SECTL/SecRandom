using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class ProfileListOrderingTests
{
    [Fact]
    public void OrderForList_OrdersRecordsWithIdsBeforeNames()
    {
        Student[] students =
        [
            new() { Id = string.Empty, Name = "Beta" },
            new() { Id = "B", Name = "Beta" },
            new() { Id = "10", Name = "Ten" },
            new() { Id = string.Empty, Name = "Delta" },
            new() { Id = "2", Name = "Two" },
            new() { Id = string.Empty, Name = "Alpha" },
            new() { Id = string.Empty, Name = "Charlie" }
        ];

        var ordered = students.OrderForList().ToArray();

        Assert.Equal(["2", "10", "B", "Alpha", "Beta", "Charlie", "Delta"],
            ordered.Select(student => string.IsNullOrWhiteSpace(student.Id) ? student.Name : student.Id));
    }

    [Fact]
    public void OrderForList_UsesTheSameOrderingForPrizes()
    {
        Prize[] prizes =
        [
            new() { Id = string.Empty, Name = "Beta" },
            new() { Id = "2", Name = "Second" },
            new() { Id = string.Empty, Name = "Alpha" }
        ];

        var ordered = prizes.OrderForList().ToArray();

        Assert.Equal(["2", "Alpha", "Beta"],
            ordered.Select(prize => string.IsNullOrWhiteSpace(prize.Id) ? prize.Name : prize.Id));
    }
}
