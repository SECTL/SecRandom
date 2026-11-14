using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Ipc;
using SecRandom.Core.Services.SingleInstance;
using SecRandom.Shared.Models.Ipc;

namespace SecRandom.Services.Ipc;

/// <summary>
///     v2 request side of the automation pipe. It subscribes to
///     <see cref="SingleInstanceService.AutomationRequestReceived" /> once the Host starts and
///     answers exactly one JSON line per frame.
/// </summary>
public sealed class AutomationIpcServer(
    SingleInstanceService singleInstance,
    AutomationCommandRegistry registry,
    AutomationEventBus events,
    MainConfigHandler config,
    ILogger<AutomationIpcServer> logger) : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string MethodsDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join('\n', AutomationCommandCatalog.Methods.Select(entry => entry.Name)))))
        .ToLowerInvariant()[..16];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        singleInstance.AutomationRequestReceived += HandleFrameAsync;
        logger.LogInformation(
            "IPC v2 自动化管道已挂载：{Count} 个方法，事件管道 {EventPipe}。",
            AutomationCommandCatalog.Methods.Count,
            SingleInstanceService.EventPipeName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        singleInstance.AutomationRequestReceived -= HandleFrameAsync;
        return Task.CompletedTask;
    }

    /// <summary>处理一帧 v2 JSON；永不抛异常，失败也折叠成一行响应。</summary>
    public async Task<string> HandleFrameAsync(string frame, CancellationToken cancellationToken)
    {
        if (!IpcV2RequestEnvelope.TryRead(frame, out var request, out var parseError))
        {
            return Serialize(IpcV2ResponseEnvelope.Failure(null, parseError!));
        }

        var id = request!.Id;
        if (request.Type == "hello")
        {
            return Serialize(new IpcV2WelcomeEnvelope(
                IpcV2RequestEnvelope.CurrentVersion,
                "welcome",
                IpcV2RequestEnvelope.CurrentVersion,
                GlobalConstants.Version,
                MethodsDigest));
        }

        if (!config.Data.General.Basic.AutomationIpcEnabled)
        {
            return Serialize(IpcV2ResponseEnvelope.Failure(id, IpcV2ErrorEnvelope.Create(
                AutomationErrorCodes.IpcDisabled,
                "自动化 IPC 已在设置中关闭。")));
        }

        if (Application.Current is App { IsOobeActive: true })
        {
            return Serialize(IpcV2ResponseEnvelope.Failure(id, IpcV2ErrorEnvelope.Create(
                AutomationErrorCodes.OobeRequired,
                "初始设置尚未完成。",
                retryable: true)));
        }

        try
        {
            var result = await registry
                .InvokeAsync(request.Method!, request.Params, cancellationToken)
                .ConfigureAwait(false);

            return Serialize(IpcV2ResponseEnvelope.Success(id, result));
        }
        catch (AutomationRpcException exception)
        {
            return Serialize(IpcV2ResponseEnvelope.Failure(id, IpcV2ErrorEnvelope.Create(
                exception.Code,
                exception.Message,
                exception.Retryable,
                exception.Details)));
        }
        catch (OperationCanceledException)
        {
            return Serialize(IpcV2ResponseEnvelope.Failure(id, IpcV2ErrorEnvelope.Create(
                AutomationErrorCodes.Timeout,
                "请求已取消或超时。",
                retryable: true)));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "IPC v2 请求 {Method} 处理失败。", request.Method);
            return Serialize(IpcV2ResponseEnvelope.Failure(id, IpcV2ErrorEnvelope.Create(
                AutomationErrorCodes.InternalError,
                "命令执行失败，详情见应用日志。")));
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}