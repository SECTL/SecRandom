using System.Text.RegularExpressions;

namespace SecRandom.Services.ImportExport;

internal static partial class DiagnosticTextRedactor
{
    public static string Redact(string text)
    {
        text = BearerTokenRegex().Replace(text, "Bearer <redacted>");
        text = SecretValueRegex().Replace(text, "$1<redacted>");
        text = AuthorizationRegex().Replace(text, "$1$2<redacted>");
        text = FilePathRegex().Replace(text, "<path>");
        return EmailRegex().Replace(text, "<email>");
    }

    [GeneratedRegex("(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]+=*")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?i)(\\\"?(?:password|secret|token)\\\"?\\s*[:=]\\s*)(\\\"[^\\\"]*\\\"|[^\\s,;}\\]]+)")]
    private static partial Regex SecretValueRegex();

    [GeneratedRegex("(?i)(authorization\\s*[:=]\\s*)(\\\"?Bearer\\s+)?(\\\"[^\\\"]*\\\"|[^\\s,;}\\]]+)")]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("[A-Za-z]:\\\\[^\\s\"]+|/[^\\s\"]+")]
    private static partial Regex FilePathRegex();

    [GeneratedRegex("[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
