namespace SecRandom.Core.Services.Security.PasswordStrength;

/// <summary>
/// Coarse strength bands. The numeric value doubles as the number of filled segments in the
/// settings dialog, so the order of the members is part of the UI contract.
/// </summary>
public enum PasswordStrengthLevel
{
    VeryWeak = 0,
    Weak = 1,
    Fair = 2,
    Strong = 3,
    VeryStrong = 4
}

/// <summary>
/// The single most useful piece of feedback for a password that is not strong yet. The UI turns
/// this into one localized hint; the estimator itself never produces user-facing text.
/// </summary>
public enum PasswordStrengthIssue
{
    None,
    TooShort,
    CommonPassword,
    PredictablePattern,
    ShortPassword,
    WeakComposition
}

public sealed record PasswordStrengthResult(
    PasswordStrengthLevel Level,
    double Log10Guesses,
    PasswordStrengthIssue Issue)
{
    public int Score => (int)Level;

    public bool IsTooShort => Issue == PasswordStrengthIssue.TooShort;
}

/// <summary>
/// Estimates how hard a password would be to guess, zxcvbn-style: it looks for the cheapest way
/// an attacker could describe the string (dictionary words including leetspeak and reversed
/// spellings, keyboard walks, sequences, repeats, dates, phone numbers) and only falls back to
/// brute force for the leftovers. It deliberately reports a band instead of enforcing rules:
/// "correct horse battery staple" is long and predictable, "P@ssw0rd!" satisfies every character
/// class rule and is still trivial.
/// </summary>
public static class PasswordStrengthEvaluator
{
    /// <summary>Mirrors <c>ISecurityService</c>; strength is advisory, the floor stays at six.</summary>
    public const int MinimumLength = 6;

    private const int MaxEvaluatedLength = 1024;

    // log10(2). Stands in for zxcvbn's factorial-of-segments term, which would need a second DP
    // dimension; one small constant per segment keeps the search one-dimensional and still stops
    // a string of many tiny dictionary words from looking cheaper than it is.
    private const double SegmentPenaltyLog10 = 0.30103;

    private const int CommonPasswordRankThreshold = 1000;
    private const int DigitOnlyCapLength = 11;
    private const int DigitOnlyScoreCap = 2;
    private const int ShortPasswordHintLength = 12;

    private static readonly PasswordStrengthResult Empty =
        new(PasswordStrengthLevel.VeryWeak, 0, PasswordStrengthIssue.TooShort);

    public static PasswordStrengthResult Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return Empty;

        var fullLength = password.Length;
        var length = Math.Min(fullLength, MaxEvaluatedLength);

        var extraLog10Guesses = 0.0;
        for (var index = length; index < fullLength; index++)
            extraLog10Guesses += Math.Log10(PasswordPatternMatcher.CharacterPoolSize(password[index]));

        var poolLog10 = BuildCharacterPoolPrefix(password, length);
        var cover = FindCheapestCover(password, length, poolLog10);

        var totalLog10Guesses = cover.TotalLog10 + extraLog10Guesses;
        if (extraLog10Guesses > 0)
            totalLog10Guesses += SegmentPenaltyLog10;

        var fullStringIsCommonPassword =
            length == fullLength && IsCommonPasswordWholeString(cover.MatchesByStart, length);

        var level = LevelFor(totalLog10Guesses);
        if (fullLength < MinimumLength)
        {
            level = PasswordStrengthLevel.VeryWeak;
        }
        else
        {
            if (fullStringIsCommonPassword)
                level = PasswordStrengthLevel.VeryWeak;

            if (fullLength <= DigitOnlyCapLength && IsAsciiDigits(password))
                level = (PasswordStrengthLevel)Math.Min((int)level, DigitOnlyScoreCap);
        }

        var issue = fullLength < MinimumLength
            ? PasswordStrengthIssue.TooShort
            : level >= PasswordStrengthLevel.Strong
                ? PasswordStrengthIssue.None
                : DescribeIssue(length, fullStringIsCommonPassword, cover);

