using System.Text;

namespace SecRandom.Platforms;

/// <summary>
///     Linux freedesktop <c>.desktop</c> 入口文件的纯解析逻辑：只读取 <c>[Desktop Entry]</c> 段里的
///     <c>Icon</c> 与 <c>Exec</c> 值，不触碰文件系统或进程，便于单独测试。
/// </summary>
public static class DesktopEntryIconReader
{
    private const string DesktopEntrySection = "[Desktop Entry]";

    /// <summary>返回 <c>Icon=</c> 的值（已去掉首尾引号）；找不到时返回 <c>null</c>。</summary>
    public static string? ReadIconValue(string? content)
    {
        return ReadKeyValue(content, "Icon");
    }

    /// <summary>返回 <c>Exec=</c> 的第一个可执行 token（已去掉引号、跳过 <c>env</c> 赋值前缀）。</summary>
    public static string? ReadExecutable(string? content)
    {
        var value = ReadKeyValue(content, "Exec");
        return string.IsNullOrWhiteSpace(value) ? null : GetExecutableToken(value);
    }

    /// <summary>读取 <c>[Desktop Entry]</c> 段中的某个键；同名键取第一次出现的值。</summary>
    public static string? ReadKeyValue(string? content, string key)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(key))
            return null;

        var inSection = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line[0] == '[')
            {
                inSection = string.Equals(line, DesktopEntrySection, StringComparison.Ordinal);
                continue;
            }

            if (!inSection)
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            if (!string.Equals(line[..separator].Trim(), key, StringComparison.Ordinal))
                continue;

            return Unquote(line[(separator + 1)..].Trim());
        }

        return null;
    }

    private static string? GetExecutableToken(string commandLine)
    {
        var tokens = Tokenize(commandLine);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (i == 0 && string.Equals(token, "env", StringComparison.Ordinal))
                continue;

            // env 的 VAR=VALUE 前缀和环境变量赋值都不是可执行文件。
            if (token.Contains('=', StringComparison.Ordinal) && !token.Contains('/', StringComparison.Ordinal))
                continue;

            return token;
        }

        return null;
    }

    private static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';

        foreach (var ch in commandLine)
        {
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else
                    current.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];

        return value;
    }
}
