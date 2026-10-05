using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Services.Auth;
using SecRandom.Shared.Models.ControlPlane;

namespace SecRandom.Services.ControlPlane;

/// <summary>命令轮询的节奏与总预算。</summary>
/// <param name="Delays">每一轮之间的等待；用完之后一直沿用最后一个值。</param>
/// <param name="Timeout">总预算，超过就按"没等到回执"处理。</param>
/// <remarks>
///     1s/2s/4s 是"教室机通常几秒内回执、但不该让手机每 200ms 打一次服务端"之间的折中；
///     总预算宁可短一点：手机端该做的是一次操作，不是一个后台任务。
/// </remarks>
public sealed record ControlPlanePollOptions(
    IReadOnlyList<TimeSpan>? Delays = null,
    TimeSpan? Timeout = null)
{
    public static ControlPlanePollOptions Default { get; } = new();

    public IReadOnlyList<TimeSpan> EffectiveDelays { get; } = Delays is { Count: > 0 }
        ? Delays
        : [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    public TimeSpan EffectiveTimeout { get; } = Timeout ?? TimeSpan.FromSeconds(30);

    public TimeSpan DelayFor(int attempt) => EffectiveDelays[Math.Min(attempt, EffectiveDelays.Count - 1)];
}

/// <summary>控制面 REST 客户端。</summary>
/// <remarks>
///     所有请求都经过 <see cref="IAuthorizedApiSender" />（Bearer + 单飞刷新），客户端自己不碰令牌。
///     <c>roster.read</c>/<c>draw.trigger</c> 都是"下命令 + 轮询回执"，因此这里只有五条方法与一个轮询辅助。
/// </remarks>
public interface IControlPlaneClient
{
    /// <summary>当前账号所属的组。</summary>
    Task<IReadOnlyList<GroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>组内的节点。</summary>
    Task<IReadOnlyList<NodeDto>> GetNodesAsync(string groupId, CancellationToken cancellationToken = default);

    /// <summary>对某个节点下发一条命令（202 返回命令记录；同步完成的期望状态可能直接给结果）。</summary>
    Task<NodeCommandDto> SubmitCommandAsync(
        string groupId,
        string nodeId,
        ControlPlaneCommandRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>查询一条命令的当前状态。</summary>
    Task<NodeCommandDto> GetCommandAsync(
        string groupId,
        string commandId,
        CancellationToken cancellationToken = default);

    /// <summary>轮询到终态；超预算时抛 <see cref="ControlPlaneErrorKind.Timeout" />。</summary>
    Task<NodeCommandDto> PollCommandAsync(
        string groupId,
        string commandId,
        ControlPlanePollOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class ControlPlaneClient(
    IAuthorizedApiSender sender,
    ILogger<ControlPlaneClient> logger,
    string? baseUrl = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IControlPlaneClient
{
    /// <summary>控制面所在的服务地址。</summary>
    /// <remarks>
    ///     与服务端约定的是**相对**路径（<c>/v1/groups</c>…），因此这里只拼一个基址；
    ///     服务端上线后若挂在别的路径下，改这一行即可，不必翻遍客户端。
    /// </remarks>
    public static string DefaultBaseUrl => SectlAuthService.ApiBaseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<IReadOnlyList<GroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        var body = await SendForBodyAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/groups"),
            cancellationToken).ConfigureAwait(false);

        return Deserialize<List<GroupDto>>(body);
    }

    public async Task<IReadOnlyList<NodeDto>> GetNodesAsync(string groupId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var body = await SendForBodyAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/groups/{Escape(groupId)}/nodes"),
            cancellationToken).ConfigureAwait(false);

        return Deserialize<List<NodeDto>>(body);
    }

    public Task<NodeCommandDto> SubmitCommandAsync(
        string groupId,
        string nodeId,
        ControlPlaneCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(request);

        return SendForCommandAsync(
            () => new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/groups/{Escape(groupId)}/nodes/{Escape(nodeId)}/commands")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(request, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            },
            cancellationToken);
    }

    public Task<NodeCommandDto> GetCommandAsync(
        string groupId,
        string commandId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        return SendForCommandAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/groups/{Escape(groupId)}/commands/{Escape(commandId)}"),
            cancellationToken);
    }

    public async Task<NodeCommandDto> PollCommandAsync(
        string groupId,
        string commandId,
        ControlPlanePollOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var plan = options ?? ControlPlanePollOptions.Default;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (var attempt = 0; ; attempt++)
        {
            var command = await GetCommandAsync(groupId, commandId, cancellationToken).ConfigureAwait(false);
            if (command.IsTerminal)
                return command;

            var wait = plan.DelayFor(attempt);
            if (stopwatch.Elapsed + wait > plan.EffectiveTimeout)
            {
                // 超时**不是抽取失败**：设备可能只是在动画/播报里，命令也许已经成功了。
                // 因此给一个专门的分类，手机端据此说"设备还没回执，稍后再看"，而不是"抽取失败"。
                throw new ControlPlaneException(
                    "poll_timeout",
                    ControlPlaneErrorKind.Timeout,
                    message: $"等待命令回执超过 {plan.EffectiveTimeout.TotalSeconds:0} 秒。");
            }

            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<NodeCommandDto> SendForCommandAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        var body = await SendForBodyAsync(createRequest, cancellationToken).ConfigureAwait(false);
        var command = Deserialize<NodeCommandDto>(body);

        // 下命令的端点还可能回"期望状态结果"这类非命令载荷。把没有 command_id 的响应当成功，
        // 只会让调用方拿着一个空 id 去轮询；在这里就说清楚"这不是一条命令回执"。
        return !string.IsNullOrWhiteSpace(command.CommandId)
            ? command
            : throw new ControlPlaneException(
                "invalid_response",
                ControlPlaneErrorKind.InvalidResponse,
                message: "控制面返回的不是一条命令回执（缺少 command_id）。");
    }

    private async Task<string> SendForBodyAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await sender.SendAuthorizedAsync(createRequest, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return body;

            var exception = CreateException(response.StatusCode, body);
            logger.LogWarning(
                "控制面请求失败：状态={Status}，错误码={Code}，分类={Kind}",
                (int)response.StatusCode, exception.Code, exception.Kind);
            throw exception;
        }
    }

    /// <summary>
    ///     把失败响应变成一个带原样错误码的异常。
    /// </summary>
    /// <remarks>
    ///     服务端的错误体风格与云接口一致（<c>error</c>/<c>error_description</c>），因此这里按同一套字段读；
    ///     读不到就用 <c>http_&lt;状态码&gt;</c>。**永远不丢码**：手机端显示的是分类，
    ///     但日志与反馈里必须留下服务端说的那个词。
    /// </remarks>
    private static ControlPlaneException CreateException(HttpStatusCode statusCode, string body)
    {
        string? code = null;
        string? description = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                        code = error.GetString();
                    if (document.RootElement.TryGetProperty("error_description", out var detail) && detail.ValueKind == JsonValueKind.String)
                        description = detail.GetString();
                    if (code is null
                        && document.RootElement.TryGetProperty("code", out var codeElement)
                        && codeElement.ValueKind == JsonValueKind.String)
                        code = codeElement.GetString();
                }
            }
            catch (JsonException)
            {
                // 错误体不是 JSON（网关页面、空响应）不是问题：状态码本身已经足够分类。
            }
        }

        return new ControlPlaneException(
            string.IsNullOrWhiteSpace(code) ? ControlPlaneException.FallbackCode(statusCode) : code!,
            ControlPlaneException.ClassifyStatus(statusCode),
            (int)statusCode,
            message: description ?? code ?? ControlPlaneException.FallbackCode(statusCode));
    }

    /// <summary>
    ///     解析成功响应；解析不了就按 InvalidResponse 抛出去，而不是回一个空列表。
    /// </summary>
    /// <remarks>
    ///     "服务端 200 但内容读不懂"与"这个组里确实没有设备"是两件事：
    ///     前者要显示"控制面返回异常"，后者该显示空态。用空列表兜住前者会把故障伪装成空数据。
    /// </remarks>
    private static T Deserialize<T>(string body) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ControlPlaneException(
                "invalid_response",
                ControlPlaneErrorKind.InvalidResponse,
                message: "控制面返回了空响应。");

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                   ?? throw new ControlPlaneException(
                       "invalid_response",
                       ControlPlaneErrorKind.InvalidResponse,
                       message: "控制面的响应内容是 null。");
        }
        catch (JsonException exception)
        {
            throw new ControlPlaneException(
                "invalid_response",
                ControlPlaneErrorKind.InvalidResponse,
                message: $"控制面响应无法解析：{exception.Message}");
        }
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
