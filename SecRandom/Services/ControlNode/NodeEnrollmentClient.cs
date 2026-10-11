using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlPlane;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     接入失败的原因，直接对应服务端 <c>{"code":"…"}</c> 的那几个取值。
/// </summary>
/// <remarks>
///     之所以不把服务端的错误码当字符串往上抛：设置页要按原因给**不同的下一步**
///     （接入码写错了 ⇒ 重填；过期/已用过/已撤销 ⇒ 换一个码；试太多次 ⇒ 等一会儿；
///     平台关了接入 ⇒ 去找管理员），用字符串 switch 迟早会漏掉一个分支。
/// </remarks>
public enum NodeEnrollmentFailure
{
    /// <summary>没有拿到可识别的服务端原因（网络、超时、5xx、响应看不懂）。</summary>
    Unknown,

    /// <summary>接入码格式不对，或请求本身不合法（HTTP 400 <c>invalid_request</c>）。</summary>
    InvalidRequest,

    /// <summary>接入码不存在或不匹配（HTTP 401 <c>enrollment_code_invalid</c>）。</summary>
    CodeInvalid,

    /// <summary>接入码已过期（HTTP 410 <c>enrollment_code_expired</c>）。</summary>
    CodeExpired,

    /// <summary>接入码已经被用掉了（HTTP 410 <c>enrollment_code_used</c>）——一个码只能用一次。</summary>
    CodeUsed,

    /// <summary>接入码已被管理员撤销（HTTP 410 <c>enrollment_code_revoked</c>）。</summary>
    CodeRevoked,

    /// <summary>尝试次数过多（HTTP 429 <c>too_many_attempts</c>），需要等一会儿再试。</summary>
    TooManyAttempts,

    /// <summary>服务端没有开放接入（HTTP 503 <c>enrollment_disabled</c>）。</summary>
    Disabled,

    /// <summary>请求没能到达服务端（DNS/连接/TLS/超时）。**接入码本身可能还是好的**，值得重试。</summary>
    Network,

    /// <summary>服务端答应得不合契约（缺字段、令牌前缀不对）。</summary>
    InvalidResponse
}

/// <summary>接入被服务端拒绝，或请求没能完成。</summary>
/// <remarks>
///     消息里**只有服务端给的 <c>code</c> 与 HTTP 状态**，绝不含接入码或令牌：
///     异常会被日志与诊断包记下来，任何用户输入都不该借它落进日志。
/// </remarks>
public sealed class NodeEnrollmentException : Exception
{
    public NodeEnrollmentException(NodeEnrollmentFailure failure, string? serverCode = null, int? statusCode = null, string? message = null)
        : base(message ?? serverCode ?? failure.ToString())
    {
        Failure = failure;
        ServerCode = serverCode;
        StatusCode = statusCode;
    }

    public NodeEnrollmentFailure Failure { get; }

    /// <summary>服务端原样给的错误码（如有）。</summary>
    public string? ServerCode { get; }

    public int? StatusCode { get; }
}

/// <summary>
///     走一次 <c>POST {集控基址}/v1/node/enroll</c>，把接入码换成节点令牌。
/// </summary>
/// <remarks>
///     <para>
///         <b>这个请求是匿名的</b>，因此它**不经过** <see cref="IAuthorizedApiSender" />：
///         那条通道的意义是"带上本账号的 access token 并在 401 时刷新一次"，而这里恰恰是
///         "还没有账号、也不需要有账号"的那一步。用匿名 <see cref="HttpClient" /> 直发，
///         顺带保证这个请求永远不会把 SECTL 令牌送到自建服务器上。
///     </para>
///     <para>
///         返回的 <see cref="NodeEnrollmentRecord.NodeToken" /> 是**不透明**的 <c>srn_…</c> 值，
///         此后控制面 REST 与节点 WebSocket 都直接拿它当 Bearer 用，客户端不解析它的内部结构。
///     </para>
///     <para>
///         HTTP 状态 → 原因的映射按服务端契约固定下来：400 <c>invalid_request</c>、
///         401 <c>enrollment_code_invalid</c>、410 <c>expired|used|revoked</c>、
///         429 <c>too_many_attempts</c>、503 <c>enrollment_disabled</c>。
///         响应体里如果还给了 <c>code</c>，**以它为准**——状态码只说了一个大类。
///     </para>
/// </remarks>
public sealed class NodeEnrollmentClient
{
    /// <summary>节点令牌前缀（服务端契约），也是"用户填的是令牌还是接入码"的判据。</summary>
    public const string TokenPrefix = "srn_";

