using SecRandom.Core.Services.Profiles;

namespace SecRandom.Core.Tests;

public sealed class ProfileSequenceNumberingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Analyze_ReturnsNone_WhenNewIdIsBlank(string newId)
    {
        var conflict = ProfileSequenceNumbering.Analyze(["1", "2"], newId);

        Assert.Equal(SequenceNumberConflictKind.None, conflict.Kind);
        Assert.Empty(conflict.ShiftEntries);
    }

    [Fact]
    public void Analyze_ReturnsNone_WhenListIsEmpty()
    {
        var conflict = ProfileSequenceNumbering.Analyze([], "15");

        Assert.Equal(SequenceNumberConflictKind.None, conflict.Kind);
        Assert.Null(conflict.AppendNumber);
    }

    [Fact]
    public void Analyze_ReturnsNone_WhenNoMemberUsesTheNumber()
    {
        var conflict = ProfileSequenceNumbering.Analyze(["1", "2", "3"], "15");

        Assert.Equal(SequenceNumberConflictKind.None, conflict.Kind);
        Assert.Equal(4, conflict.AppendNumber);
        Assert.Empty(conflict.DuplicateIndices);
    }

    [Fact]
    public void Analyze_OffersShift_ForSingleNumericCollision()
    {
        var conflict = ProfileSequenceNumbering.Analyze(["1", "15", "16", "45"], "15");

        Assert.Equal(SequenceNumberConflictKind.Shiftable, conflict.Kind);
        Assert.Equal([15L, 16L, 45L], conflict.ShiftEntries.Select(entry => entry.OldNumber));
        Assert.Equal([16L, 17L, 46L], conflict.ShiftEntries.Select(entry => entry.NewNumber));
        Assert.Equal(1, conflict.InsertIndex);
        Assert.Equal(46, conflict.AppendNumber);
        Assert.Equal([1], conflict.DuplicateIndices);
    }

    [Fact]
    public void Analyze_SkipsNonNumericIds_WhenShifting()
    {
        var conflict = ProfileSequenceNumbering.Analyze(["A1", "15", "初一(3)班-07"], "15");

        Assert.Equal(SequenceNumberConflictKind.Shiftable, conflict.Kind);
        var entry = Assert.Single(conflict.ShiftEntries);
        Assert.Equal(1, entry.Index);
        Assert.Equal("15", entry.OldId);
        Assert.Equal(16, entry.NewNumber);
    }

    [Fact]
    public void Analyze_ReportsNonNumeric_WhenNewIdIsNotANumber()
    {
        var conflict = ProfileSequenceNumbering.Analyze(["A15", "15"], "A15");

        Assert.Equal(SequenceNumberConflictKind.NotNumeric, conflict.Kind);
        Assert.Equal([0], conflict.DuplicateIndices);
        Assert.Empty(conflict.ShiftEntries);
        Assert.Equal(16, conflict.AppendNumber);
    }

    [Fact]
    public void Analyze_ReportsAlreadyDuplicated_WhenSeveralMembersShareTheNumber()
    {
        var conflict = ProfileSequenceNumbering.Analyze(["15", "15", "20"], "15");

        Assert.Equal(SequenceNumberConflictKind.AlreadyDuplicated, conflict.Kind);
        Assert.Equal([0, 1], conflict.DuplicateIndices);
        Assert.Empty(conflict.ShiftEntries);
        Assert.Equal(21, conflict.AppendNumber);
    }

    [Fact]
    public void Analyze_ComparesIdsAfterTrimmingWhitespace()
    {
        var conflict = ProfileSequenceNumbering.Analyze([" 15 ", "20"], "15");

        Assert.Equal(SequenceNumberConflictKind.Shiftable, conflict.Kind);
        Assert.Equal(["15", "20"], conflict.ShiftEntries.Select(entry => entry.OldId));
        Assert.Equal([16L, 21L], conflict.ShiftEntries.Select(entry => entry.NewNumber));
    }

    [Fact]
    public void Analyze_HandlesLongStudentNumbers()
    {
        var conflict = ProfileSequenceNumbering.Analyze(["20260101001", "20260101002"], "20260101001");

        Assert.Equal(SequenceNumberConflictKind.Shiftable, conflict.Kind);
        Assert.Equal([20260101002L, 20260101003L], conflict.ShiftEntries.Select(entry => entry.NewNumber));
    }

    [Fact]
    public void Analyze_HandlesLeadingZerosAsNumbers()
    {
        var conflict = ProfileSequenceNumbering.Analyze(["007", "8"], "007");

        Assert.Equal(SequenceNumberConflictKind.Shiftable, conflict.Kind);
        Assert.Equal([8L, 9L], conflict.ShiftEntries.Select(entry => entry.NewNumber));
        Assert.Equal(9, conflict.AppendNumber);
    }

    [Fact]
    public void Analyze_RefusesToShiftBeyondLongMaximum()
    {
        var conflict = ProfileSequenceNumbering.Analyze([long.MaxValue.ToString()], long.MaxValue.ToString());

        Assert.Equal(SequenceNumberConflictKind.NotNumeric, conflict.Kind);
        Assert.Null(conflict.AppendNumber);
    }

    [Fact]
    public void Analyze_KeepsShiftedNumbersUnique()
    {
        string[] ids = ["3", "7", "8", "12", "A"];
        var conflict = ProfileSequenceNumbering.Analyze(ids, "7");

        var shifted = ids.ToArray();
        foreach (var entry in conflict.ShiftEntries)
            shifted[entry.Index] = entry.NewNumber.ToString();

        Assert.Equal(SequenceNumberConflictKind.Shiftable, conflict.Kind);
        Assert.Equal(shifted.Length, shifted.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("7", shifted);
        Assert.Contains("7", ids);
    }
}
