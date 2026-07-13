using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.Ipc;

public sealed record IpcRequestEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] IpcRequestPayload Payload);

public sealed record IpcRequestPayload(
    [property: JsonPropertyName("url")] string Url);

public sealed record IpcResponseEnvelope(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("result")] IpcBusinessResult? Result = null,
    [property: JsonPropertyName("error")] IpcError? Error = null)
{
    public static IpcResponseEnvelope TransportFailure(string type, string code, string message) =>
        new(false, type, Error: new IpcError(code, message));
}

public sealed record IpcBusinessResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("code")] string? Code = null,
    [property: JsonPropertyName("data")] object? Data = null);

public sealed record IpcError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);

public sealed record IpcRecordDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("gender")] string Gender);

public sealed record IpcHistoryRecordDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("prize")] string? Prize = null);

public sealed record IpcHistoryEntryDto(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("students")] IReadOnlyList<IpcHistoryRecordDto>? Students = null,
    [property: JsonPropertyName("winners")] IReadOnlyList<IpcHistoryRecordDto>? Winners = null);
