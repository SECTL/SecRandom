using SecRandom.Services.Verification;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

/// <summary>
///     The roster digest is the only commitment that ties a winning record id back to a person: the proof's
///     candidate pool is anonymous. It must follow the mapping and ignore incidental reordering.
/// </summary>
public sealed class RosterDigestTests
{
    [Fact]
    public void DigestIgnoresOrderingButFollowsTheMapping()
    {
        var first = new Student { RecordId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Id = "01", Name = "张三", Group = "A组" };
        var second = new Student { RecordId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Id = "02", Name = "李四", Group = "A组" };

        var baseline = RosterDigest.Compute(new StudentList { Students = [first, second] });
        Assert.Equal(baseline, RosterDigest.Compute(new StudentList { Students = [second, first] }));

        second.Name = "王五";
        Assert.NotEqual(baseline, RosterDigest.Compute(new StudentList { Students = [first, second] }));

        second.Name = "李四";
        second.Group = "B组";
        Assert.NotEqual(baseline, RosterDigest.Compute(new StudentList { Students = [first, second] }));

        second.Group = "A组";
        second.Exists = false;
        Assert.NotEqual(baseline, RosterDigest.Compute(new StudentList { Students = [first, second] }));
    }

    [Fact]
    public void PrizeDigestFollowsNameIdentityWeightAndCount()
    {
        var prize = new Prize { RecordId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Id = "P1", Name = "笔记本", Weight = 2, Count = 3 };

        var baseline = RosterDigest.Compute(new PrizeList { Prizes = [prize] });

        prize.Weight = 3;
        Assert.NotEqual(baseline, RosterDigest.Compute(new PrizeList { Prizes = [prize] }));

        prize.Weight = 2;
        prize.Count = 4;
        Assert.NotEqual(baseline, RosterDigest.Compute(new PrizeList { Prizes = [prize] }));

        prize.Count = 3;
        Assert.Equal(baseline, RosterDigest.Compute(new PrizeList { Prizes = [prize] }));
    }

    [Fact]
    public void SaltMakesTheSameRosterDigestDifferentPerProof()
    {
        var student = new Student { RecordId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Id = "01", Name = "张三" };
        StudentList list = new() { Students = [student] };
        byte[] firstSalt = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        byte[] secondSalt = [16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1];

        var first = RosterDigest.Compute(list, firstSalt);
        var second = RosterDigest.Compute(list, secondSalt);

        // Cross-proof joinability is exactly what the salt removes: two draws from one list must not look like
        // the same roster to someone holding both files.
        Assert.NotEqual(first, second);
        // The digest stays recomputable, because the salt ships inside the proof.
        Assert.Equal(first, RosterDigest.Compute(list, firstSalt));
        // And it still follows the mapping rather than the salt alone.
        student.Name = "李四";
        Assert.NotEqual(first, RosterDigest.Compute(list, firstSalt));
    }

    [Fact]
    public void UnsaltedDigestStaysTheLegacyV3Format()
    {
        var student = new Student { RecordId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Id = "01", Name = "张三" };
        StudentList list = new() { Students = [student] };

        // v3 proofs already on disk carry this value; the overload without a salt must keep deriving it.
        Assert.Equal(RosterDigest.Compute(list), RosterDigest.Compute(list, []));
        Assert.NotEqual(RosterDigest.Compute(list), RosterDigest.Compute(list, [0]));
    }

    [Fact]
    public void StudentAndPrizeRostersDoNotShareADigest()
    {
        Assert.NotEqual(
            RosterDigest.Compute(new StudentList()),
            RosterDigest.Compute(new PrizeList()));
    }
}
