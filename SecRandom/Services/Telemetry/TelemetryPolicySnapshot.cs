using SecRandom.Core;

namespace SecRandom.Services.Telemetry;

/// <summary>
/// Sentry 遥测策略快照，控制遥测 SDK 的初始化和上传行为。
/// </summary>
public sealed record TelemetryPolicySnapshot(
    bool IsEnabled,
    bool ShouldInitializeSdk,
    bool ShouldUploadTelemetry,
    bool SendDefaultPii,
    bool EnableTraces,
    bool EnableProfiles)
{
    /// <summary>
    /// 根据启用状态创建策略快照。
    /// </summary>
    public static TelemetryPolicySnapshot From(bool isEnabled)
    {
        return isEnabled ? Enabled : Disabled;
    }

    /// <summary>
    /// 追踪采样率：开发环境 100%，生产环境 20%。
    /// 仅设置采样率不会自动创建事务，需在业务代码中显式调用 StartTransaction。
    /// </summary>
    public double TracesSampleRate => EnableTraces
        ? (GlobalConstants.IsDevelopment ? 1.0 : 0.2)
        : 0.0;

    /// <summary>
    /// 性能分析采样率：基于已采样的事务再采样。
    /// 例如 TracesSampleRate=0.2 且 ProfilesSampleRate=0.2，则实际 profile 率为 4%。
    /// </summary>
    public double ProfilesSampleRate => EnableProfiles
        ? (GlobalConstants.IsDevelopment ? 1.0 : 0.2)
        : 0.0;

    private static TelemetryPolicySnapshot Enabled => new(
        IsEnabled: true,
        ShouldInitializeSdk: true,
        ShouldUploadTelemetry: true,
        SendDefaultPii: false,
        EnableTraces: true,
        EnableProfiles: true);

    private static TelemetryPolicySnapshot Disabled => new(
        IsEnabled: false,
        ShouldInitializeSdk: false,
        ShouldUploadTelemetry: false,
        SendDefaultPii: false,
        EnableTraces: false,
        EnableProfiles: false);
}
