using System;

namespace SecRandom.Services.Telemetry;

/// <summary>
/// 遥测事务状态，与具体 SDK 的 span 状态枚举解耦，供非桌面端复用。
/// </summary>
public enum TelemetryTransactionStatus
{
    Ok,
    PermissionDenied,
    InternalError
}

/// <summary>
/// 遥测事务抽象，屏蔽具体 SDK 的 span/transaction 类型。
/// 结束事务优先调用 Finish；仅 Dispose 而未 Finish 时由底层 SDK 决定收尾状态。
/// </summary>
public interface ITelemetryTransaction : IDisposable
{
    void Finish(TelemetryTransactionStatus status);

    void Finish(Exception exception, TelemetryTransactionStatus status);

    /// <summary>
    /// 以成功/失败语义结束事务；等价于 <see cref="Finish(TelemetryTransactionStatus)"/>。
    /// </summary>
    void Finish(bool ok) =>
        Finish(ok ? TelemetryTransactionStatus.Ok : TelemetryTransactionStatus.InternalError);
}
