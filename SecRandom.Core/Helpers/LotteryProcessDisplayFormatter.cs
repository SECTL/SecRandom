using System.Text.RegularExpressions;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Helpers;

public static partial class LotteryProcessDisplayFormatter
{
    public const string DefaultTemplate = "{id} {prize}{/}{group}-{member}";

    [GeneratedRegex(" {2,}")]
    private static partial Regex ConsecutiveSpaces();

    public static string NormalizeTemplate(string? template)
    {
        var normalized = (template ?? string.Empty)
            .Replace("\r\n", "{/}", StringComparison.Ordinal)
            .Replace("\r", "{/}", StringComparison.Ordinal)
            .Replace("\n", "{/}", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        return ConsecutiveSpaces().Replace(normalized, " ");
    }

    public static string ResolveTemplate(LotteryShowRandomMode mode, string? customTemplate)
    {
        return mode switch
        {
            LotteryShowRandomMode.PrizeIdPrizeBreakGroupHyphenMember => DefaultTemplate,
            LotteryShowRandomMode.PrizeBreakGroupHyphenMember => "{prize}{/}{group}-{member}",
            LotteryShowRandomMode.PrizeHyphenMember => "{prize}-{member}",
            LotteryShowRandomMode.PrizeHyphenGroup => "{prize}-{group}",
            LotteryShowRandomMode.Custom => NormalizeTemplate(customTemplate),
            _ => DefaultTemplate
        };
    }

    public static string Format(
        string? template,
        string? id,
        string? prizeId,
        string? prize,
        string? group,
        string? memberId,
        string? member)
    {
        var formatted = NormalizeTemplate(template)
            .Replace("{id}", id ?? string.Empty, StringComparison.Ordinal)
            .Replace("{prizeId}", prizeId ?? string.Empty, StringComparison.Ordinal)
            .Replace("{prize}", prize ?? string.Empty, StringComparison.Ordinal)
            .Replace("{group}", group ?? string.Empty, StringComparison.Ordinal)
            .Replace("{memberId}", memberId ?? string.Empty, StringComparison.Ordinal)
            .Replace("{member}", member ?? string.Empty, StringComparison.Ordinal)
            .Replace("{/}", "\n", StringComparison.Ordinal);

        return string.Join("\n", formatted
            .Split('\n')
            .Select(static line => line.Trim(' ', '-'))
            .Where(static line => !string.IsNullOrWhiteSpace(line)));
    }
}
