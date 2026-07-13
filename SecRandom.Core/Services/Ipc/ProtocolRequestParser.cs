namespace SecRandom.Core.Services.Ipc;

public sealed record ProtocolQueryItem(string Key, string Value);

public sealed record ParsedProtocolRequest(
    string Route,
    IReadOnlyList<ProtocolQueryItem> Query,
    bool IsFullUri);

public sealed record ProtocolParseFailure(string Code, string Message);

public static class ProtocolRequestParser
{
    public const int MaxRequestLength = 8 * 1024;
    private const int MaxQueryItems = 32;
    private const int MaxQueryValueLength = 1024;

    public static bool TryParse(string value, bool requireSecRandomScheme, out ParsedProtocolRequest? request,
        out ProtocolParseFailure? failure)
    {
        request = null;
        failure = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxRequestLength || value.Contains('#') || ContainsControlCharacter(value)
            || !HasValidPercentEscapes(value))
        {
            failure = new("invalid_request", "请求格式无效。");
            return false;
        }

        var isFullUri = Uri.TryCreate(value, UriKind.Absolute, out var uri);
        string pathAndQuery;
        if (isFullUri)
        {
            if (!string.Equals(uri!.Scheme, "secrandom", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(uri.Host)
                || uri.Port != -1
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                failure = new("invalid_command", "协议命令无效。");
                return false;
            }

            pathAndQuery = string.Concat(uri.Host, uri.AbsolutePath, uri.Query);
        }
        else
        {
            if (requireSecRandomScheme)
            {
                failure = new("invalid_command", "协议命令无效。");
                return false;
            }

            pathAndQuery = value;
        }

        var separator = pathAndQuery.IndexOf('?');
        var rawPath = separator < 0 ? pathAndQuery : pathAndQuery[..separator];
        var rawQuery = separator < 0 ? string.Empty : pathAndQuery[(separator + 1)..];
        if (!TryDecode(rawPath.Trim('/'), out var route) || string.IsNullOrWhiteSpace(route))
        {
            failure = new("invalid_command", "协议命令无效。");
            return false;
        }

        route = route.ToLowerInvariant();
        if (route.Any(character => char.IsControl(character)))
        {
            failure = new("invalid_command", "协议命令无效。");
            return false;
        }

        var query = new List<ProtocolQueryItem>();
        if (!string.IsNullOrEmpty(rawQuery))
        {
            foreach (var pair in rawQuery.Split('&', StringSplitOptions.None))
            {
                if (query.Count >= MaxQueryItems)
                {
                    failure = new("invalid_request", "请求参数过多。");
                    return false;
                }

                var equals = pair.IndexOf('=');
                var rawKey = equals < 0 ? pair : pair[..equals];
                var rawValue = equals < 0 ? string.Empty : pair[(equals + 1)..];
                if (!TryDecode(rawKey, out var key) || string.IsNullOrWhiteSpace(key) || !TryDecode(rawValue, out var queryValue)
                    || queryValue.Length > MaxQueryValueLength || ContainsControlCharacter(key) || ContainsControlCharacter(queryValue))
                {
                    failure = new("invalid_parameter", "请求参数无效。");
                    return false;
                }

                query.Add(new ProtocolQueryItem(key.ToLowerInvariant(), queryValue));
            }
        }

        request = new ParsedProtocolRequest(route, query, isFullUri);
        return true;
    }

    public static string? GetLast(IReadOnlyList<ProtocolQueryItem> query, params string[] aliases)
    {
        for (var index = query.Count - 1; index >= 0; index--)
        {
            if (aliases.Contains(query[index].Key, StringComparer.OrdinalIgnoreCase))
                return query[index].Value;
        }

        return null;
    }

    private static bool TryDecode(string value, out string decoded)
    {
        try
        {
            decoded = Uri.UnescapeDataString(value.Replace('+', ' '));
            return true;
        }
        catch (UriFormatException)
        {
            decoded = string.Empty;
            return false;
        }
    }

    private static bool ContainsControlCharacter(string value)
    {
        return value.Any(char.IsControl);
    }

    private static bool HasValidPercentEscapes(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
                continue;
            if (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2]))
                return false;
            index += 2;
        }

        return true;
    }
}