    /// <summary>接入请求的路径，拼在控制面基址后面。</summary>
    public const string EnrollPath = "/v1/node/enroll";

    /// <summary>
    ///     接入码输入框的长度上限（**界面护栏，不是协议约束**）。
    /// </summary>
    /// <remarks>
    ///     控制台签发的接入码很短（形如 <c>7K3M-9QZX</c>），但用户也可能把一整份 <c>srn_…</c>
    ///     令牌粘进这个框里，所以上限取得足够宽：它只用来挡住"把整段 JSON / 一整篇文本误粘进来"，
    ///     真值校验始终在服务端。写死一个贴近接入码真实长度的数字只会把合法的长令牌挡在门外。
    /// </remarks>
    public const int MaxCodeLength = 128;

    /// <summary>
    ///     接入请求的超时（与节点通道握手同一个量级）：接入是用户在场、盯着界面的操作，
    ///     等太久只会让人以为按钮坏了，不如早点回"网络不通，重试"。
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IControlPlaneEndpointStore _endpointStore;
    private readonly string? _fixedBaseUrl;

    public NodeEnrollmentClient(
        IHttpClientFactory httpClientFactory,
        IControlPlaneEndpointStore endpointStore,
        string? baseUrl = null)
    {
        _httpClientFactory = httpClientFactory;
        _endpointStore = endpointStore;
        _fixedBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl!.Trim().TrimEnd('/');
    }

    /// <summary>本次请求会打到的基址（自建/私有部署时由设置页决定，因此每次请求都重新读）。</summary>
    public string BaseUrl => _fixedBaseUrl ?? _endpointStore.Current;

