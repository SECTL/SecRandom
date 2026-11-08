using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     接入失败的原因，直接对应服务端 <c>{"code":"…"}</c> 的那几个取值。
/// </summary>
/// <remarks>
///     之所以不把服务端的错误码当字符串往上抛：设置页要按原因给**不同的下一步**
///     （地点 ID 写错了 ⇒ 重填；配对码不对 ⇒ 重填；这个码不属于该地点 ⇒ 检查两栏；
///     过期/已用过/已撤销/平台不允许 ⇒ 找管理员换码；这台机器已接在别的地点 ⇒ 先去那边解绑），
///     用字符串 switch 迟早会漏掉一个分支。
/// </remarks>
public enum NodeEnrollmentFailure
{
    /// <summary>没有拿到可识别的服务端原因（网络、超时、5xx、响应看不懂）。</summary>
    Unknown,

    /// <summary>请求本身不合法（HTTP 400 <c>bad_request</c>）。</summary>
    InvalidRequest,

    /// <summary>地点 ID 不存在（HTTP 404 <c>site_not_found</c>）。</summary>
    SiteNotFound,

    /// <summary>配对码不存在（HTTP 404 <c>code_not_found</c>）。</summary>
    CodeNotFound,

    /// <summary>配对码不属于这个地点（HTTP 403 <c>code_site_mismatch</c>）。</summary>
    CodeSiteMismatch,

    /// <summary>配对码已过期（HTTP 410 <c>code_expired</c>）。</summary>
    CodeExpired,

    /// <summary>配对码已经被用掉了（HTTP 410 <c>code_used</c>）。</summary>
    CodeUsed,

    /// <summary>配对码已被管理员撤销（HTTP 410 <c>code_revoked</c>）。</summary>
    CodeRevoked,

    /// <summary>这个码不允许接入 SecRandom（HTTP 403 <c>platform_not_allowed</c>）。</summary>
    PlatformNotAllowed,

    /// <summary>这台机器已经接在另一个地点（HTTP 409 <c>device_conflict</c>）。</summary>
    DeviceConflict,

    /// <summary>尝试次数过多（HTTP 429 <c>too_many_requests</c>），需要等一会儿再试。</summary>
    TooManyAttempts,

    /// <summary>v1 兼容：旧的接入码不存在（v1 已断代，不会再产生）。</summary>
    CodeInvalid,

    /// <summary>v1 兼容：服务端没有开放接入。</summary>
    Disabled,

    /// <summary>请求没能到达服务端（DNS/连接/TLS/超时）。**输入本身可能还是好的**，值得重试。</summary>
    Network,

    /// <summary>服务端答应得不合契约（缺字段、secret 为空）。</summary>
    InvalidResponse
}

/// <summary>接入被服务端拒绝，或请求没能完成。</summary>
/// <remarks>
///     消息里**只有服务端给的 <c>code</c> 与 HTTP 状态**，绝不含地点 ID、配对码或 secret：
///     异常会被日志与诊断包记下来，任何用户输入都不该借它落进日志。
/// </remarks>
public sealed class NodeEnrollmentException : Exception
{
    public NodeEnrollmentException(
        NodeEnrollmentFailure failure,
        string? serverCode = null,
        int? statusCode = null,
        string? message = null,
        string? conflictSiteName = null)
        : base(message ?? serverCode ?? failure.ToString())
    {
        Failure = failure;
        ServerCode = serverCode;
        StatusCode = statusCode;
        ConflictSiteName = conflictSiteName;
    }

    public NodeEnrollmentFailure Failure { get; }

    /// <summary>服务端原样给的错误码（如有）。</summary>
    public string? ServerCode { get; }

    public int? StatusCode { get; }

    /// <summary><c>device_conflict</c> 时服务端告诉你的"已经接在哪个地点"。</summary>
    public string? ConflictSiteName { get; }
}

