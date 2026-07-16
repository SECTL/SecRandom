using System.Reflection;
using Sentry;
using SecRandom.Services.Telemetry;

namespace SecRandom.Core.Tests;

public class SentryTelemetrySdkAdapterTests
{
    [Fact]
    public void ConfigureOptions_UsesConfiguredDsn()
    {
        SentryOptions options = GetConfiguredOptions();

        Assert.Equal(
            "https://7614b2b2fd46a451e7cb3ed670279e75@o4510689230192640.ingest.us.sentry.io/4511675887910912",
            options.Dsn?.ToString());
    }

    [Fact]
    public void ConfigureOptions_EnablesSdkDebugLogging()
    {
        SentryOptions options = GetConfiguredOptions();

        Assert.True(options.Debug);
    }

    [Fact]
    public void ConfigureOptions_DisablesStructuredLogs()
    {
        SentryOptions options = GetConfiguredOptions();

        Assert.False(options.EnableLogs);
    }

    [Fact]
    public void ConfigureOptions_DoesNotInstallLogFilter()
    {
        SentryOptions options = GetConfiguredOptions();
        PropertyInfo beforeSendLogProperty = GetBeforeSendLogProperty();

        Assert.Null(beforeSendLogProperty.GetValue(options));
    }

    private static SentryOptions GetConfiguredOptions()
    {
        var options = new SentryOptions();
        var configureOptions = typeof(SentryTelemetrySdkAdapter).GetMethod(
            "ConfigureOptions",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(configureOptions);
        configureOptions.Invoke(null, [options, TelemetryPolicySnapshot.From(true)]);
        return options;
    }

    private static PropertyInfo GetBeforeSendLogProperty()
    {
        var beforeSendLogProperty = typeof(SentryOptions).GetProperty(
            "BeforeSendLogInternal",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(beforeSendLogProperty);
        return beforeSendLogProperty;
    }
}
