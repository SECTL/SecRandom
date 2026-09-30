using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Verification;

/// <summary>
///     Commits the RecordId-to-display mapping of the roster a draw was taken from.
///     A proof already commits the candidate pool, but that pool is anonymous: it carries record ids and
///     weights only, never names. Without this digest, editing the list after the draw silently re-points a
///     winning record id at a different person while every receipt, replay check, and time stamp still
///     validates — the mapping is the one thing the draw result is read through.
///     Only a digest leaves the device; names, student numbers, groups, and gender never do.
/// </summary>
public static class RosterDigest
{
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SecRandomProof/v3/roster");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(StudentList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        var entries = list.Students
            .OrderBy(student => student.RecordId.ToString("N"), StringComparer.Ordinal)
            .Select(student => new
            {
                recordId = student.RecordId.ToString("N"),
                id = student.Id,
                name = student.Name,
                group = student.Group,
                gender = student.Gender,
                exists = student.Exists
            })
            .ToArray();
        return Compute("student", entries);
    }

    public static string Compute(PrizeList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        var entries = list.Prizes
            .OrderBy(prize => prize.RecordId.ToString("N"), StringComparer.Ordinal)
            .Select(prize => new
            {
                recordId = prize.RecordId.ToString("N"),
                id = prize.Id,
                name = prize.Name,
                exists = prize.Exists,
                count = prize.Count,
                weight = prize.Weight
            })
            .ToArray();
        return Compute("prize", entries);
    }

    // Entries are sorted by record id before hashing, so reordering the list is not a change while renaming,
    // renumbering, regrouping, or disabling a record is.
    private static string Compute<T>(string kind, T[] entries)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { kind, entries }, JsonOptions);
        var material = new byte[DomainSeparator.Length + payload.Length];
        DomainSeparator.CopyTo(material, 0);
        payload.CopyTo(material, DomainSeparator.Length);
        return WitnessClient.ToBase64Url(SHA256.HashData(material));
    }
}
