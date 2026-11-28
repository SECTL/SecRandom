using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Core.Services.FeatureConfiguration;
using SecRandom.Services.ControlNode;
using SecRandom.Services.Ipc;
using SecRandom.Services.FloatingWindow;
using SecRandom.Services.Desktop;
using SecRandom.Services.Plugins;
using SecRandom.Services.Settings;
using SecRandom.Services.Updates;
using SecRandom.Shared.Models.Ipc;
using SecRandom.ViewModels;
using SecRandom.Views;
using SecRandom.Views.SettingsPages.Notification;
using SecRandom.Views.SettingsPages.Picking;
using SecRandom.Views.SettingsPages.More;
using SecRandom.Views.SettingsPages.Personalized;
using SecRandom.Views.SettingsPages.Update;
using SecRandom.Views.SettingsPages.General;

namespace SecRandom.Mobile.Tests;

public sealed class FeatureConfigurationSettingsPageTests
{
    [AvaloniaFact]
    public void CustomFloatingButtonsRespectDisabledFeaturesWithoutChangingConfiguration()
    {
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection().AddSingleton(new FeatureRuntimeSnapshot([])).BuildServiceProvider();
        try
        {
            IAppHost.Host = new TestHost(provider);
            var quick = new FloatingWindowButtonConfig { ActionKind = FloatingWindowButtonActionKind.QuickDraw };
            var timer = new FloatingWindowButtonConfig { ActionKind = FloatingWindowButtonActionKind.Timer };
            var lottery = new FloatingWindowButtonConfig { ActionKind = FloatingWindowButtonActionKind.MainPage, ActionTarget = "main.lottery" };
            var rollCall = new FloatingWindowButtonConfig { ActionKind = FloatingWindowButtonActionKind.MainPage, ActionTarget = "main.rollCall" };
            Assert.False(FloatingWindowButtonActionRunner.IsFeatureAvailable(quick));
            Assert.False(FloatingWindowButtonActionRunner.IsFeatureAvailable(timer));
            Assert.False(FloatingWindowButtonActionRunner.IsFeatureAvailable(lottery));
            Assert.True(FloatingWindowButtonActionRunner.IsFeatureAvailable(rollCall));
            Assert.Equal("main.lottery", lottery.ActionTarget);
        }
        finally
        {
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void SearchExcludesDisabledFeatureSettings()
    {
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection().AddSingleton(new FeatureRuntimeSnapshot([])).BuildServiceProvider();
        try
        {
            IAppHost.Host = new TestHost(provider);
            var method = typeof(SettingsSearchService).GetMethod("IsSearchableSetting",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            bool Searchable(string resource, string page, string setting) =>
                (bool)method.Invoke(null, [resource, page, setting])!;
            Assert.False(Searchable("Picking", "settings.picking.default", "S_AnimationMusic"));
            Assert.False(Searchable("Picking", "settings.picking.default", "S_VoiceAnnouncementEnabled"));
            Assert.False(Searchable("More", "settings.more", "S_LotteryEnabled"));
            Assert.False(Searchable("Update", "settings.update", "S_Strategy_AutoUpdateMode"));
            Assert.True(Searchable("Picking", "settings.picking.default", "S_DrawMode"));
        }
        finally
        {
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void RemoteDrawVisibilityRemainsEditableWhenControlNodeIsDisabled()
    {
        var config = new MainConfigModel();
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var store = new TestControlNodeStore();
        var previousHost = IAppHost.Host;
        var previousDesktop = App.IsDesktop;
        using var provider = new ServiceCollection()
            .AddSingleton(handler)
            .AddSingleton(new ViewModelBase(handler))
            .AddSingleton(new FeatureRuntimeSnapshot([]))
            .AddSingleton<IControlNodeStateStore>(store)
            .BuildServiceProvider();
        try
        {
            App.IsDesktop = true;
            IAppHost.Host = new TestHost(provider);
            var page = new MoreSettingsPage();
            Assert.True(page.IsRemoteDrawControlAvailable);
            page.RemoteDrawPageEnabled = true;
            Assert.True(store.Current.RemoteDrawPageEnabled);
            Assert.True(page.RemoteDrawPageEnabled);
        }
        finally
        {
            IAppHost.Host = previousHost;
            App.IsDesktop = previousDesktop;
        }
    }

    [AvaloniaFact]
    public void DisabledAutomaticUpdatesKeepTheStoredPolicy()
    {
        var config = new MainConfigModel();
        config.UpdateSettings.AutoUpdateMode = 2;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var previousHost = IAppHost.Host;
        using var client = new HttpClient();
        using var provider = new ServiceCollection()
            .AddSingleton(handler)
            .AddSingleton(new ViewModelBase(handler))
            .AddSingleton(new FeatureRuntimeSnapshot([]))
            .AddSingleton(new UpdateCenterService(handler, NullLogger<UpdateCenterService>.Instance, client))
            .BuildServiceProvider();
        Window? window = null;
        try
        {
            IAppHost.Host = new TestHost(provider);
            var page = new UpdateSettingsPage();
            window = new Window { Content = page, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(page.FindControl<Control>("S_Strategy_AutoUpdateMode")!.IsVisible);
            Assert.Equal(2, config.UpdateSettings.AutoUpdateMode);
        }
        finally
        {
            window?.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void DisabledFloatingButtonsKeepTheirStoredVisibility()
    {
        var config = new MainConfigModel();
        config.FloatingWindowSettings.ShowQuickDrawButton = true;
        config.FloatingWindowSettings.ShowLotteryButton = true;
        config.FloatingWindowSettings.ShowTimerButton = true;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection()
            .AddSingleton(handler)
            .AddSingleton(new ViewModelBase(handler))
            .AddSingleton(new FeatureRuntimeSnapshot([]))
            .AddSingleton<IFloatingWindowButtonRegistry, FloatingWindowButtonRegistry>()
            .AddSingleton(new DesktopIntegrationService(handler, NullLogger<DesktopIntegrationService>.Instance))
            .BuildServiceProvider();
        Window? window = null;
        try
        {
            IAppHost.Host = new TestHost(provider);
            var page = new FloatingWindowSettingsPage();
            window = new Window { Content = page, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(page.ButtonOptions);
            page.SelectedButtonOptions.Clear();
            Assert.True(config.FloatingWindowSettings.ShowQuickDrawButton);
            Assert.True(config.FloatingWindowSettings.ShowLotteryButton);
            Assert.True(config.FloatingWindowSettings.ShowTimerButton);
        }
        finally
        {
            window?.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void DisabledDrawFeaturesHideSettingsWithoutChangingStoredChoices()
    {
        var config = new MainConfigModel();
        config.MoreSettings.LotteryEnabled = true;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection()
            .AddSingleton(handler)
            .AddSingleton(new ViewModelBase(handler))
            .AddSingleton(new FeatureRuntimeSnapshot([]))
            .BuildServiceProvider();
        Window? window = null;
        try
        {
            IAppHost.Host = new TestHost(provider);
            var page = new MoreSettingsPage();
            window = new Window { Content = page, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(page.FindControl<Control>("S_LotteryEnabled")!.IsVisible);
            Assert.False(page.FindControl<Control>("S_Shortcut_QuickDraw")!.IsVisible);
            Assert.True(config.MoreSettings.LotteryEnabled);
        }
        finally
        {
            window?.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void DisabledMusicDoesNotOverwriteStoredTracks()
    {
        var config = new MainConfigModel();
        config.DefaultDrawSettings.AnimationMusic = "existing-animation";
        config.DefaultDrawSettings.ResultMusic = "existing-result";
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection().AddSingleton(new FeatureRuntimeSnapshot([])).BuildServiceProvider();
        Window? window = null;
        try
        {
            IAppHost.Host = new TestHost(provider);
            var control = new DrawMusicSettingsExpander { Settings = config.DefaultDrawSettings, MusicSelections = [] };
            window = new Window { Content = control, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(control.IsVisible);
            Assert.Equal("existing-animation", config.DefaultDrawSettings.AnimationMusic);
            Assert.Equal("existing-result", config.DefaultDrawSettings.ResultMusic);
        }
        finally
        {
            window?.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public async Task DisabledQuickDrawProtocolDoesNotResolveDrawViewModel()
    {
        var config = new MainConfigModel();
        config.General.Basic.AutomationIpcEnabled = true;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var availability = new DisabledFeatures();
        var router = new ProtocolCommandRouter(handler, null!,
            () => throw new InvalidOperationException("Lottery should remain unloaded"),
            () => throw new InvalidOperationException("Quick draw should remain unloaded"),
            null!, null!, availability, new PageRouteResolver(availability), new FeatureRuntimeSnapshot([]));
        var response = await router.HandleIpcAsync(
            new IpcRequestEnvelope(1, "request", new IpcRequestPayload("roll_call/quick_draw")),
            CancellationToken.None);
        Assert.True(response.Success);
        Assert.Equal("error", response.Result?.Status);
        Assert.Equal("feature_disabled", response.Result?.Code);
    }

    [AvaloniaFact]
    public void DisabledFullscreenChannelDoesNotOverwriteStoredSelection()
    {
        var config = new MainConfigModel();
        config.NotificationSettings.Default.NotificationServiceType = 3;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection()
            .AddSingleton(handler)
            .AddSingleton(new ViewModelBase(handler))
            .AddSingleton(new FeatureRuntimeSnapshot([StartupFeatureCatalog.Notifications]))
            .BuildServiceProvider();
        Window? window = null;
        try
        {
            IAppHost.Host = new TestHost(provider);
            var page = new DefaultNotificationSettingsPage();
            window = new Window { Content = page, Width = 800, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(page.NotificationServiceOptions, option => option.Value == 3);
            Assert.Equal(3, config.NotificationSettings.Default.NotificationServiceType);
        }
        finally
        {
            window?.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public async Task DisabledRemoteDrawsAreRejectedWithoutCreatingDrawViewModels()
    {
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection()
            .AddSingleton(new FeatureRuntimeSnapshot([]))
            .AddSingleton<IFeatureAvailabilityService, DisabledFeatures>()
            .BuildServiceProvider();
        try
        {
            IAppHost.Host = new TestHost(provider);
            var executor = new ControlPageDrawExecutor();
            var quick = await executor.DrawQuickAsync(CancellationToken.None);
            var lottery = await executor.DrawLotteryAsync(
                new ControlDrawTriggerRequest(ControlDrawTriggerRequest.TargetLottery, null, null, null, null),
                CancellationToken.None);
            Assert.False(quick.Succeeded);
            Assert.Equal("feature_disabled", quick.Reason);
            Assert.False(lottery.Succeeded);
            Assert.Equal("draw_denied", lottery.Reason);
        }
        finally
        {
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void FeatureSwitchCanBeChangedAndDiscardedWithoutSaving()
    {
        var config = new MainConfigModel();
        var store = new TestConfigService(config);
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, store);
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        IAppHost.Host = new TestHost(provider);
        var page = new FeatureConfigurationSettingsPage();
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var categories = page.FindControl<TabControl>("CategoryTabs")!;
            categories.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            var toggle = window.GetVisualDescendants().OfType<ToggleSwitch>()
                .Single(item => Equals(item.Tag, StartupFeatureCatalog.Lottery));
            var status = page.FindControl<TextBlock>("StatusText")!;
            var originalStatus = status.Text;
            toggle.IsChecked = false;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(originalStatus, status.Text);
            Assert.Equal(0, store.SaveCount);

            page.FindControl<Button>("DiscardButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(originalStatus, status.Text);
            Assert.True(window.GetVisualDescendants().OfType<ToggleSwitch>()
                .Single(item => Equals(item.Tag, StartupFeatureCatalog.Lottery)).IsChecked);
        }
        finally
        {
            window.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void RequiredCapabilitiesAreVisibleWithoutEditableSwitches()
    {
        var config = new MainConfigModel();
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        IAppHost.Host = new TestHost(provider);
        var page = new FeatureConfigurationSettingsPage();
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var requiredIds = StartupFeatureCatalog.Graph.Features.Where(feature => feature.Required)
                .Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
            var toggles = window.GetVisualDescendants().OfType<ToggleSwitch>()
                .Select(toggle => toggle.Tag as string).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
            Assert.Empty(requiredIds.Intersect(toggles));
            Assert.Contains(StartupFeatureCatalog.Graph.Features.Count.ToString(),
                page.FindControl<TextBlock>("CountText")!.Text);
        }
        finally
        {
            window.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void ProfileSelectionCanBeChangedRepeatedlyWithoutResettingTheSelectionSource()
    {
        var config = new MainConfigModel();
        config.General.SimpleMode.SelectedProfileId = "user.first";
        config.General.SimpleMode.Profiles =
        [
            new FeatureProfile { Id = "user.first", Name = "First", EnabledFeatures = [StartupFeatureCatalog.Lottery] },
            new FeatureProfile { Id = "user.second", Name = "Second", EnabledFeatures = [StartupFeatureCatalog.QuickDraw] }
        ];
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        IAppHost.Host = new TestHost(provider);
        var page = new FeatureConfigurationSettingsPage();
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var selector = page.FindControl<ComboBox>("ProfileSelector")!;
            var profiles = selector.ItemsSource!.Cast<ComboBoxItem>().ToArray();

            selector.SelectedItem = profiles.Single(item => Equals(item.Tag, "user.second"));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Second", Convert.ToString(page.FindControl<FASettingsExpander>("ProfileExpander")!.Header));

            selector.SelectedItem = profiles.Single(item => Equals(item.Tag, "user.first"));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("First", Convert.ToString(page.FindControl<FASettingsExpander>("ProfileExpander")!.Header));
        }
        finally
        {
            window.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void OverviewDisplaysEveryCatalogCapabilityAsAStandardSettingsExpander()
    {
        var config = new MainConfigModel();
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        IAppHost.Host = new TestHost(provider);
        var page = new FeatureConfigurationSettingsPage();
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var cards = page.FindControl<StackPanel>("PageContent")!.Children.OfType<FASettingsExpander>().ToArray();
            Assert.Equal(StartupFeatureCatalog.Graph.Features.Count, cards.Length);
            Assert.Equal(StartupFeatureCatalog.Graph.Features.Count(feature => !feature.Required),
                cards.SelectMany(card => (card.Footer as StackPanel)?.Children.OfType<ToggleSwitch>() ?? [])
                    .Count(toggle => toggle.Tag is string));
        }
        finally
        {
            window.Close();
            IAppHost.Host = previousHost;
        }
    }

    [AvaloniaFact]
    public void FeatureConfigurationUsesItsOwnFluentWindow()
    {
        var config = new MainConfigModel();
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var previousHost = IAppHost.Host;
        using var provider = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
        IAppHost.Host = new TestHost(provider);
        var window = new FeatureConfigurationWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.IsAssignableFrom<FAAppWindow>(window);
            Assert.Same(window.ConfigurationPage, window.FindControl<ContentControl>("ContentHost")!.Content);
            Assert.Equal(FeatureConfigurationText.Get("Title"), window.Title);
        }
        finally
        {
            window.Close();
            IAppHost.Host = previousHost;
        }
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public int SaveCount { get; private set; }
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) => SaveCount++;
        public override void DeleteConfig<T>(T value) { }
    }

    private sealed class DisabledFeatures : IFeatureAvailabilityService
    {
        public bool IsSimpleModeEnabled => false;
        public bool IsLotteryEnabled => false;
        public event EventHandler? Changed { add { } remove { } }
        public void Refresh() { }
    }

    private sealed class TestControlNodeStore : IControlNodeStateStore
    {
        public ControlNodeState Current { get; private set; } = new();
        public event EventHandler<ControlNodeState>? Changed;
        public void Update(Func<ControlNodeState, ControlNodeState> mutate)
        {
            Current = mutate(Current);
            Changed?.Invoke(this, Current);
        }
    }

    private sealed class TestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
