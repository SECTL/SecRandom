using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Services.Integration;

internal static class IntegrationProtocol
{
    public const int Version = 1;
    public const int MaxFrameBytes = 256 * 1024;
    public const string Name = "secrandom.integration.v1";
}

internal sealed record IntegrationManifest(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("sdk_version")] string SdkVersion,
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("pid")] int ProcessId,
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("publisher_proof")] string? PublisherProof);

internal sealed record OfficialSdkProof(
    [property: JsonPropertyName("package_id")] string PackageId,
    [property: JsonPropertyName("sdk_version")] string SdkVersion,
    [property: JsonPropertyName("assembly_sha256")] string AssemblySha256,
    [property: JsonPropertyName("signature")] string Signature);

internal sealed record IntegrationFrame(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("params")] JsonElement? Params,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] IntegrationError? Error = null);

internal sealed record IntegrationError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable = false);

internal sealed record LinkageLeaseRequest(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("lease_id")] string LeaseId);

internal static class IntegrationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record IntegrationChannel(
    string AppId,
    string DisplayName,
    string SdkVersion,
    string Endpoint,
    int ProcessId,
    string InstanceId,
    DateTimeOffset ApprovedAt,
    DateTimeOffset LastSeen);

