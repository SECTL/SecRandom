namespace SecRandom.Services.Telemetry;

public sealed record TelemetryPolicySnapshot(
    bool IsEnabled,
    bool ShouldInitializeSdk,
    bool ShouldUploadTelemetry,
    bool SendDefaultPii,
    bool EnableLogs,
    bool EnableTraces,
    bool EnableProfiles)
{
    public static TelemetryPolicySnapshot From(bool isEnabled)
    {
        return isEnabled ? Enabled : Disabled;
    }

    public double TracesSampleRate => EnableTraces ? 0.2 : 0.0;

    public double ProfilesSampleRate => EnableProfiles ? 0.2 : 0.0;

    private static TelemetryPolicySnapshot Enabled => new(
        IsEnabled: true,
        ShouldInitializeSdk: true,
        ShouldUploadTelemetry: true,
        SendDefaultPii: false,
        EnableLogs: true,
        EnableTraces: true,
        EnableProfiles: true);

    private static TelemetryPolicySnapshot Disabled => new(
        IsEnabled: false,
        ShouldInitializeSdk: false,
        ShouldUploadTelemetry: false,
        SendDefaultPii: false,
        EnableLogs: false,
        EnableTraces: false,
        EnableProfiles: false);
}