    /// <summary>
    ///     用接入码换节点令牌。
    /// </summary>
    /// <param name="code">用户填的接入码，形如 <c>7K3M-9QZX</c>。</param>
    /// <param name="nodeId">本机已有的节点 ID；服务端可据此把新令牌绑到同一个节点上。</param>
    /// <param name="platform">平台名（与节点通道上报的同一个值）。</param>
    /// <param name="version">客户端版本。</param>
    /// <param name="displayName">控制台里显示的名字；与节点通道一致地留空即缺席。</param>
    /// <param name="capabilities">能力列表；不填即缺席（服务端按自身支持的集合判定）。</param>
    public async Task<NodeEnrollmentRecord> EnrollAsync(
        string code,
        string? nodeId = null,
        string? platform = null,
        string? version = null,
        string? displayName = null,
        IReadOnlyList<string>? capabilities = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var request = new EnrollRequest
        {
            Code = code.Trim(),
            NodeId = Blank(nodeId),
            Platform = Blank(platform),
            Version = Blank(version),
            DisplayName = Blank(displayName),
            Capabilities = capabilities is { Count: > 0 } ? capabilities : null
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, EnrollUri)
        {
            Content = JsonContent.Create(request, options: RequestJsonOptions)
        };

        // 一次接入一个客户端实例：接入是"用户点一下"的低频操作，共用实例省下的那点开销
        // 换不来"基址改了要重启才生效"的困惑。
        using var http = _httpClientFactory.CreateClient();
        http.Timeout = RequestTimeout;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // 没能送到服务端：接入码可能还是好的，界面应当说"网络不通，重试"，而不是"码不对"。
            throw new NodeEnrollmentException(NodeEnrollmentFailure.Network, message: exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NodeEnrollmentException(NodeEnrollmentFailure.Network, message: exception.Message);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw BuildFailure(response, body);

            EnrollResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<EnrollResponse>(body, ResponseJsonOptions);
            }
            catch (JsonException exception)
            {
                throw new NodeEnrollmentException(
                    NodeEnrollmentFailure.InvalidResponse,
                    message: exception.Message,
                    statusCode: (int)response.StatusCode);
            }

            if (payload is null
                || string.IsNullOrWhiteSpace(payload.NodeToken)
                || !payload.NodeToken!.StartsWith(TokenPrefix, StringComparison.Ordinal))
            {
                // 令牌前缀是服务端契约的一部分：没有它，后面两路请求都会带着一个必然被拒的头去连。
                throw new NodeEnrollmentException(
                    NodeEnrollmentFailure.InvalidResponse,
                    message: "接入响应缺少合法的 node_token。",
                    statusCode: (int)response.StatusCode);
            }

            return new NodeEnrollmentRecord
            {
                NodeId = string.IsNullOrWhiteSpace(payload.NodeId) ? string.Empty : payload.NodeId!.Trim(),
                GroupId = string.IsNullOrWhiteSpace(payload.GroupId) ? string.Empty : payload.GroupId!.Trim(),
                GroupName = Blank(payload.GroupName),
                NodeToken = payload.NodeToken!.Trim(),
                ExpiresAt = payload.ExpiresAt?.ToUniversalTime()
            };
        }
    }

    /// <summary>
    ///     用户填进来的东西是不是**已经就是**一个节点令牌。
    /// </summary>
    /// <remarks>
    ///     粘贴一个现成令牌时不该再去换一次：换回来的是**另一个**令牌，而旧的那个还在服务端有效——
    ///     一个班几十台机器复制粘贴同一个令牌是运维常态，这里必须认。
    /// </remarks>
    public static bool LooksLikeNodeToken(string? value) =>
        value is not null && value.Trim().StartsWith(TokenPrefix, StringComparison.Ordinal);

    /// <summary>接入地址：控制面基址 + <see cref="EnrollPath" />。</summary>
    public string EnrollUri => BuildEnrollUri(BaseUrl);

    /// <summary>
    ///     把控制面基址拼成接入地址。
    /// </summary>
    /// <remarks>
    ///     接入是**控制面（集控 REST）的接口**，因此基址取控制面基址——自建部署在设置页填的
    ///     就是这一个地址。它同时是"控制台访问集控平台所用的基址"，所以客户端把手机控制台与
    ///     本机接入指向同一台服务器，不会出现"控制台连 A、节点接入到 B"。
    /// </remarks>
    public static string BuildEnrollUri(string? baseUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl!.Trim().TrimEnd('/');
        return normalized is null ? EnrollPath : normalized + EnrollPath;
    }

    private static NodeEnrollmentException BuildFailure(HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        var serverCode = ReadErrorCode(body);

        var failure = status switch
        {
            400 => NodeEnrollmentFailure.InvalidRequest,
            401 => NodeEnrollmentFailure.CodeInvalid,
            429 => NodeEnrollmentFailure.TooManyAttempts,
            503 => NodeEnrollmentFailure.Disabled,
            410 => serverCode switch
            {
                "enrollment_code_used" => NodeEnrollmentFailure.CodeUsed,
                "enrollment_code_revoked" => NodeEnrollmentFailure.CodeRevoked,
                _ => NodeEnrollmentFailure.CodeExpired
            },
            _ => NodeEnrollmentFailure.Unknown
        };

        // 响应体里给了 code 就以它为准：状态码只说了一个大类，而界面要按具体原因给出下一步。
        failure = serverCode switch
        {
            "invalid_request" => NodeEnrollmentFailure.InvalidRequest,
            "enrollment_code_invalid" => NodeEnrollmentFailure.CodeInvalid,
            "enrollment_code_expired" => NodeEnrollmentFailure.CodeExpired,
            "enrollment_code_used" => NodeEnrollmentFailure.CodeUsed,
            "enrollment_code_revoked" => NodeEnrollmentFailure.CodeRevoked,
            "too_many_attempts" => NodeEnrollmentFailure.TooManyAttempts,
            "enrollment_disabled" => NodeEnrollmentFailure.Disabled,
            _ => failure
        };

        return new NodeEnrollmentException(failure, serverCode, status);
    }

    /// <summary>响应体形如 <c>{"code":"enrollment_code_invalid"}</c>；读不出来就回 null。</summary>
    private static string? ReadErrorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                return code.GetString();

            // 少数网关会把它包一层 error；两种都认，读不出来也算"没给码"。
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class EnrollRequest
    {
        public string? Code { get; set; }

        public string? NodeId { get; set; }

        public string? Platform { get; set; }

        public string? Version { get; set; }

        public string? DisplayName { get; set; }

        public IReadOnlyList<string>? Capabilities { get; set; }
    }

    private sealed class EnrollResponse
    {
        [JsonPropertyName("node_id")]
        public string? NodeId { get; set; }

        [JsonPropertyName("group_id")]
        public string? GroupId { get; set; }

        [JsonPropertyName("group_name")]
        public string? GroupName { get; set; }

        [JsonPropertyName("node_token")]
        public string? NodeToken { get; set; }

        /// <summary>服务端给的有效期；解析不出来就是 null（**不因为一个显示字段把接入判失败**）。</summary>
        [JsonPropertyName("expires_at")]
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
