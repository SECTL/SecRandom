using System.Globalization;
using System.Text;

namespace SecRandom.Core.Services.Security.PasswordStrength;

/// <summary>
/// Ranked list of very common passwords and password-shaped strings.
/// </summary>
/// <remarks>
/// The list is embedded in the assembly (<c>Data/common-passwords.txt</c>) and is only used to
/// rank how obvious a password token is. It is not a breach corpus, and it must never grow into
/// one: a large file would bloat every build and change nothing about the estimate's shape.
/// </remarks>
internal static class PasswordStrengthDictionary
{
    private const string ResourceName = "SecRandom.Core.Services.Security.PasswordStrength.Data.common-passwords.txt";

    private static readonly Lazy<IReadOnlyDictionary<string, int>> Ranked =
        new(Load, LazyThreadSafetyMode.PublicationOnly);

    public static int Count => Ranked.Value.Count;

    public static bool TryGetRank(string candidate, out int rank)
    {
        rank = 0;
        return candidate.Length != 0 && Ranked.Value.TryGetValue(Normalize(candidate), out rank);
    }

    /// <summary>
    /// Folds compatibility forms (full-width digits/letters, for example <c>１２３</c>) onto their
    /// canonical characters before matching, so an input method cannot disguise a known password.
    /// </summary>
    public static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    private static IReadOnlyDictionary<string, int> Load()
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);

        using var stream = typeof(PasswordStrengthDictionary).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            return ranks;

        using var reader = new StreamReader(stream);
        var rank = 0;
        while (reader.ReadLine() is { } line)
        {
            var value = line.Trim();
            if (value.Length == 0 || value[0] == '#')
                continue;

            rank++;
            ranks.TryAdd(Normalize(value), rank);
        }

        return ranks;
    }
}
