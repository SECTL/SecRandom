using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlCenter;

namespace SecRandom.Services.ControlCenter;

/// <summary>
///     v4 集控控制面（REST）客户端。
/// </summary>
/// <remarks>
///     <para>
///         所有请求都带 SECTL 账号的 Bearer，并且**只打官方站 <c>https://sectl.cn/control/api</c>**。
///         自建/私有部署的集控仍然可以用「地点 ID + 配对码」把桌面接入成被控节点，但控制台
///         不再把账号令牌发给任何可配置地址——那等于把账号交出去，也是旧客户端里既有的泄露。
///     </para>
///     <para>
///         这里没有轮询：v4 的命令端点是同步等待回执的（设备离线 409、超时 504），
///         所以一次调用就是一次结果。命令**绝不自动重试**，重试等于抽两次。
///     </para>
/// </remarks>
public interface IControlCenterClient
{
    /// <summary>当前账号、可管理的地点、平台能力表。</summary>
    Task<ControlCenterMe> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>地点下的年级（不含班级明细）。</summary>
    Task<IReadOnlyList<ControlCenterGrade>> GetGradesAsync(
        string sitePublicId,
        CancellationToken cancellationToken = default);

    /// <summary>年级下的班级。</summary>
    Task<IReadOnlyList<ControlCenterClass>> GetClassesAsync(
        string sitePublicId,
        string gradeId,
        CancellationToken cancellationToken = default);

    /// <summary>班级详情：设备、占班、抽取锁。</summary>
    Task<ControlCenterClassDetail> GetClassAsync(
        string classId,
        CancellationToken cancellationToken = default);

    /// <summary>地点下的全部设备（跨班级，用于展示"这台机器在哪"）。</summary>
    Task<IReadOnlyList<ControlCenterDevice>> GetDevicesAsync(
        string sitePublicId,
        CancellationToken cancellationToken = default);

    /// <summary>按班级下发一条命令，等待设备回执。</summary>
    Task<ControlCenterCommandResult> SendCommandAsync(
        string classId,
        string platform,
        string command,
        IReadOnlyDictionary<string, object?>? args = null,
        int? expiresInSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     列出该班占班设备上的点名名单 / 奖池名称（<c>roster.list</c>，operator 可读）。
    /// </summary>
    /// <remarks>
    ///     设备没有声明 <c>roster.list</c> 能力时会以 <c>capability_unsupported</c> 失败；
    ///     调用方应把它当作"这份设备不支持选名单"，回落到不选名单的抽取，而不是整页失败。
    /// </remarks>
    Task<ControlCenterRosterList> ListRostersAsync(
        string classId,
        CancellationToken cancellationToken = default);

    /// <summary>设置/清除班级的远程抽取锁。</summary>
    Task<ControlCenterDrawLock> SetDrawLockAsync(
        string classId,
        bool locked,
        string platform,
        CancellationToken cancellationToken = default);

    /// <summary>用当前账号把这台机器接入为地点下的设备（首次 201 / 接管 200）。</summary>
    /// <remarks>
    ///     <paramref name="deviceName" /> 留空时回落到本机主机名，与配对码通道一致
    ///     （见 <c>NodeEnrollmentClient.EnrollAsync</c>）：接入请求里的
    ///     <c>deviceName</c> 是必填非空字段，而 <c>ControlNodeDisplayName.Resolve</c>
    ///     在用户没填名字且取不到主机名时会返回 <c>null</c>。
    /// </remarks>
    Task<ControlCenterAccountEnrollment> EnrollAccountAsync(
        string sitePublicId,
        string? deviceName,
        string machineHost,
        string machineGuid,
        string? classId = null,
        CancellationToken cancellationToken = default);
}

public sealed class ControlCenterClient : IControlCenterClient
{
    /// <summary>
    ///     控制面基址。**固定常量**：账号令牌不走可配置地址（见类型注释）。
    /// </summary>
    public const string ApiBaseUrl = "https://sectl.cn/control/api";

