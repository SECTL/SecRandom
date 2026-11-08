using System.Text;
using System.Text.Json;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     v4.1 新增的四条动作命令的载荷约束（可单测，不碰界面）。
/// </summary>
/// <remarks>
///     <para>
///         协议规定的长度上限按**码点**（Unicode code point）算，不是按 <see cref="string.Length" />：
///         一个 emoji 在 UTF-16 里是两个 char，按 char 算会让 200 字的上限提前触发，
///         也会让截断切在代理对中间、产出半个字符。
///     </para>
///     <para>
///         未知字段一律忽略（协议 §2.13）：控制台可能先于客户端升级，多带一个字段不应让整条命令失败。
///     </para>
/// </remarks>
public static class ControlTextLimits
{
    /// <summary>按 Unicode 码点计数。</summary>
    public static int CountCodePoints(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : text.EnumerateRunes().Count();
}

/// <summary><c>broadcast</c>：在教室机全屏显示一段通知。</summary>
public sealed record ControlBroadcastRequest
{
    public const int MaxTextCodePoints = 200;
    public const int MaxSeconds = 3600;
    public const int DefaultSeconds = 10;

    public required string Text { get; init; }

    /// <summary>自动关闭秒数；缺省 10 秒。</summary>
    public int Seconds { get; init; } = DefaultSeconds;

    public static bool TryParse(JsonElement? payload, out ControlBroadcastRequest? request, out string? hint)
    {
        request = null;
        hint = null;

        if (payload is not { ValueKind: JsonValueKind.Object } element)
        {
            hint = "payload 必须是对象";
            return false;
        }

        var text = ControlPayloadReader.ReadString(element, "text")?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            hint = "text 不能为空";
            return false;
        }

        if (ControlTextLimits.CountCodePoints(text) > MaxTextCodePoints)
        {
            hint = $"text 最多 {MaxTextCodePoints} 个字符";
            return false;
        }

        var seconds = ControlPayloadReader.ReadInt(element, "seconds") ?? DefaultSeconds;
        if (seconds is < 1 or > MaxSeconds)
        {
            hint = $"seconds 必须在 1..{MaxSeconds} 之间";
            return false;
        }

        request = new ControlBroadcastRequest { Text = text, Seconds = seconds };
        return true;
    }
}

/// <summary><c>lockScreen</c>：锁住本机屏幕，直到远程解锁、计时到点或本机收回远控权。</summary>
public sealed record ControlLockScreenRequest
{
    public const int MaxMessageCodePoints = 60;

    /// <summary>缺省时不自动解锁：由控制台的 <c>draw_locked=false</c> 或本机开关解除。</summary>
    public string? Message { get; init; }

    /// <summary>自动解锁秒数；缺省不自动解锁。</summary>
    public int? UnlockAfterSeconds { get; init; }

    public static bool TryParse(JsonElement? payload, out ControlLockScreenRequest? request, out string? hint)
    {
        request = null;
        hint = null;

        if (payload is { ValueKind: JsonValueKind.Object } element)
        {
            var message = ControlPayloadReader.ReadString(element, "message")?.Trim();
            if (ControlTextLimits.CountCodePoints(message) > MaxMessageCodePoints)
            {
                hint = $"message 最多 {MaxMessageCodePoints} 个字符";
                return false;
            }

            var unlockAfter = ControlPayloadReader.ReadInt(element, "unlockAfterSeconds");
            if (unlockAfter is < 1 or > 86400)
            {
                hint = "unlockAfterSeconds 必须在 1..86400 之间";
                return false;
            }

            request = new ControlLockScreenRequest
            {
                Message = string.IsNullOrWhiteSpace(message) ? null : message,
                UnlockAfterSeconds = unlockAfter
            };
            return true;
        }

        // 允许空载荷：不带参数就是"立即锁定，由控制台解锁"。
        request = new ControlLockScreenRequest();
        return true;
    }
}

/// <summary><c>restartClient</c>：重启本机客户端。</summary>
public sealed record ControlRestartRequest
{
    public const int MaxDelaySeconds = 600;
    public const int DefaultDelaySeconds = 3;

    public int DelaySeconds { get; init; } = DefaultDelaySeconds;

    public static bool TryParse(JsonElement? payload, out ControlRestartRequest? request, out string? hint)
    {
        request = null;
        hint = null;

        var delay = DefaultDelaySeconds;
        if (payload is { ValueKind: JsonValueKind.Object } element)
            delay = ControlPayloadReader.ReadInt(element, "delaySeconds") ?? DefaultDelaySeconds;

        if (delay is < 0 or > MaxDelaySeconds)
        {
            hint = $"delaySeconds 必须在 0..{MaxDelaySeconds} 之间";
            return false;
        }

        request = new ControlRestartRequest { DelaySeconds = delay };
        return true;
    }
}

/// <summary><c>switchPage</c>：把教室机主界面切到指定页。</summary>
public sealed record ControlSwitchPageRequest
{
    public const string RollCallPage = "rollCall";
    public const string QuickDrawPage = "quickDraw";
    public const string LotteryPage = "lottery";

    public static readonly IReadOnlyList<string> SupportedPages = [RollCallPage, QuickDrawPage, LotteryPage];

    public required string Page { get; init; }

    public static bool TryParse(JsonElement? payload, out ControlSwitchPageRequest? request, out string? hint)
    {
        request = null;
        hint = null;

        var page = payload is { ValueKind: JsonValueKind.Object } element
            ? ControlPayloadReader.ReadString(element, "page")?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(page))
        {
            hint = $"page 必须是 {string.Join("/", SupportedPages)} 之一";
            return false;
        }

        if (!SupportedPages.Contains(page, StringComparer.Ordinal))
        {
            hint = $"page 必须是 {string.Join("/", SupportedPages)} 之一";
            return false;
        }

        request = new ControlSwitchPageRequest { Page = page };
        return true;
    }
}

/// <summary>载荷字段读取的小工具：同时容忍 camelCase / snake_case / PascalCase。</summary>
internal static class ControlPayloadReader
{
    public static string? ReadString(JsonElement element, string name)
    {
        if (TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();

        return null;
    }

    public static int? ReadInt(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    public static bool? ReadBool(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;

        foreach (var candidate in Alternatives(name))
        {
            if (element.TryGetProperty(candidate, out value))
                return true;
        }

        value = default;
        return false;
    }

    private static IEnumerable<string> Alternatives(string name)
    {
        // camelCase 名同时容忍 snake_case 与 PascalCase。
        yield return ToSnake(name);
        yield return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string ToSnake(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            var ch = name[index];
            if (char.IsUpper(ch) && index > 0)
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}