using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Auth;

public sealed record SectlCloudFile(string FileId, string? FileDocumentId, string FileName, long Size, DateTimeOffset? CreatedAt);

public sealed record SectlCloudQuota(long Used, long Total, long Available, int FileCount, long PlatformUsed, int PlatformFileCount);

/// <summary>
///     Cloud API failure with the service error code kept intact so the UI can localize
///     "signed out", "cloud disabled", "quota exceeded", and transport failures differently.
/// </summary>
public sealed class SectlCloudStorageException(string code, string description) : Exception(description)
{
    public string Code { get; } = code;
}

/// <summary>
///     Typed client for the SECTL personal cloud storage API (<c>/api/cloud/*</c>, Appwrite cloud
///     function). Requests carry the signed-in OAuth bearer token plus the SecRandom platform
///     <c>client_id</c>, so the account, the platform scope, and the quota are all resolved
///     server-side. Only the account cloud sync path uses this client; the anonymous device-transfer
///     channel stays on SecRandom Sync.
/// </summary>
public sealed class SectlCloudStorageClient(
    SectlAuthService authService,
    IHttpClientFactory httpClientFactory,
    ILogger<SectlCloudStorageClient> logger)
{
    private const string CloudPath = "api/cloud";
    private const string HttpClientName = "sectl-cloud";
    private const int PageSize = 500;

    /// <summary>One upload part never exceeds this size; release parts stay far below it.</summary>
    private const long MaxUploadBytes = 4L * 1024 * 1024;

    private const long MaxDownloadBytes = 8L * 1024 * 1024;

    /// <summary>
    ///     Server responses slower than this are treated as an execution timeout: the SECTL cloud
    ///     function spends that long scanning per-user storage usage before it answers, and the
    ///     gateway answers the client with a bare 5xx while the execution keeps running.
    /// </summary>
    private static readonly TimeSpan SlowServerThreshold = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SectlCloudFile>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        var files = new List<SectlCloudFile>();
        for (var offset = 0; ; offset += PageSize)
        {
            var body = await SendForBodyAsync(
                () => new HttpRequestMessage(HttpMethod.Get, BuildUri("files", $"limit={PageSize}&offset={offset}")),
                cancellationToken).ConfigureAwait(false);
            CloudFileListResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<CloudFileListResponse>(body, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new SectlCloudStorageException("invalid_response", $"云端文件列表无法解析：{exception.Message}");
            }

            if (payload?.Files is null)
                return files;

            foreach (var file in payload.Files)
            {
                if (string.IsNullOrWhiteSpace(file.FileId) || string.IsNullOrWhiteSpace(file.FileName))
                    continue;
                files.Add(new SectlCloudFile(file.FileId!, file.FileDocumentId, file.FileName!,
                    file.Size ?? 0, ParseTimestamp(file.CreatedAt)));
            }

            if (!payload.HasMore || payload.Files.Count == 0)
                return files;
        }
    }

    public async Task<SectlCloudFile> UploadAsync(string fileName, string mimeType, byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        if (content.LongLength > MaxUploadBytes)
            throw new SectlCloudStorageException("payload_too_large", "云端备份分片体积超过上传限制。");

        var payload = new
        {
            client_id = SectlAuthService.ClientId,
            file = new
            {
                name = fileName,
                type = mimeType,
                size = content.LongLength,
                data = $"data:{mimeType};base64,{Convert.ToBase64String(content)}"
            }
        };
        var body = await SendForBodyAsync(
            () => new HttpRequestMessage(HttpMethod.Post, BuildUri("upload"))
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            },
            cancellationToken).ConfigureAwait(false);
        CloudUploadResponse? uploaded;
        try
        {
            uploaded = JsonSerializer.Deserialize<CloudUploadResponse>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SectlCloudStorageException("upload_failed", $"云端上传结果无法解析：{exception.Message}");
        }

        if (uploaded?.Success != true || string.IsNullOrWhiteSpace(uploaded.FileId))
            throw new SectlCloudStorageException("upload_failed", "云端未确认分片上传结果。");

        return new SectlCloudFile(uploaded.FileId!, null, uploaded.FileName ?? fileName,
            uploaded.Size ?? content.LongLength, DateTimeOffset.UtcNow);
    }

    public async Task<byte[]> DownloadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        var body = await SendForBodyAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri($"files/{Uri.EscapeDataString(fileId)}/download")),
            cancellationToken).ConfigureAwait(false);
        CloudDownloadResponse? access;
        try
        {
            access = JsonSerializer.Deserialize<CloudDownloadResponse>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SectlCloudStorageException("download_failed", $"云端下载地址无法解析：{exception.Message}");
        }

        if (string.IsNullOrWhiteSpace(access?.DownloadUrl))
            throw new SectlCloudStorageException("download_failed", "云端未返回下载地址。");

        using var request = new HttpRequestMessage(HttpMethod.Get, access.DownloadUrl);
        // A factory-provided client is shared; dispose only the response, never the client.
        var client = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage download = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!download.IsSuccessStatusCode)
            throw CreateException(download.StatusCode, string.Empty);

        await using var stream = await download.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaxDownloadBytes)
                throw new SectlCloudStorageException("payload_too_large", "云端备份分片体积超过下载限制。");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    public async Task DeleteAsync(string fileId, string? fileDocumentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        var payload = new Dictionary<string, object?> { ["client_id"] = SectlAuthService.ClientId };
        if (!string.IsNullOrWhiteSpace(fileDocumentId))
            payload["file_document_id"] = fileDocumentId;

        await SendForBodyAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, BuildUri($"files/{Uri.EscapeDataString(fileId)}"))
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<SectlCloudQuota> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var body = await SendForBodyAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri("storage/usage")),
            cancellationToken).ConfigureAwait(false);
        CloudUsageResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CloudUsageResponse>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new SectlCloudStorageException("invalid_response", $"云端用量无法解析：{exception.Message}");
        }

        if (payload is null)
            throw new SectlCloudStorageException("invalid_response", "云端未返回用量信息。");
        return new SectlCloudQuota(payload.UsedStorage, payload.TotalStorage, payload.AvailableStorage,
            payload.FileCount, payload.PlatformUsedStorage, payload.PlatformFileCount);
    }

    private static string BuildUri(string path, string? extraQuery = null)
    {
        var uri = $"{SectlAuthService.ApiBaseUrl}/{CloudPath}/{path}?client_id={Uri.EscapeDataString(SectlAuthService.ClientId)}";
        return string.IsNullOrWhiteSpace(extraQuery) ? uri : $"{uri}&{extraQuery}";
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;

    /// <summary>
    ///     Sends one cloud API request and returns its success body, keeping the elapsed time so a
    ///     failing gateway response can be classified as a server-side execution timeout.
    /// </summary>
    private async Task<string> SendForBodyAsync(Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await authService.SendAuthorizedAsync(createRequest,
            HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        if (response.IsSuccessStatusCode)
            return body;

        var exception = CreateException(response.StatusCode, body, stopwatch.Elapsed);
        logger.LogWarning(
            "云端接口请求失败：状态={Status}，耗时={ElapsedMs}ms，错误码={Code}，响应={Body}",
            (int)response.StatusCode, stopwatch.ElapsedMilliseconds, exception.Code, Truncate(body, 300));
        throw exception;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength] + "…";

    private static SectlCloudStorageException CreateException(HttpStatusCode statusCode, string body,
        TimeSpan? elapsed = null)
    {
        string? functionCode = null;
        string? description = null;
        string? appwriteCode = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                        functionCode = error.GetString();
                    if (document.RootElement.TryGetProperty("error_description", out var detail) && detail.ValueKind == JsonValueKind.String)
                        description = detail.GetString();
                    if (document.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                        appwriteCode = type.GetString();
                    if (description is null
                        && document.RootElement.TryGetProperty("message", out var message)
                        && message.ValueKind == JsonValueKind.String)
                        description = message.GetString();
                }
            }
            catch (JsonException)
            {
                // A non-JSON error body still maps through the status code below.
            }
        }

        var isServerError = (int)statusCode >= 500;
        var slow = elapsed is { } duration && duration >= SlowServerThreshold;
        if (isServerError && functionCode is null)
        {
            // The SECTL cloud function answers its own failures with an `error` envelope. A bare or
            // non-function 5xx is the gateway giving up while the execution keeps running, which is
            // the shape a slow per-user storage usage scan produces today.
            var reason = string.IsNullOrWhiteSpace(description)
                ? "服务端未返回错误详情（空响应）"
                : description!;
            return new SectlCloudStorageException("cloud_timeout",
                $"云端服务响应超时或不可用（HTTP {(int)statusCode}，耗时 {(elapsed?.TotalSeconds ?? 0):0.#}s）：{reason}");
        }

        if (isServerError && slow)
        {
            return new SectlCloudStorageException("cloud_timeout",
                $"云端服务响应超时（HTTP {(int)statusCode}，耗时 {elapsed!.Value.TotalSeconds:0.#}s）：{description ?? functionCode}");
        }

        var code = functionCode ?? appwriteCode ??
                   (statusCode == HttpStatusCode.RequestEntityTooLarge ? "payload_too_large" : $"http_{(int)statusCode}");
        return new SectlCloudStorageException(code,
            string.IsNullOrWhiteSpace(description) ? $"云端请求失败（{(int)statusCode}）。" : description!);
    }

    private sealed record CloudFileListResponse(
        [property: JsonPropertyName("files")] List<CloudFilePayload>? Files,
        [property: JsonPropertyName("has_more")] bool HasMore);

    private sealed record CloudFilePayload(
        [property: JsonPropertyName("file_id")] string? FileId,
        [property: JsonPropertyName("file_document_id")] string? FileDocumentId,
        [property: JsonPropertyName("filename")] string? FileName,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("created_at")] string? CreatedAt);

    private sealed record CloudUploadResponse(
        [property: JsonPropertyName("success")] bool? Success,
        [property: JsonPropertyName("file_id")] string? FileId,
        [property: JsonPropertyName("filename")] string? FileName,
        [property: JsonPropertyName("size")] long? Size);

    private sealed record CloudDownloadResponse(
        [property: JsonPropertyName("download_url")] string? DownloadUrl,
        [property: JsonPropertyName("expires_at")] string? ExpiresAt);

    private sealed record CloudUsageResponse(
        [property: JsonPropertyName("used_storage")] long UsedStorage,
        [property: JsonPropertyName("total_storage")] long TotalStorage,
        [property: JsonPropertyName("available_storage")] long AvailableStorage,
        [property: JsonPropertyName("file_count")] int FileCount,
        [property: JsonPropertyName("platform_used_storage")] long PlatformUsedStorage,
        [property: JsonPropertyName("platform_file_count")] int PlatformFileCount);
}
