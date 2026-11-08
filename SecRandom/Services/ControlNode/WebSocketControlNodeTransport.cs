using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <see cref="IControlNodeTransport" /> 的 WebSocket 实现（v4：纯文本信封，不带任何凭据）。
/// </summary>
/// <remarks>
///     协议帧都是 UTF-8 文本帧，单帧上限 64 KiB。超限或非文本帧结束连接：合法节点不会发出这种帧，
///     继续保持连接只会让故障无限刷日志。
/// </remarks>
public sealed class WebSocketControlNodeTransport(ClientWebSocket socket, ILogger<WebSocketControlNodeTransport> logger)
    : IControlNodeTransport
{
    private const int MaxFrameBytes = ControlV4Protocol.MaxFrameBytes;
    private const int ReceiveBufferSize = 4096;

    public string? CloseReason { get; private set; }

    public int? CloseCode { get; private set; }

    public async Task SendAsync(string frameJson, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(frameJson);
        if (payload.Length > MaxFrameBytes)
            throw new ControlFrameTooLargeException("frame", payload.Length, MaxFrameBytes);

        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
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
                CloseCode = result.CloseStatus is { } status ? (int)status : null;
                CloseReason = string.IsNullOrWhiteSpace(result.CloseStatusDescription)
                    ? result.CloseStatus?.ToString()
                    : result.CloseStatusDescription;

                if (!string.IsNullOrWhiteSpace(CloseReason))
                    logger.LogInformation("集控节点连接被对端关闭：{Code} {Reason}", CloseCode, CloseReason);

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

        return accumulated.Length == 0 ? null : Encoding.UTF8.GetString(accumulated.ToArray());
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

/// <summary>建立节点 WebSocket 连接。v4 不在 URL 或请求头里放任何凭据。</summary>
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
        socket.Options.CollectHttpResponseDetails = true;

        try
        {
            await socket.ConnectAsync(endpoint!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var statusCode = ReadUpgradeStatusCode(socket);
            socket.Dispose();

            if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ControlNodeConnectRejectedException(statusCode.Value, exception);

            throw;
        }

        return new WebSocketControlNodeTransport(socket, logger);
    }

    private static HttpStatusCode? ReadUpgradeStatusCode(ClientWebSocket socket)
    {
        try
        {
            return socket.HttpStatusCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}