/// <summary>
///     集控接入、设备树与分班的 HTTP 客户端（v4.1）。
/// </summary>
/// <remarks>
///     <para>
///         <b>接入是匿名的</b>，因此它**不经过** <see cref="IAuthorizedApiSender" />：
///         这一步恰恰是"这台设备还没有凭据"的时候。用匿名 <see cref="HttpClient" /> 直发，
///         顺带保证这个请求永远不会把 SECTL 账号令牌送到自建服务器上。
///     </para>
///     <para>
///         接入成功后服务端只回一次 <c>secret</c>；它由 <see cref="INodeEnrollmentStore" /> 加密落盘，
///         之后连节点通道时放进第一帧 <c>hello</c> 的载荷里——v4 不再使用 HTTP <c>Authorization</c> 头。
///     </para>
///     <para>
///         设备树与分班请求的凭据放在**请求体**里（<c>deviceId</c> + <c>secret</c>），
///         同样不是 Bearer：这样服务端日志、代理与 URL 里都不会出现密钥。
///     </para>
/// </remarks>
public sealed class NodeEnrollmentClient
{
    /// <summary>v1 节点令牌前缀。v4 不再产生，保留常量仅为兼容既有调用。</summary>
    public const string TokenPrefix = "srn_";

    /// <summary>v4 接入请求路径。</summary>
    public const string EnrollPath = "/control/api/enroll";

    /// <summary>设备树（年级/班级结构 + 本机当前分班）。</summary>
    public const string DeviceTreePath = "/control/api/device/tree";

    /// <summary>保存本机分班。</summary>
    public const string DeviceAssignPath = "/control/api/device/assign";

    /// <summary>v1 接入路径（已断代，仅保留常量给旧测试/文档引用）。</summary>
    public const string LegacyEnrollPath = "/v1/node/enroll";

    /// <summary>
    ///     输入框长度上限（**界面护栏，不是协议约束**）：只用来挡住"把整段 JSON 误粘进来"。
    /// </summary>
    public const int MaxCodeLength = 128;

    /// <summary>地点 ID 规范形态：<c>SC-</c> + 6 位大写字母数字。</summary>
    public const int SiteIdBodyLength = 6;

    /// <summary>配对码规范形态：8 位大写字母数字（展示为 <c>ABCD-7KQ2</c>）。</summary>
    public const int PairingCodeLength = 8;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        // 接入/设备树/分班三个端点的请求体是 camelCase（见 control-center-protocol-v4.md §2.10）。
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IControlPlaneEndpointStore _endpointStore;
    private readonly IControlNodeStateStore? _stateStore;
    private readonly string? _fixedBaseUrl;

    public NodeEnrollmentClient(
        IHttpClientFactory httpClientFactory,
        IControlPlaneEndpointStore endpointStore,
        string? baseUrl = null,
        IControlNodeStateStore? stateStore = null)
    {
        _httpClientFactory = httpClientFactory;
        _endpointStore = endpointStore;
        _fixedBaseUrl = baseUrl;
        _stateStore = stateStore;
    }

    /// <summary>当前生效的集控基址（自建时可指向内网）。</summary>
    public string BaseUrl => string.IsNullOrWhiteSpace(_fixedBaseUrl) ? _endpointStore.Current : _fixedBaseUrl;

