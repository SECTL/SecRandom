using System.Globalization;

namespace SecRandom.Helpers;

public static class AvatarInitialResolver
{
    public static string Resolve(string? name, string? id)
    {
        var displayText = string.IsNullOrWhiteSpace(name) ? id : name;
        if (string.IsNullOrWhiteSpace(displayText))
            return "?";

        return StringInfo.GetNextTextElement(displayText.Trim());
    }
}