        return new PasswordStrengthResult(level, Math.Max(totalLog10Guesses, 0.0), issue);
    }

    private static PasswordStrengthLevel LevelFor(double log10Guesses) => log10Guesses switch
    {
        < 3 => PasswordStrengthLevel.VeryWeak,
        < 6 => PasswordStrengthLevel.Weak,
        < 8 => PasswordStrengthLevel.Fair,
        < 10 => PasswordStrengthLevel.Strong,
        _ => PasswordStrengthLevel.VeryStrong
    };

    private static double[] BuildCharacterPoolPrefix(string password, int length)
    {
        var prefix = new double[length + 1];
        for (var index = 0; index < length; index++)
            prefix[index + 1] = prefix[index] + Math.Log10(PasswordPatternMatcher.CharacterPoolSize(password[index]));

        return prefix;
    }

    private sealed record Covering(
        double TotalLog10,
        int[] PreviousStart,
        PasswordMatchKind[] PreviousKind,
        List<PasswordMatch>[] MatchesByStart);

    /// <summary>
    /// Minimum-cost covering of the password where every segment is either a structural match or
    /// a brute-force run, and every segment carries the small multi-segment penalty.
    /// </summary>
    private static Covering FindCheapestCover(string password, int length, double[] poolLog10)
    {
        var matchesByStart = PasswordPatternMatcher.FindMatches(password, length);

        var best = new double[length + 1];
        var previousStart = new int[length + 1];
        var previousKind = new PasswordMatchKind[length + 1];
        Array.Fill(best, double.PositiveInfinity);
        Array.Fill(previousStart, -1);
        best[0] = 0;

        for (var start = 0; start < length; start++)
        {
            var baseCost = best[start];
            if (double.IsPositiveInfinity(baseCost))
                continue;

            foreach (var match in matchesByStart[start])
            {
                if (match.End > length)
                    continue;

                var candidate = baseCost + match.Log10Guesses + SegmentPenaltyLog10;
                if (candidate < best[match.End])
                {
                    best[match.End] = candidate;
                    previousStart[match.End] = start;
                    previousKind[match.End] = match.Kind;
                }
            }

            for (var end = start + 1; end <= length; end++)
            {
                var candidate = baseCost + (poolLog10[end] - poolLog10[start]) + SegmentPenaltyLog10;
                if (candidate < best[end])
                {
                    best[end] = candidate;
                    previousStart[end] = start;
                    previousKind[end] = PasswordMatchKind.BruteForce;
                }
            }
        }

        return new Covering(best[length], previousStart, previousKind, matchesByStart);
    }

    private static bool IsCommonPasswordWholeString(List<PasswordMatch>[] matchesByStart, int length)
    {
        if (length == 0 || matchesByStart.Length == 0)
            return false;

        foreach (var match in matchesByStart[0])
        {
            if (match.End == length &&
                match.Kind == PasswordMatchKind.Dictionary &&
                match.Rank <= CommonPasswordRankThreshold)
                return true;
        }

        return false;
    }

    private static PasswordStrengthIssue DescribeIssue(int length, bool fullStringIsCommonPassword, Covering cover)
    {
        if (fullStringIsCommonPassword)
            return PasswordStrengthIssue.CommonPassword;

        var dictionaryCharacters = 0;
        var patternCharacters = 0;
        for (var end = length; end > 0;)
        {
            var start = cover.PreviousStart[end];
            if (start < 0)
                break;

            var characters = end - start;
            switch (cover.PreviousKind[end])
            {
                case PasswordMatchKind.Dictionary:
                case PasswordMatchKind.LeetDictionary:
                case PasswordMatchKind.ReversedDictionary:
                    dictionaryCharacters += characters;
                    break;
                case PasswordMatchKind.Sequence:
                case PasswordMatchKind.KeyboardWalk:
                case PasswordMatchKind.Repeat:
                case PasswordMatchKind.Date:
                case PasswordMatchKind.Phone:
                    patternCharacters += characters;
                    break;
            }

            end = start;
        }

        if (dictionaryCharacters * 2 >= length)
            return PasswordStrengthIssue.CommonPassword;

        if (patternCharacters * 2 >= length)
            return PasswordStrengthIssue.PredictablePattern;

        if (length < ShortPasswordHintLength)
            return PasswordStrengthIssue.ShortPassword;

        return PasswordStrengthIssue.WeakComposition;
    }

    private static bool IsAsciiDigits(string password)
    {
        foreach (var value in password)
        {
            if (value is < '0' or > '9')
                return false;
        }

        return true;
    }
}
