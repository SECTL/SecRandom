using System.Reflection;
using Sentry;
using SecRandom.Services.Telemetry;

namespace SecRandom.Core.Tests;

public class SentryTelemetrySdkAdapterTests
{
    [Fact]
    public void ConfigureOptions_EnablesStructuredLogs()
    {
        SentryOptions options = GetConfiguredOptions();

        Assert.True(options.EnableLogs);
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
