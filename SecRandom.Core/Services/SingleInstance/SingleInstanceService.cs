using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

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
    private const string PipeName = $"SecRandom_IPC_{AppId}";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;
    private bool _disposed;

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
                ".", PipeName,
                PipeDirection.Out, PipeOptions.Asynchronous);

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
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(server);
                    var command = await reader.ReadLineAsync(token).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(command))
                        CommandReceived?.Invoke(command);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // 连接中断或管道错误，短暂等待后继续监听
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
        }, token);
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
