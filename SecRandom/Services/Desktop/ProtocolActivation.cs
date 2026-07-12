using System;
using System.Collections.Generic;
using System.Linq;

namespace SecRandom.Services.Desktop;

public static class ProtocolActivation
{
    private static string? _startupUri;

    public static void SetStartupArguments(IReadOnlyList<string> arguments)
    {
        _startupUri = ExtractUri(arguments);
    }

    public static string? ConsumeStartupUri()
    {
        var uri = _startupUri;
        _startupUri = null;
        return uri;
    }

    private static string? ExtractUri(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "--url", StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Count)
                return Validate(arguments[index + 1]);

            var uri = Validate(argument);
            if (uri is not null)
                return uri;
        }

        return null;
    }

    private static string? Validate(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && string.Equals(uri.Scheme, "secrandom", StringComparison.OrdinalIgnoreCase)
               && !value.Contains('\r')
               && !value.Contains('\n')
            ? uri.AbsoluteUri
            : null;
    }
}
