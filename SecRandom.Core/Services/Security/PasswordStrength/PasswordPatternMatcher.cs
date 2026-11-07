namespace SecRandom.Core.Services.Security.PasswordStrength;

internal enum PasswordMatchKind
{
    BruteForce,
    Dictionary,
    LeetDictionary,
    ReversedDictionary,
    Sequence,
    Repeat,
    KeyboardWalk,
    Date,
    Phone
}

/// <summary>
/// One candidate way to explain a slice of the password, together with how many guesses that
/// explanation implies. The estimator then picks the cheapest non-overlapping set of them.
/// </summary>
internal readonly record struct PasswordMatch(
    int Start,
    int End,
    double Log10Guesses,
    PasswordMatchKind Kind,
    int Rank = 0)
{
    public int Length => End - Start;
}

/// <summary>
/// Pattern matchers in the spirit of zxcvbn: instead of a rule list ("must contain a digit"),
/// the estimator asks how a real attacker would guess this particular string.
/// </summary>
internal static class PasswordPatternMatcher
{
    public const int MaxDictionaryTokenLength = 32;
    public const int MaxPatternLength = 32;

    private const int MaxUppercaseFactorExponent = 6;
    private const int MaxLeetFactorExponent = 6;

    private static readonly string[] KeyboardRows =
    {
        "`1234567890-=",
        "qwertyuiop[]\\",
        "asdfghjkl;'",
        "zxcvbnm,./"
    };

    private static readonly Dictionary<char, HashSet<char>> KeyboardNeighbors = BuildKeyboardNeighbors();

    private static readonly Dictionary<char, char> LeetReplacements = new()
    {
        ['@'] = 'a',
        ['4'] = 'a',
        ['0'] = 'o',
        ['1'] = 'i',
        ['3'] = 'e',
        ['5'] = 's',
        ['$'] = 's',
        ['7'] = 't',
        ['8'] = 'b',
        ['9'] = 'g',
        ['2'] = 'z',
        ['!'] = 'i',
        ['+'] = 't',
        ['|'] = 'i'
    };

    /// <summary>
    /// Collects every structural match that starts at each position of <paramref name="password"/>,
    /// considering only the first <paramref name="length"/> characters.
    /// </summary>
    public static List<PasswordMatch>[] FindMatches(string password, int length)
    {
        var matchesByStart = new List<PasswordMatch>[length];
        for (var index = 0; index < length; index++)
            matchesByStart[index] = new List<PasswordMatch>();

        for (var start = 0; start < length; start++)
        {
            var available = length - start;
            var dictionaryLimit = Math.Min(available, MaxDictionaryTokenLength);
            for (var tokenLength = 1; tokenLength <= dictionaryLimit; tokenLength++)
                AddDictionaryMatch(password, start, tokenLength, matchesByStart[start]);

            var patternLimit = Math.Min(available, MaxPatternLength);
            for (var patternLength = 3; patternLength <= patternLimit; patternLength++)
            {
                AddPatternMatch(password, start, patternLength, matchesByStart[start]);
            }
        }

        AddPhoneMatch(password, length, matchesByStart);
        return matchesByStart;
    }

