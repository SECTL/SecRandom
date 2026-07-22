using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SecRandom.Services.Mobile;
using SecRandom.Views.Mobile;
using SecRandom.Views.Mobile.Settings;

namespace SecRandom.Mobile.Tests;

public sealed class MobileSettingsCatalogPageTests
{
    [AvaloniaFact]
    public void CatalogLoadsItsCompiledXaml()
    {
        var page = new MobileSettingsCatalogPage(new TestCapabilities(), new TestSettingsNavigator());

        Assert.NotNull(page.FindControl<Avalonia.Controls.ScrollViewer>("PageScroll"));
    }

    private sealed class TestCapabilities : IMobileCapabilities
    {
        public bool IsLotteryEnabled => true;
        public bool SupportsInAppUpdate => false;
    }

    private sealed class TestSettingsNavigator : IMobileSettingsNavigator
    {
        public bool IsOpen => false;
        public Task OpenAsync(string? pageId = null) => Task.CompletedTask;
        public Task NavigateAsync(string pageId) => Task.CompletedTask;
        public Task CloseAsync() => Task.CompletedTask;
    }
}
