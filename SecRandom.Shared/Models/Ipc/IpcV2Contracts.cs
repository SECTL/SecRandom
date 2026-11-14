using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.Ipc;

/// <summary>
/// v2 automation envelope. One line, one request, one response; the control pipe stays
/// short-lived so instance forwarding keeps its own connection budget.
/// </summary>
public sealed record IpcV2RequestEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("params")] JsonElement? Params)
{
    public const int CurrentVersion = 2;

    public static bool TryRead(
        string line,
        out IpcV2RequestEnvelope? request,
        out IpcV2ErrorEnvelope? error)
    {
        request = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = IpcV2ErrorEnvelope.Create(
                    IpcV2ErrorCodes.InvalidRequest, "请求必须是 JSON 对象。");
                return false;
            }

            var version = root.TryGetProperty("version", out var versionElement) &&
                          versionElement.TryGetInt32(out var parsedVersion)
                ? parsedVersion
                : 0;
            if (version != CurrentVersion)
            {
                error = IpcV2ErrorEnvelope.Create(
                    IpcV2ErrorCodes.UnsupportedVersion,
                    $"不支持的协议版本: {version}。",
                    retryable: false,
                    details: new { supported = new[] { 1, CurrentVersion } });
                return false;
            }

            var type = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
            if (!string.Equals(type, "rpc", StringComparison.Ordinal) &&
                !string.Equals(type, "hello", StringComparison.Ordinal))
            {
                error = IpcV2ErrorEnvelope.Create(
                    IpcV2ErrorCodes.InvalidRequest, "type 必须是 rpc 或 hello。");
                return false;
            }

            var id = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            var method = root.TryGetProperty("method", out var methodElement) &&
                         methodElement.ValueKind == JsonValueKind.String
                ? methodElement.GetString()
                : null;
            if (string.Equals(type, "rpc", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(method))
            {
                error = IpcV2ErrorEnvelope.Create(
                    IpcV2ErrorCodes.MissingParameter, "缺少 method。");
                return false;
            }

            JsonElement? parameters = null;
            if (root.TryGetProperty("params", out var paramsElement))
            {
                parameters = paramsElement.Clone();
                if (paramsElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                {
                    error = IpcV2ErrorEnvelope.Create(
                        IpcV2ErrorCodes.InvalidParameter, "params 必须是对象。");
                    return false;
                }

                if (paramsElement.ValueKind == JsonValueKind.Null)
                    parameters = null;
            }

            request = new IpcV2RequestEnvelope(CurrentVersion, type!, id, method, parameters);
            return true;
        }
        catch (JsonException)
        {
            error = IpcV2ErrorEnvelope.Create(
                IpcV2ErrorCodes.InvalidRequest, "请求不是合法 JSON。");
            return false;
        }
    }
}

/// <summary>
///     Stable v2 error codes carried by <see cref="IpcV2ErrorEnvelope"/>. Shared keeps its own copy because the
///     Core catalog (<c>AutomationErrorCodes</c>) is not visible from this project; keep both value sets identical.
/// </summary>
public static class IpcV2ErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedVersion = "unsupported_version";
    public const string MissingParameter = "missing_parameter";
    public const string InvalidParameter = "invalid_parameter";
}

public sealed record IpcV2ResponseEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] IpcV2ErrorEnvelope? Error = null)
{
    public static IpcV2ResponseEnvelope Success(string? id, object? result) =>
        new(IpcV2RequestEnvelope.CurrentVersion, "rpc", id, true, result);

    public static IpcV2ResponseEnvelope Failure(string? id, IpcV2ErrorEnvelope error) =>
        new(IpcV2RequestEnvelope.CurrentVersion, "rpc", id, false, Error: error);
}

public sealed record IpcV2ErrorEnvelope(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable = false,
    [property: JsonPropertyName("details")] object? Details = null)
{
    public static IpcV2ErrorEnvelope Create(
        string code,
        string message,
        bool retryable = false,
        object? details = null) =>
        new(code, message, retryable, details);
}

public sealed record IpcV2EventEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("data")] object? Data);

public sealed record IpcV2EventPayload(
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("data")] object? Data)
{
    public static IpcV2EventPayload Create(long seq, string name, object? data) =>
        new(seq, name, data);
}

public sealed record IpcV2HelloEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("client")] IpcV2ClientInfo? Client,
    [property: JsonPropertyName("protocolVersions")] int[]? ProtocolVersions);

public sealed record IpcV2ClientInfo(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("version")] string? Version);

public sealed record IpcV2WelcomeEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("serverVersion")] string ServerVersion,
    [property: JsonPropertyName("methodsDigest")] string MethodsDigest);