    /// <summary>产品平台名。节点 hello 里报的操作系统平台在这里不适用。</summary>
    public const string Platform = "secrandom";

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IAuthorizedApiSender _sender;
    private readonly ILogger<ControlCenterClient> _logger;
    private readonly string _baseUrl;

    /// <param name="baseUrl">
    ///     仅测试与本地联调可覆盖；生产 DI 不传，永远用 <see cref="ApiBaseUrl" />。
    /// </param>
    public ControlCenterClient(
        IAuthorizedApiSender sender,
        ILogger<ControlCenterClient> logger,
        string? baseUrl = null)
    {
        _sender = sender;
        _logger = logger;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? ApiBaseUrl : baseUrl.TrimEnd('/');
    }

    public async Task<ControlCenterMe> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetRootAsync("/me", cancellationToken).ConfigureAwait(false);
        var sites = new List<ControlCenterSite>();
        foreach (var site in Enumerate(root, "sites"))
        {
            sites.Add(new ControlCenterSite(
                ReadString(site, "id") ?? string.Empty,
                ReadString(site, "publicId") ?? string.Empty,
                ReadString(site, "name") ?? string.Empty,
                ReadString(site, "role") ?? string.Empty,
                ReadInt(site, "gradeCount"),
                ReadInt(site, "classCount"),
                ReadInt(site, "deviceCount"),
                ReadInt(site, "onlineDeviceCount")));
        }

        var platforms = new List<ControlCenterPlatform>();
        foreach (var platform in Enumerate(root, "platforms"))
        {
            var commands = new List<ControlCenterCommand>();
            foreach (var command in Enumerate(platform, "commands"))
            {
                commands.Add(new ControlCenterCommand(
                    ReadString(command, "id") ?? string.Empty,
                    ReadString(command, "label") ?? string.Empty,
                    ReadBool(command, "needsConfirm")));
            }

            platforms.Add(new ControlCenterPlatform(
                ReadString(platform, "id") ?? string.Empty,
                ReadString(platform, "name") ?? string.Empty,
                ReadStringArray(platform, "capabilities"),
                commands));
        }

        return new ControlCenterMe(
            ReadString(root, "user", "id"),
            ReadString(root, "user", "name"),
            sites,
            platforms);
    }

    public async Task<IReadOnlyList<ControlCenterGrade>> GetGradesAsync(
        string sitePublicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePublicId);
        var root = await GetRootAsync($"/sites/{Escape(sitePublicId)}/grades", cancellationToken)
            .ConfigureAwait(false);

        var grades = new List<ControlCenterGrade>();
        foreach (var grade in Enumerate(root, "grades"))
        {
            grades.Add(new ControlCenterGrade(
                ReadString(grade, "id") ?? string.Empty,
                ReadString(grade, "name") ?? string.Empty,
                ReadInt(grade, "classCount"),
                ReadStringArray(grade, "floors")));
        }