    /// <summary>
    ///     把 地点 ID + 配对码 换成设备凭据。成功后 <paramref name="record" /> 里含
    ///     <c>DeviceId</c>/<c>Secret</c>/<c>SiteId</c>/<c>SiteName</c>/<c>ClassId</c>。
    /// </summary>
    public async Task<NodeEnrollmentRecord> EnrollAsync(
        string siteId,
        string pairingCode,
        string? deviceName,
        CancellationToken cancellationToken = default)
    {
        var normalizedSiteId = NormalizeSiteId(siteId);
        var normalizedCode = NormalizePairingCode(pairingCode);

        if (normalizedSiteId is null)
            throw new NodeEnrollmentException(NodeEnrollmentFailure.InvalidRequest,
                message: "地点 ID 格式不正确");

        if (normalizedCode is null)
            throw new NodeEnrollmentException(NodeEnrollmentFailure.InvalidRequest,
                message: "配对码格式不正确");

        var request = new EnrollRequest
        {
            SiteId = normalizedSiteId,
            Code = normalizedCode,
            Platform = ControlV4Protocol.Platform,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? Environment.MachineName : deviceName.Trim(),
            Machine = new MachineInfo
            {
                Host = Environment.MachineName,
                Guid = ResolveMachineGuid()
            }
        };

        var uri = BuildUri(BaseUrl, EnrollPath);
        using var content = JsonContent.Create(request, options: RequestJsonOptions);
        var response = await SendAsync(uri, content, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Created && !response.IsSuccessStatusCode)
        {
            var error = await ReadEnrollErrorAsync(response, cancellationToken).ConfigureAwait(false);
            throw error;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // 线上接入端点回的是统一信封 { ok: true, data: { deviceId, secret, site, class, ... } }；
        // 早期参考实现按「字段铺平在顶层」写的，这里两种都认：先看 data，再退回顶层。
        // 不认信封的后果不是报错，是「接入成功却拿不到凭据」，配对码被消耗掉、人还得再要一张。
        var root = document.RootElement;
        if (!root.TryGetProperty("deviceId", out _) && root.TryGetProperty("data", out var wrapped) &&
            wrapped.ValueKind == JsonValueKind.Object)
            root = wrapped;

        var deviceId = ReadString(root, "deviceId");
        var secret = ReadString(root, "secret");
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(secret))
            throw new NodeEnrollmentException(NodeEnrollmentFailure.InvalidResponse,
                message: "接入响应缺少 deviceId/secret");

        var siteName = ReadString(root, "site", "name");
        var classId = ReadString(root, "class", "id");
        var className = ReadString(root, "class", "name");

        return new NodeEnrollmentRecord
        {
            DeviceId = deviceId,
            Secret = secret,
            SiteId = ReadString(root, "siteId") ?? normalizedSiteId,
            SiteName = siteName,
            ClassId = classId,
            ClassName = className,
            EnrolledAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    ///     拉取本设备所在站点的年级/班级结构，以及本机当前分班。
    /// </summary>
    /// <remarks>
    ///     设置页会把整棵树缓存起来：用户切"年级"时只过滤本地列表，不再打一次请求。
    /// </remarks>
    public async Task<ControlDeviceTree> GetDeviceTreeAsync(
        NodeDeviceCredential credential,
        CancellationToken cancellationToken = default)
    {
        var request = new DeviceCredentialRequest
        {
            DeviceId = credential.DeviceId,
            Secret = credential.Secret
        };

        var uri = BuildUri(BaseUrl, DeviceTreePath);
        using var content = JsonContent.Create(request, options: RequestJsonOptions);
        var response = await SendAsync(uri, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadDeviceErrorAsync(response, cancellationToken).ConfigureAwait(false);
            throw error;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var tree = await JsonSerializer
            .DeserializeAsync<DeviceTreeResponse>(stream, ResponseJsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (tree is null)
            throw new NodeEnrollmentException(NodeEnrollmentFailure.InvalidResponse,
                message: "设备树响应无法解析");

        return new ControlDeviceTree
        {
            SiteId = tree.Site?.PublicId,
            SiteName = tree.Site?.Name,
            Grades = tree.Grades?.Select(grade => new ControlGradeNode(
                    grade.Id ?? string.Empty,
                    grade.Name ?? string.Empty,
                    grade.Classes?.Select(classroom => new ControlClassNode(
                            classroom.Id ?? string.Empty,
                            classroom.Name ?? string.Empty))
                        .ToArray() ?? []))
                .ToArray() ?? [],
            DeviceId = tree.Device?.Id,
            DeviceName = tree.Device?.Name,
            ClassId = tree.Device?.ClassId
        };
    }

    /// <summary>保存本机分班。<paramref name="classId" /> 传 <c>null</c> 表示不分班。</summary>
    public async Task AssignClassAsync(
        NodeDeviceCredential credential,
        string? classId,
        CancellationToken cancellationToken = default)
    {
        var request = new AssignRequest
        {
            DeviceId = credential.DeviceId,
            Secret = credential.Secret,
            ClassId = string.IsNullOrWhiteSpace(classId) ? null : classId
        };

        var uri = BuildUri(BaseUrl, DeviceAssignPath);
        using var content = JsonContent.Create(request, options: RequestJsonOptions);
        var response = await SendAsync(uri, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadDeviceErrorAsync(response, cancellationToken).ConfigureAwait(false);
            throw error;
        }
    }

    /// <summary>
    ///     把用户输入规范成 <c>SC-XXXXXX</c> 形态：允许省略 <c>SC</c> 前缀、允许小写与分隔符。
    /// </summary>
    public static string? NormalizeSiteId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var compact = new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (compact.StartsWith("SC", StringComparison.Ordinal) && compact.Length == 2 + SiteIdBodyLength)
            compact = compact[2..];

        return compact.Length == SiteIdBodyLength ? $"SC-{compact}" : null;
    }

    /// <summary>把配对码规范成 8 位大写字母数字（展示时才加中间的连字符）。</summary>
    public static string? NormalizePairingCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var compact = new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return compact.Length == PairingCodeLength ? compact : null;
    }

    /// <summary>把 8 位配对码展示成 <c>ABCD-7KQ2</c>。</summary>
    public static string FormatPairingCode(string? value)
    {
        var compact = NormalizePairingCode(value);
        return compact is null ? value ?? string.Empty : $"{compact[..4]}-{compact[4..]}";
    }

    /// <summary>v1 兼容：判断输入是否像旧版 <c>srn_</c> 令牌。v4 的接入码永远不会命中。</summary>
    public static bool LooksLikeNodeToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().StartsWith(TokenPrefix, StringComparison.Ordinal);

    /// <summary>v1 兼容：旧版接入地址。v4 用 <see cref="EnrollPath" />。</summary>
    public static string BuildEnrollUri(string baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl)
            ? LegacyEnrollPath
            : baseUrl.TrimEnd('/') + LegacyEnrollPath;

    /// <summary>v1 兼容的重载：旧版接入码已断代，调用会得到明确的失败原因。</summary>
    [Obsolete("v4 已改为 地点ID + 配对码 双因子接入，请使用 EnrollAsync(siteId, pairingCode, deviceName, ct)。")]
    public Task<NodeEnrollmentRecord> EnrollAsync(
        string code,
        CancellationToken cancellationToken = default)
        => throw new NodeEnrollmentException(NodeEnrollmentFailure.Disabled,
            message: "v1 接入已断代，请使用 地点 ID + 配对码 重新接入");

    /// <summary>v1 兼容的重载：旧版接入码已断代，调用会得到明确的失败原因。</summary>
    [Obsolete("v4 已改为 地点ID + 配对码 双因子接入，请使用 EnrollAsync(siteId, pairingCode, deviceName, ct)。")]
    public Task<NodeEnrollmentRecord> EnrollAsync(
        string code,
        string? nodeId,
        string? platform,
        string? version,
        string? displayName,
        IReadOnlyList<string>? capabilities,
        CancellationToken cancellationToken = default)
        => throw new NodeEnrollmentException(NodeEnrollmentFailure.Disabled,
            message: "v1 接入已断代，请使用 地点 ID + 配对码 重新接入");

    private async Task<HttpResponseMessage> SendAsync(Uri uri, HttpContent content, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(NodeEnrollmentClient));
        client.Timeout = RequestTimeout;

        try
        {
            return await client.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new NodeEnrollmentException(NodeEnrollmentFailure.Network,
                message: "无法连接集控服务端", statusCode: null);
        }
    }

    private async Task<NodeEnrollmentException> ReadEnrollErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var (code, siteName) = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
        var failure = code switch
        {
            "site_not_found" => NodeEnrollmentFailure.SiteNotFound,
            "code_not_found" => NodeEnrollmentFailure.CodeNotFound,
            "code_site_mismatch" => NodeEnrollmentFailure.CodeSiteMismatch,
            "code_expired" => NodeEnrollmentFailure.CodeExpired,
            "code_used" => NodeEnrollmentFailure.CodeUsed,
            "code_revoked" => NodeEnrollmentFailure.CodeRevoked,
            "platform_not_allowed" => NodeEnrollmentFailure.PlatformNotAllowed,
            "device_conflict" => NodeEnrollmentFailure.DeviceConflict,
            "too_many_requests" => NodeEnrollmentFailure.TooManyAttempts,
            "bad_request" => NodeEnrollmentFailure.InvalidRequest,
            _ => NodeEnrollmentFailure.Unknown
        };

        return new NodeEnrollmentException(failure, code, (int)response.StatusCode,
            message: code, conflictSiteName: siteName);
    }

