using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <see cref="IControlNodeTransport" /> 的 WebSocket 实现。
/// </summary>
/// <remarks>
///     协议帧都是 UTF-8 文本帧，单帧上限 64 KiB。超出上限或无法解析的帧**结束连接**
///     而不是被忽略：合法节点不会发出这种帧，继续保持连接只会让故障无限刷日志，
///     断开重连能让问题可见且可恢复。
/// </remarks>
public sealed class WebSocketControlNodeTransport(ClientWebSocket socket, ILogger<WebSocketControlNodeTransport> logger)
    : IControlNodeTransport
{
    private const int MaxFrameBytes = ControlProtocolJson.MaxFrameBytes;
    private const int ReceiveBufferSize = 4096;

    /// <inheritdoc />
    public string? CloseReason { get; private set; }

    public async Task SendAsync(ControlFrame frame, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(ControlProtocolJson.Serialize(frame));
        if (payload.Length > MaxFrameBytes)
            throw new ControlFrameTooLargeException(frame.Type ?? "unknown", payload.Length, MaxFrameBytes);

        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ControlFrame?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        using var accumulated = new MemoryStream();

        while (true)
        {
            var result = await socket
                .ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                // 关闭码与原因要留下来：服务端接管同一 node_id 时会用 CloseStatusDescription="replaced"。
                CloseReason = string.IsNullOrWhiteSpace(result.CloseStatusDescription)
                    ? result.CloseStatus?.ToString()
                    : result.CloseStatusDescription;

                if (!string.IsNullOrWhiteSpace(CloseReason))
                    logger.LogInformation("集控节点连接被对端关闭：{Reason}", CloseReason);

                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                logger.LogWarning("集控节点收到非文本帧，结束连接。");
                return null;
            }

            if (accumulated.Length + result.Count > MaxFrameBytes)
            {
                logger.LogWarning("集控帧超过 {Limit} 字节上限，结束连接。", MaxFrameBytes);
                return null;
            }

            accumulated.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
                break;
        }

        if (accumulated.Length == 0)
            return null;

        var frame = ControlProtocolJson.TryParse(Encoding.UTF8.GetString(accumulated.ToArray()));
        if (frame is null)
            logger.LogWarning("集控帧无法解析，结束连接。");

        return frame;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_stop", CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // 对端已经断开：无需再关。
        }
        finally
        {
            socket.Dispose();
        }
    }
}

/// <summary>建立节点 WebSocket 连接，凭据放在 <c>Authorization</c> 头而不是 URL。</summary>
public sealed class WebSocketControlNodeTransportFactory(ILogger<WebSocketControlNodeTransport> logger)
    : IControlNodeTransportFactory
{
    public async Task<IControlNodeTransport> ConnectAsync(
        ControlNodeConnectRequest request,
        CancellationToken cancellationToken)
    {
        if (!ControlEndpointPolicy.TryValidate(request.Endpoint, out var endpoint, out var error))
            throw new InvalidOperationException($"集控节点地址无效：{error}");

        var socket = new ClientWebSocket();
        // 查询参数会进访问日志，所以凭据只走请求头。
        socket.Options.SetRequestHeader("Authorization", $"Bearer {request.BearerToken}");
        // 升级失败时才能读回 HTTP 状态码：401/403（凭据被拒）与断网/DNS/超时
        // 必须走完全不同的处理，见 ControlNodeConnectRejectedException。
        socket.Options.CollectHttpResponseDetails = true;

        try
        {
            await socket.ConnectAsync(endpoint!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // 状态码必须在 Dispose 之前读。
            var statusCode = ReadUpgradeStatusCode(socket);
            socket.Dispose();

            if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ControlNodeConnectRejectedException(statusCode.Value, exception);

            throw;
        }

        return new WebSocketControlNodeTransport(socket, logger);
    }

    /// <summary>
    ///     读回升级响应的状态码；没有 HTTP 响应（DNS 失败、连接被拒、超时）时为 <c>null</c>。
    /// </summary>
    private static HttpStatusCode? ReadUpgradeStatusCode(ClientWebSocket socket)
    {
        try
        {
            return socket.HttpStatusCode;
        }
        catch (InvalidOperationException)
        {
            // 没有打开 CollectHttpResponseDetails 时这个属性会抛异常。这里绝不能让
            // "读不到状态码"把原本的连接失败替换成另一种异常：读不到就按网络故障处理。
            return null;
        }
    }
}
