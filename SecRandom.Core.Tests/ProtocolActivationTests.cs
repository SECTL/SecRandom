using System.Reflection;
using SecRandom.Services.Desktop;
using SecRandom.Services.Ipc;

namespace SecRandom.Core.Tests;

[Collection("ProtocolActivation")]
public class ProtocolActivationTests
{
    [Fact]
    public void StartupArguments_ExtractsUrlArgument()
    {
        ProtocolActivation.SetStartupArguments(["--url", "secrandom://window/settings"]);

        Assert.Equal("secrandom://window/settings", ProtocolActivation.ConsumeStartupUri());
    }

    [Fact]
    public void StartupArguments_ExtractsDirectProtocolUri()
    {
        ProtocolActivation.SetStartupArguments(["secrandom://window/main"]);

        Assert.Equal("secrandom://window/main", ProtocolActivation.ConsumeStartupUri());
    }

    [Fact]
    public void StartupArguments_RejectsOtherUriSchemes()
    {
        ProtocolActivation.SetStartupArguments(["https://example.com"]);

        Assert.Null(ProtocolActivation.ConsumeStartupUri());
    }

    [Fact]
    public void StartupUri_IsConsumedOnlyOnce()
    {
        ProtocolActivation.SetStartupArguments(["secrandom://window/float"]);

        Assert.NotNull(ProtocolActivation.ConsumeStartupUri());
        Assert.Null(ProtocolActivation.ConsumeStartupUri());
    }

    [Fact]
    public void LegacyNotificationSettingsRouteTargetsRegisteredDefaultPage()
    {
        var field = typeof(ProtocolCommandRouter).GetField(
            "SettingsPages",
            BindingFlags.NonPublic | BindingFlags.Static);
        var pages = Assert.IsType<Dictionary<string, string>>(field?.GetValue(null));

        Assert.Equal("settings.notification.default", pages["notificationsettingsinterface"]);
    }
}
