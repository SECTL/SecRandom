using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Services.Ipc;
using SecRandom.Shared.Models.Ipc;

namespace SecRandom.Core.Services.SingleInstance;

/// <summary>
///     基于 Mutex 的跨平台单实例守护 + Named Pipe IPC 服务。
///     <para>
///         第一实例调用 <see cref="TryAcquire" /> 成功后，后台监听管道命令。
///         第二实例调用 <see cref="TryAcquire" /> 失败（<see cref="IsDuplicate" /> = true），
///         再通过 <see cref="SendCommandAsync" /> 向第一实例发送 IPC 指令。
///     </para>
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    // 使用项目特定 ID 作为 Mutex / Pipe 名称后缀，保证唯一性。
    private const string AppId = "SecRandom_3F2A1B0E";
    private const string MutexName = $"SecRandom_SingleInstance_{AppId}";
    public const string IpcPipeName = $"SecRandom_IPC_{AppId}";
    private static readonly TimeSpan FrameReadTimeout = TimeSpan.FromSeconds(5);
    private const int MaxConcurrentConnections = 8;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;
    private bool _disposed;
    private readonly SemaphoreSlim _connectionGate = new(MaxConcurrentConnections, MaxConcurrentConnections);

    // 静态单例，允许 App 层在 DI 容器构建前访问。
    private static SingleInstanceService? _instance;
    private static readonly object _lock = new();

    /// <summary>获取全局单例。</summary>
    public static SingleInstanceService Instance
    {
        get
        {
            lock (_lock)
            {
                return _instance ??= new SingleInstanceService();
            }
        }
    }

    /// <summary>当前进程是否为重复实例（Mutex 已被第一实例持有）。</summary>
    public bool IsDuplicate { get; private set; }

    /// <summary>
    ///     第一实例收到 IPC 命令时触发。
    ///     回调在线程池线程上执行，调用方需自行切换到 UI 线程。
    /// </summary>
    public event Action<string>? CommandReceived;
    public event Func<IpcRequestEnvelope, CancellationToken, Task<IpcResponseEnvelope>>? RequestReceived;

    private static readonly JsonSerializerOptions IpcJsonOptions = new(JsonSerializerDefaults.Web);

    private SingleInstanceService() { }

    /// <summary>
    ///     尝试获取 Mutex。
    ///     成功：<see cref="IsDuplicate" /> = false，并启动 IPC 服务端。
    ///     失败：<see cref="IsDuplicate" /> = true。
    /// </summary>
    /// <returns>true 表示当前是唯一运行实例。</returns>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // Mutex 已存在，尝试立即获取（零等待时间）
            try
            {
                createdNew = _mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // 前一进程异常退出导致 Mutex 遗弃，视为获取成功
                createdNew = true;
            }
        }

        IsDuplicate = !createdNew;

        if (!IsDuplicate)
            StartIpcServer();

        return !IsDuplicate;
    }

    /// <summary>
    ///     向第一实例发送 IPC 命令（仅重复实例调用）。
    /// </summary>
    /// <param name="command">命令字符串，使用 <see cref="SingleInstanceCommand" /> 中的常量。</param>
    /// <returns>true 表示发送成功。</returns>
    public static async Task<bool> SendCommandAsync(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", IpcPipeName,
                PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await client.ConnectAsync(3000).ConfigureAwait(false);

            using var writer = new StreamWriter(client, leaveOpen: false);
            await writer.WriteLineAsync(command).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<IpcResponseEnvelope> SendRequestAsync(
        IpcRequestEnvelope request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", IpcPipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(3000, cancellationToken).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, StrictUtf8, leaveOpen: true);
            using var reader = new StreamReader(client, StrictUtf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, IpcJsonOptions)).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            var responseLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            var response = string.IsNullOrWhiteSpace(responseLine)
                ? null
                : JsonSerializer.Deserialize<IpcResponseEnvelope>(responseLine, IpcJsonOptions);
            return response ?? IpcResponseEnvelope.TransportFailure(request.Type, "invalid_response", "IPC 响应无效。");
        }
        catch (OperationCanceledException)
        {
            return IpcResponseEnvelope.TransportFailure(request.Type, "timeout", "IPC 请求超时。");
        }
        catch
        {
            return IpcResponseEnvelope.TransportFailure(request.Type, "pipe_unavailable", "无法连接到 SecRandom。");
        }
    }

    /// <summary>
    ///     启动 Named Pipe 服务端，在后台线程上循环接受连接。
    /// </summary>
    private void StartIpcServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var gateEntered = false;
                NamedPipeServerStream? server = null;
                try
                {
                    await _connectionGate.WaitAsync(token).ConfigureAwait(false);
                    gateEntered = true;
                    server = new NamedPipeServerStream(
                        IpcPipeName,
                        PipeDirection.InOut,
                        maxNumberOfServerInstances: MaxConcurrentConnections,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    var acceptedServer = server;
                    _ = Task.Run(() => HandleAcceptedConnectionAsync(acceptedServer, token));
                    server = null;
                    gateEntered = false;
                }
                catch (OperationCanceledException)
                {
                    server?.Dispose();
                    if (gateEntered)
                        _connectionGate.Release();
                    break;
                }
                catch
                {
                    server?.Dispose();
                    if (gateEntered)
                        _connectionGate.Release();
                    // 连接中断或管道错误，短暂等待后继续监听
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
        }, token);
    }

    private async Task HandleAcceptedConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        await using var connection = server;
        try
        {
            await HandleConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // A malformed or disconnected client must not terminate the IPC listener.
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private static async Task<string?> ReadRequestLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[ProtocolRequestParser.MaxRequestLength + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var character = await reader.ReadAsync(buffer.AsMemory(length, 1), cancellationToken).ConfigureAwait(false);
            if (character == 0)
                break;
            if (buffer[length] == '\n')
                break;
            length++;
        }

        return length > ProtocolRequestParser.MaxRequestLength ? null : new string(buffer, 0, length).TrimEnd('\r');
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(server, StrictUtf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using var writer = new StreamWriter(server, StrictUtf8, leaveOpen: true);
        using var frameCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        frameCts.CancelAfter(FrameReadTimeout);
        var frame = await ReadRequestLineAsync(reader, frameCts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(frame))
            return;

        if (frame[0] != '{')
        {
            CommandReceived?.Invoke(frame);
            return;
        }

        IpcRequestEnvelope? request;
        try
        {
            request = JsonSerializer.Deserialize<IpcRequestEnvelope>(frame, IpcJsonOptions);
        }
        catch (JsonException)
        {
            await WriteResponseAsync(writer,
                IpcResponseEnvelope.TransportFailure("unknown", "invalid_request", "IPC 请求格式无效。")).ConfigureAwait(false);
            return;
        }

        if (request is null || request.Version != 1 || !string.Equals(request.Type, "url", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.Payload?.Url))
        {
            await WriteResponseAsync(writer,
                IpcResponseEnvelope.TransportFailure(request?.Type ?? "unknown", "invalid_request", "IPC 请求格式无效。")).ConfigureAwait(false);
            return;
        }

        var handlers = RequestReceived;
        if (handlers is null)
        {
            await WriteResponseAsync(writer,
                IpcResponseEnvelope.TransportFailure(request.Type, "pipe_unavailable", "SecRandom 尚未准备好处理 IPC 请求。")).ConfigureAwait(false);
            return;
        }

        IpcResponseEnvelope response;
        try
        {
            response = await handlers(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            response = IpcResponseEnvelope.TransportFailure(request.Type, "internal_error", "IPC 请求处理失败。");
        }

        await WriteResponseAsync(writer, response).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(StreamWriter writer, IpcResponseEnvelope response)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, IpcJsonOptions)).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        _pipeCts = null;

        if (!IsDuplicate)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 未持有 Mutex（例如从未获取成功），忽略
            }
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
