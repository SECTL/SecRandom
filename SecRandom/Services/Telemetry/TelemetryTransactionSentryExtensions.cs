using System;
using Sentry;

namespace SecRandom.Services.Telemetry;

/// <summary>
/// 桌面兼容扩展：既有调用点（如 App.axaml.cs 的窗口事务）仍使用 Sentry 的 SpanStatus 结束抽象事务。
/// 新代码应直接调用 ITelemetryTransaction.Finish(TelemetryTransactionStatus)，移动端不应包含本文件。
/// </summary>
public static class TelemetryTransactionSentryExtensions
{
    public static void Finish(this ITelemetryTransaction transaction, SpanStatus status)
    {
        transaction.Finish(ToStatus(status));
    }

    public static void Finish(this ITelemetryTransaction transaction, Exception exception, SpanStatus status)
    {
        transaction.Finish(exception, ToStatus(status));
    }

    private static TelemetryTransactionStatus ToStatus(SpanStatus status)
    {
        if (status.Equals(SpanStatus.Ok))
            return TelemetryTransactionStatus.Ok;

        if (status.Equals(SpanStatus.PermissionDenied))
            return TelemetryTransactionStatus.PermissionDenied;

        return TelemetryTransactionStatus.InternalError;
    }
}