    /// <summary>
    /// How many roughly-equally-likely characters a plain brute-force search would try for one
    /// character. The numbers are coarse on purpose: they only have to be within an order of
    /// magnitude, and over-estimating a script's alphabet would flatter a weak password.
    /// </summary>
    public static double CharacterPoolSize(char value)
    {
        if (value is >= '0' and <= '9')
            return 10;
        if (value is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            return 26;
        if (char.IsWhiteSpace(value))
            return 10;
        if (char.IsDigit(value))
            return 10;
        if (IsCjk(value))
            return 3500;
        if (char.IsLetter(value))
            return 100;
        return value < 128 ? 33 : 200;
    }

    private static void AddPatternMatch(string password, int start, int length, List<PasswordMatch> output)
    {
        var end = start + length;

        if (TrySequence(password, start, length, out var sequenceLog10))
            output.Add(new PasswordMatch(start, end, sequenceLog10, PasswordMatchKind.Sequence));

        if (TryKeyboardWalk(password, start, length, out var keyboardLog10))
            output.Add(new PasswordMatch(start, end, keyboardLog10, PasswordMatchKind.KeyboardWalk));

        if (TryRepeat(password, start, length, out var repeatLog10))
            output.Add(new PasswordMatch(start, end, repeatLog10, PasswordMatchKind.Repeat));

        if (TryDate(password, start, length, out var dateLog10))
            output.Add(new PasswordMatch(start, end, dateLog10, PasswordMatchKind.Date));
    }

    private static void AddDictionaryMatch(string password, int start, int length, List<PasswordMatch> output)
    {
        var token = password.Substring(start, length);
        var lowered = PasswordStrengthDictionary.Normalize(token);
        var end = start + length;

        AddRankedMatch(lowered, PasswordMatchKind.Dictionary, leetCount: 0, uppercaseCount: CountUppercase(token),
            reversed: false, start, end, output);

        if (TryBuildLeetCandidate(lowered, out var unleet, out var leetCount))
        {
            AddRankedMatch(unleet, PasswordMatchKind.LeetDictionary, leetCount, CountUppercase(token),
                reversed: false, start, end, output);
        }

        var reversed = Reverse(lowered);
        if (!string.Equals(reversed, lowered, StringComparison.Ordinal))
        {
            AddRankedMatch(reversed, PasswordMatchKind.ReversedDictionary, leetCount: 0, CountUppercase(token),
                reversed: true, start, end, output);
        }
    }

    private static void AddRankedMatch(
        string candidate,
        PasswordMatchKind kind,
        int leetCount,
        int uppercaseCount,
        bool reversed,
        int start,
        int end,
        List<PasswordMatch> output)
    {
        if (!PasswordStrengthDictionary.TryGetRank(candidate, out var rank))
            return;

        var uppercaseExponent = Math.Min(uppercaseCount, MaxUppercaseFactorExponent);
        var leetExponent = Math.Min(leetCount, MaxLeetFactorExponent);
        var transforms = Math.Pow(2, uppercaseExponent + leetExponent) * (reversed ? 2 : 1);
        var guesses = Math.Max(rank, 1) * transforms;

        output.Add(new PasswordMatch(start, end, Math.Log10(guesses), kind, rank));
    }

    private static bool TryBuildLeetCandidate(string lowered, out string unleet, out int leetCount)
    {
        leetCount = 0;
        char[]? buffer = null;

        for (var index = 0; index < lowered.Length; index++)
        {
            var value = lowered[index];
            if (!LeetReplacements.TryGetValue(value, out var replacement))
                continue;

            buffer ??= lowered.ToCharArray();
            buffer[index] = replacement;
            leetCount++;
        }

        unleet = buffer is null ? lowered : new string(buffer);
        return leetCount > 0;
    }

    private static bool TrySequence(string password, int start, int length, out double log10Guesses)
    {
        log10Guesses = 0;
        if (length < 3)
            return false;

        var ascending = true;
        var descending = true;
        for (var index = start; index < start + length; index++)
        {
            if (!char.IsLetterOrDigit(password[index]))
                return false;

            if (index == start)
                continue;

            var difference = password[index] - password[index - 1];
            if (difference != 1)
                ascending = false;
            if (difference != -1)
                descending = false;
            if (!ascending && !descending)
                return false;
        }

        log10Guesses = Math.Log10(4.0 * length);
        return true;
    }

    private static bool TryKeyboardWalk(string password, int start, int length, out double log10Guesses)
    {
        log10Guesses = 0;
        if (length < 3)
            return false;

        for (var index = start; index < start + length; index++)
        {
            var value = char.ToLowerInvariant(password[index]);
            if (!KeyboardNeighbors.ContainsKey(value))
                return false;
            if (index == start)
                continue;

            var previous = char.ToLowerInvariant(password[index - 1]);
            if (!KeyboardNeighbors[previous].Contains(value))
                return false;
        }

        log10Guesses = Math.Log10(50.0 * length);
        return true;
    }

    private static bool TryRepeat(string password, int start, int length, out double log10Guesses)
    {
        log10Guesses = 0;
        if (length < 3)
            return false;

        for (var period = 1; period <= length / 2; period++)
        {
            var isRepeat = true;
            for (var index = start + period; index < start + length; index++)
            {
                if (password[index] == password[index - period])
                    continue;

                isRepeat = false;
                break;
            }

            if (!isRepeat)
                continue;

            var repeatCount = (length + period - 1) / period;
            log10Guesses = UnitLog10(password, start, period) + Math.Log10(repeatCount);
            return true;
        }

        return false;
    }

    private static double UnitLog10(string password, int start, int length)
    {
        var unit = password.Substring(start, length);
        if (PasswordStrengthDictionary.TryGetRank(unit, out var rank))
            return Math.Log10(rank);

        var log10 = 0.0;
        for (var index = start; index < start + length; index++)
            log10 += Math.Log10(CharacterPoolSize(password[index]));
        return log10;
    }

    private static bool TryDate(string password, int start, int length, out double log10Guesses)
    {
        log10Guesses = 0;
        for (var index = start; index < start + length; index++)
        {
            if (password[index] is < '0' or > '9')
                return false;
        }

        var value = password.Substring(start, length);
        switch (length)
        {
            case 4:
                if (int.TryParse(value, out var year) && year is >= 1900 and <= 2099)
                {
                    log10Guesses = 2.0;
                    return true;
                }

                return false;
            case 6:
                if (IsValidDate(value[4..6], value[2..4], value[..2]) ||
                    IsValidDate(value[..2], value[2..4], value[4..6]))
                {
                    log10Guesses = 3.3;
                    return true;
                }

                return false;
            case 8:
                if (IsValidDate(value[6..8], value[4..6], value[..4]))
                {
                    log10Guesses = 3.3;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool IsValidDate(string dayText, string monthText, string yearText)
    {
        if (!int.TryParse(dayText, out var day) || !int.TryParse(monthText, out var month) ||
            !int.TryParse(yearText, out _))
            return false;

        return month is >= 1 and <= 12 && day is >= 1 and <= 31;
    }

    private static void AddPhoneMatch(string password, int length, List<PasswordMatch>[] matchesByStart)
    {
        if (length != password.Length || length != 11)
            return;

        if (password[0] != '1' || password[1] is < '3' or > '9')
            return;

        for (var index = 2; index < length; index++)
        {
            if (password[index] is < '0' or > '9')
                return;
        }

        matchesByStart[0].Add(new PasswordMatch(0, length, 5.0, PasswordMatchKind.Phone));
    }

    private static Dictionary<char, HashSet<char>> BuildKeyboardNeighbors()
    {
        var neighbors = new Dictionary<char, HashSet<char>>();

        for (var row = 0; row < KeyboardRows.Length; row++)
        {
            var keys = KeyboardRows[row];
            for (var column = 0; column < keys.Length; column++)
            {
                var key = keys[column];
                AddNeighbor(neighbors, key, column > 0 ? keys[column - 1] : null);
                AddNeighbor(neighbors, key, column + 1 < keys.Length ? keys[column + 1] : null);

                if (row > 0)
                    AddAdjacentRowNeighbors(neighbors, key, KeyboardRows[row - 1], column);
                if (row + 1 < KeyboardRows.Length)
                    AddAdjacentRowNeighbors(neighbors, key, KeyboardRows[row + 1], column);
            }
        }

        return neighbors;
    }

    private static void AddAdjacentRowNeighbors(
        Dictionary<char, HashSet<char>> neighbors,
        char key,
        string adjacentRow,
        int column)
    {
        if (column < adjacentRow.Length)
            AddNeighbor(neighbors, key, adjacentRow[column]);
        if (column > 0 && column - 1 < adjacentRow.Length)
            AddNeighbor(neighbors, key, adjacentRow[column - 1]);
        if (column + 1 < adjacentRow.Length)
            AddNeighbor(neighbors, key, adjacentRow[column + 1]);
    }

    private static void AddNeighbor(Dictionary<char, HashSet<char>> neighbors, char key, char? neighbor)
    {
        if (neighbor is not { } value || value == key)
            return;

        if (!neighbors.TryGetValue(key, out var set))
            neighbors[key] = set = new HashSet<char>();
        set.Add(value);
    }

    private static int CountUppercase(string value)
    {
        var count = 0;
        foreach (var character in value)
        {
            if (char.IsUpper(character))
                count++;
        }

        return count;
    }

    private static string Reverse(string value)
    {
        var characters = value.ToCharArray();
        Array.Reverse(characters);
        return new string(characters);
    }

    private static bool IsCjk(char value) =>
        value is >= '\u3400' and <= '\u4dbf' or >= '\u4e00' and <= '\u9fff' or >= '\uf900' and <= '\ufaff' or
            >= '\u3040' and <= '\u30ff' or >= '\uac00' and <= '\ud7af';
}
