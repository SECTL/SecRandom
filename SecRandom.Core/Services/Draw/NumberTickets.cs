using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SecRandom.Shared.Models.Profile;
using SecRandom.Core.Services.Profiles;

namespace SecRandom.Core.Services.Draw;

/// <summary>
///     Builds the synthetic candidates used by the "no roster" number-range draw. A number ticket is not a
///     stored roster member: its <see cref="Student.RecordId" /> / <see cref="Prize.RecordId" /> is a
///     deterministic name-based Guid derived from the range and the number, so the same visible number keeps
///     the same proof identity across draws while two different ranges can never collide.
/// </summary>
public static class NumberTickets
{
    /// <summary>Inclusive ranges are supported; the UI enforces this same span limit.</summary>
    public const int MaxSpan = RosterSelection.MaxNumberRangeSpan;

    public static Guid CreateRecordId(int rangeStart, int rangeEnd, int number)
    {
        var canonical = string.Create(CultureInfo.InvariantCulture,
            $"SecRandom/number/v1/{rangeStart}:{rangeEnd}:{number}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static Student CreateStudent(int rangeStart, int rangeEnd, int number) =>
        new()
        {
            RecordId = CreateRecordId(rangeStart, rangeEnd, number),
            Id = number.ToString(CultureInfo.InvariantCulture),
            Name = number.ToString(CultureInfo.InvariantCulture),
            Exists = true
        };

    public static Prize CreatePrize(int rangeStart, int rangeEnd, int number) =>
        new()
        {
            RecordId = CreateRecordId(rangeStart, rangeEnd, number),
            Id = number.ToString(CultureInfo.InvariantCulture),
            Name = number.ToString(CultureInfo.InvariantCulture),
            Count = 1,
            Weight = 1,
            Exists = true
        };

    public static IReadOnlyList<Student> CreateStudents(int rangeStart, int rangeEnd) =>
        CreateNumbers(rangeStart, rangeEnd).Select(number => CreateStudent(rangeStart, rangeEnd, number)).ToArray();

    public static IReadOnlyList<Prize> CreatePrizes(int rangeStart, int rangeEnd) =>
        CreateNumbers(rangeStart, rangeEnd).Select(number => CreatePrize(rangeStart, rangeEnd, number)).ToArray();

    private static IEnumerable<int> CreateNumbers(int rangeStart, int rangeEnd)
    {
        if (!IsValidRange(rangeStart, rangeEnd))
            yield break;
        for (var number = rangeStart; number <= rangeEnd; number++)
            yield return number;
    }

    public static bool IsValidRange(int rangeStart, int rangeEnd) =>
        rangeStart >= 0 && rangeEnd >= rangeStart && rangeEnd - rangeStart + 1 <= MaxSpan;
}
