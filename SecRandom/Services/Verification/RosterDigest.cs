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
    /// <summary>
    ///     Length of the per-proof salt. Sixteen bytes keeps the published value short while leaving a value space
    ///     no proof collection can collide with.
    /// </summary>
    public const int SaltLength = 16;

    /// <summary>
    ///     The original, unsalted digest format. Retained because files already written under it must keep both
    ///     verifying and re-deriving.
    /// </summary>
    public const string FormatV3 = "v3";

    /// <summary>
    ///     Salted digest format. The digest is published together with its salt, so the salt buys unlinkability
    ///     (one roster in two proofs no longer produces one digest) rather than secrecy.
    /// </summary>
    public const string FormatV4 = "v4";

    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SecRandomProof/v3/roster");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(StudentList list) => Compute(list, []);

    public static string Compute(StudentList list, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(salt);

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
        return Compute("student", entries, salt);
    }

    public static string Compute(PrizeList list) => Compute(list, []);

    public static string Compute(PrizeList list, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(salt);

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
        return Compute("prize", entries, salt);
    }

    // Entries are sorted by record id before hashing, so reordering the list is not a change while renaming,
    // renumbering, regrouping, or disabling a record is. The salt sits between the domain separator and the
    // payload: it is a per-proof random value of fixed length, so no payload can be shifted into its slot.
    private static string Compute<T>(string kind, T[] entries, byte[] salt)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { kind, entries }, JsonOptions);
        var material = new byte[DomainSeparator.Length + salt.Length + payload.Length];
        DomainSeparator.CopyTo(material, 0);
        salt.CopyTo(material, DomainSeparator.Length);
        payload.CopyTo(material, DomainSeparator.Length + salt.Length);
        return WitnessClient.ToBase64Url(SHA256.HashData(material));
    }
}