        return grades;
    }

    public async Task<IReadOnlyList<ControlCenterClass>> GetClassesAsync(
        string sitePublicId,
        string gradeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePublicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gradeId);
        var root = await GetRootAsync(
                $"/sites/{Escape(sitePublicId)}/grades/{Escape(gradeId)}/classes",
                cancellationToken)
            .ConfigureAwait(false);

        var classes = new List<ControlCenterClass>();
        foreach (var classroom in Enumerate(root, "classes"))
        {
            ControlCenterOccupiedBy? occupiedBy = null;
            if (TryGetObject(classroom, "occupiedBy", out var occupied))
            {
                occupiedBy = new ControlCenterOccupiedBy(
                    ReadString(occupied, "deviceId") ?? string.Empty,
                    ReadString(occupied, "deviceName") ?? string.Empty);
            }

            classes.Add(new ControlCenterClass(
                ReadString(classroom, "id") ?? string.Empty,
                ReadString(classroom, "name") ?? string.Empty,
                ReadString(classroom, "gradeId") ?? gradeId,
                ReadString(classroom, "gradeName") ?? string.Empty,
                ReadString(classroom, "floor"),
                ReadInt(classroom, "deviceCount"),
                ReadStringArray(classroom, "projects"),
                occupiedBy));
        }

        return classes;
    }

    public async Task<ControlCenterClassDetail> GetClassAsync(
        string classId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classId);
        var root = await GetRootAsync($"/classes/{Escape(classId)}", cancellationToken).ConfigureAwait(false);

        var classroom = ReadObject(root, "class")
                        ?? throw Invalid("班级详情缺少 class。");

        var devices = new List<ControlCenterDevice>();
        foreach (var device in Enumerate(root, "devices"))
        {
            devices.Add(new ControlCenterDevice(
                ReadString(device, "id") ?? string.Empty,
                ReadString(device, "name") ?? string.Empty,
                ReadStringArray(device, "platforms"),
                ReadString(device, "classId"),
                ReadBool(device, "online"),
                ReadStringArray(device, "capabilities"),
                ReadDateTime(device, "lastSeenAt")));
        }

        var occupancy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (TryGetObject(root, "occupancy", out var occupancyElement))
        {
            foreach (var property in occupancyElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    occupancy[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        var occupiedBy = ReadObject(classroom, "occupiedBy");
        return new ControlCenterClassDetail(
            new ControlCenterClass(
                ReadString(classroom, "id") ?? classId,
                ReadString(classroom, "name") ?? string.Empty,
                ReadString(classroom, "gradeId") ?? string.Empty,
                ReadString(classroom, "gradeName") ?? string.Empty,
                ReadString(classroom, "floor"),
                ReadInt(classroom, "deviceCount"),
                ReadStringArray(classroom, "projects"),
                occupiedBy is { } value
                    ? new ControlCenterOccupiedBy(
                        ReadString(value, "deviceId") ?? string.Empty,
                        ReadString(value, "deviceName") ?? string.Empty)
                    : null),
            new ControlCenterSiteRef(
                ReadString(root, "site", "publicId") ?? string.Empty,
                ReadString(root, "site", "name") ?? string.Empty),
            devices,
            occupancy,
            ReadStringArray(root, "onlineDeviceIds"),
            ReadBool(root, "drawLocked"));
    }

    public async Task<IReadOnlyList<ControlCenterDevice>> GetDevicesAsync(
        string sitePublicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePublicId);
        var root = await GetRootAsync($"/sites/{Escape(sitePublicId)}/devices", cancellationToken)
            .ConfigureAwait(false);

        var devices = new List<ControlCenterDevice>();
        foreach (var device in Enumerate(root, "devices"))
        {
            devices.Add(new ControlCenterDevice(
                ReadString(device, "id") ?? string.Empty,
                ReadString(device, "name") ?? string.Empty,
                ReadStringArray(device, "platforms"),
                ReadString(device, "classId"),
                ReadBool(device, "online"),
                ReadStringArray(device, "capabilities"),
                ReadDateTime(device, "lastSeenAt")));
        }

        return devices;
    }

    public async Task<ControlCenterCommandResult> SendCommandAsync(
        string classId,
        string platform,
        string command,
        IReadOnlyDictionary<string, object?>? args = null,
        int? expiresInSeconds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classId);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["platform"] = platform,
            ["cmd"] = command,
            ["args"] = args is { Count: > 0 } ? args : new Dictionary<string, object?>()
        };
        if (expiresInSeconds is > 0)
            payload["expires_in_seconds"] = expiresInSeconds.Value;

        var data = await SendForDataAsync(
                HttpMethod.Post,
                $"/classes/{Escape(classId)}/command",
                payload,
                cancellationToken)
            .ConfigureAwait(false);

        JsonElement? resultPayload = null;
        if (data.TryGetProperty("payload", out var payloadElement))
            resultPayload = payloadElement.Clone();

        return new ControlCenterCommandResult(
            ReadBool(data, "ok"),
            resultPayload,
            ReadDouble(data, "elapsed"),
            ReadString(data, "ack"),
            ReadString(data, "commandId"));
    }

    public async Task<ControlCenterRosterList> ListRostersAsync(
        string classId,
        CancellationToken cancellationToken = default)
    {
        var result = await SendCommandAsync(
                classId,
                Platform,
                "roster.list",
                null,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Ok || result.Payload is not { ValueKind: JsonValueKind.Object } payload)
        {
            throw new ControlPlaneException(
                "roster_list_failed",
                ControlPlaneErrorKind.InvalidResponse,
                message: result.Ack ?? "设备没有返回名单名称");
        }

        return new ControlCenterRosterList(
            ReadStringArray(payload, "roll_call"),
            ReadStringArray(payload, "lottery"),
            ReadString(payload, "default_roll_call"),
            ReadString(payload, "default_lottery"));
    }

    public async Task<ControlCenterDrawLock> SetDrawLockAsync(
        string classId,
        bool locked,
        string platform,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classId);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);

        var data = await SendForDataAsync(
                HttpMethod.Post,
                $"/classes/{Escape(classId)}/draw-lock",
                new Dictionary<string, object?>
                {
                    ["locked"] = locked,
                    ["platform"] = platform
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new ControlCenterDrawLock(
            ReadBool(data, "locked"),
            ReadString(data, "platform") ?? platform,
            ReadBool(data, "pushed"),
            ReadLong(data, "revision"));
    }

    public async Task<ControlCenterAccountEnrollment> EnrollAccountAsync(
        string sitePublicId,
        string? deviceName,
        string machineHost,
        string machineGuid,
        string? classId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePublicId);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineGuid);

        var resolvedDeviceName = string.IsNullOrWhiteSpace(deviceName)
            ? Environment.MachineName
            : deviceName.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedDeviceName);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["siteId"] = sitePublicId,
            ["platform"] = Platform,
            ["deviceName"] = resolvedDeviceName,
            ["machine"] = new Dictionary<string, object?>
            {
                ["host"] = machineHost,
                ["guid"] = machineGuid
            }
        };
        if (!string.IsNullOrWhiteSpace(classId))
            payload["classId"] = classId;

        var data = await SendForDataAsync(HttpMethod.Post, "/device/enroll", payload, cancellationToken)
            .ConfigureAwait(false);

        var deviceId = ReadString(data, "deviceId");
        var secret = ReadString(data, "secret");
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(secret))
            throw Invalid("账号接入响应缺少 deviceId/secret。");

        return new ControlCenterAccountEnrollment(
            deviceId,
            secret,
            ReadString(data, "site", "publicId") ?? sitePublicId,
            ReadString(data, "site", "name") ?? string.Empty,
            ReadString(data, "class", "id"),
            ReadString(data, "class", "name"),
            ReadInt(data, "heartbeatSeconds"),
            ReadInt(data, "offlineAfterSeconds"));
    }

    private async Task<JsonElement> GetRootAsync(string path, CancellationToken cancellationToken)
    {
        var data = await SendForDataAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        return data;
    }

    /// <summary>
    ///     发一次请求并返回统一信封里的 <c>data</c>（没有 data 时返回整个成功体）。
    /// </summary>
    private async Task<JsonElement> SendForDataAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var uri = _baseUrl + path;
        HttpResponseMessage response;
        try
        {
            response = await _sender.SendAuthorizedAsync(
                    () =>
                    {
                        var request = new HttpRequestMessage(method, uri);
                        if (body is not null)
                            request.Content = JsonContent.Create(body, options: RequestJsonOptions);
                        return request;
                    },
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            // 授权边界的既有契约：没有登录时抛的就是这个异常。
            throw new ControlPlaneException(
                "not_signed_in",
                ControlPlaneErrorKind.Unauthorized,
                message: exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new ControlPlaneException(
                "network_error",
                ControlPlaneErrorKind.Network,
                message: exception.Message);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var requestUri = response.RequestMessage?.RequestUri?.ToString();

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            }
            catch (JsonException exception)
            {
                if (response.IsSuccessStatusCode)
                {
                    throw Invalid($"控制面响应无法解析：{exception.Message}");
                }

                throw CreateException(response.StatusCode, null, null, requestUri);
            }

            using (document)
            {
                var root = document.RootElement;
                if (!response.IsSuccessStatusCode || IsFailureEnvelope(root))
                {
                    var code = ReadString(root, "code");
                    var message = ReadString(root, "message") ?? ReadString(root, "error_description");
                    JsonElement? detail = null;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("detail", out var detailElement))
                        detail = detailElement.Clone();

                    var exception = CreateException(response.StatusCode, code, message, requestUri);
                    if (detail is not null)
                    {
                        exception = new ControlPlaneException(
                            exception.Code,
                            exception.Kind,
                            exception.StatusCode,
                            detail,
                            exception.Message,
                            exception.RequestUri);
                    }

                    _logger.LogWarning(
                        "控制面请求失败：状态={Status}，URL={Url}，错误码={Code}，分类={Kind}",
                        (int)response.StatusCode,
                        requestUri ?? "unknown",
                        exception.Code,
                        exception.Kind);
                    throw exception;
                }

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("data", out var data) &&
                    data.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    return data.Clone();

                return root.Clone();
            }
        }
    }

    private static bool IsFailureEnvelope(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("ok", out var ok) &&
        ok.ValueKind is JsonValueKind.False;

    private static ControlPlaneException CreateException(
        HttpStatusCode statusCode,
        string? code,
        string? message,
        string? requestUri) =>
        new(
            string.IsNullOrWhiteSpace(code) ? ControlPlaneException.FallbackCode(statusCode) : code,
            ControlPlaneException.ClassifyStatus(statusCode),
            (int)statusCode,
            message: message ?? code ?? ControlPlaneException.FallbackCode(statusCode),
            requestUri: requestUri);

    private static ControlPlaneException Invalid(string message) =>
        new("invalid_response", ControlPlaneErrorKind.InvalidResponse, message: message);

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static IEnumerable<JsonElement> Enumerate(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in array.EnumerateArray())
            yield return item;
    }

    private static JsonElement? ReadObject(JsonElement parent, string name) =>
        TryGetObject(parent, name, out var value) ? value : null;

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Object)
            return false;

        value = element;
        return true;
    }

    private static string? ReadString(JsonElement parent, params string[] path)
    {
        if (!TryResolve(parent, path, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool ReadBool(JsonElement parent, params string[] path) =>
        TryResolve(parent, path, out var value) && value.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement parent, params string[] path) =>
        TryResolve(parent, path, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static long ReadLong(JsonElement parent, params string[] path) =>
        TryResolve(parent, path, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private static double? ReadDouble(JsonElement parent, params string[] path) =>
        TryResolve(parent, path, out var value) && value.TryGetDouble(out var result) ? result : null;

    private static DateTimeOffset? ReadDateTime(JsonElement parent, params string[] path) =>
        TryResolve(parent, path, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.TryGetDateTimeOffset(out var result)
            ? result
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, params string[] path)
    {
        if (!TryResolve(parent, path, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                item.GetString() is { Length: > 0 } text)
                items.Add(text);
        }

        return items;
    }

    private static bool TryResolve(JsonElement parent, string[] path, out JsonElement value)
    {
        var current = parent;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(name, out current))
            {
                value = default;
                return false;
            }
        }

        value = current;
        return true;
    }
}