    private async Task<NodeEnrollmentException> ReadDeviceErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var (code, _) = await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
        var failure = code switch
        {
            "bad_secret" => NodeEnrollmentFailure.CodeNotFound,
            "device_revoked" => NodeEnrollmentFailure.CodeRevoked,
            "bad_request" => NodeEnrollmentFailure.InvalidRequest,
            _ => NodeEnrollmentFailure.Unknown
        };

        return new NodeEnrollmentException(failure, code, (int)response.StatusCode, message: code);
    }

    private static async Task<(string? Code, string? SiteName)> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;

            var code = ReadString(root, "code");
            var siteName = ReadString(root, "detail", "siteName") ?? ReadString(root, "siteName");
            return (code, siteName);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return new Uri(trimmed + path, UriKind.Absolute);
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        var current = element;
        foreach (var name in names)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return null;

            JsonElement next;
            if (current.TryGetProperty(name, out next))
            {
                current = next;
                continue;
            }

            // 服务端 JSON 是 camelCase，同时容忍 snake_case/PascalCase。
            var snake = ToSnake(name);
            if (snake != name && current.TryGetProperty(snake, out next))
            {
                current = next;
                continue;
            }

            return null;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            _ => null
        };
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

    /// <summary>稳定机器标识：优先用本机节点状态里持久化的随机 ID，其次主机名+系统版本的散列。</summary>
    private string ResolveMachineGuid()
    {
        var nodeId = _stateStore?.Current.NodeId;
        if (!string.IsNullOrWhiteSpace(nodeId))
            return nodeId;

        var seed = $"{Environment.MachineName}|{Environment.OSVersion.VersionString}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class EnrollRequest
    {
        public string SiteId { get; init; } = string.Empty;

        public string Code { get; init; } = string.Empty;

        public string Platform { get; init; } = string.Empty;

        public string DeviceName { get; init; } = string.Empty;

        public MachineInfo Machine { get; init; } = new();
    }

    private sealed class MachineInfo
    {
        public string Host { get; init; } = string.Empty;

        public string Guid { get; init; } = string.Empty;
    }

    private sealed class DeviceCredentialRequest
    {
        public string DeviceId { get; init; } = string.Empty;

        public string Secret { get; init; } = string.Empty;
    }

    private sealed class AssignRequest
    {
        public string DeviceId { get; init; } = string.Empty;

        public string Secret { get; init; } = string.Empty;

        public string? ClassId { get; init; }
    }

    private sealed class DeviceTreeResponse
    {
        public SiteDto? Site { get; init; }

        public List<GradeDto>? Grades { get; init; }

        public DeviceDto? Device { get; init; }
    }

    private sealed class SiteDto
    {
        public string? PublicId { get; init; }

        public string? Name { get; init; }
    }

    private sealed class GradeDto
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public List<ClassDto>? Classes { get; init; }
    }

    private sealed class ClassDto
    {
        public string? Id { get; init; }

        public string? Name { get; init; }
    }

    private sealed class DeviceDto
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? ClassId { get; init; }
    }
}