using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;
using SecRandom.Views;

namespace SecRandom.Mobile.Tests;

/// <summary>
/// The runtime lottery capability may only hide/show the existing sidebar entry. Rebuilding the
/// footer items source after an already-selected sidebar entry was used makes FluentAvalonia's
/// FANavigationView drop its footer selection and invoke a null item, which throws a
/// NullReferenceException (v3.0.0 crash report: FANavigationView.RaiseItemInvoked).
/// </summary>
public sealed class MainViewLotteryNavigationTests
{
    [AvaloniaFact]
    public void TogglingLotteryAvailabilityKeepsNavigationStable()
    {
        var availability = new FakeFeatureAvailabilityService();
        using var harness = new MainViewHarness(availability);

        var view = harness.View;
        var navigation = Assert.IsType<FANavigationView>(view.FindControl<FANavigationView>("NavigationView"));

        var selectedItem = Assert.IsType<FANavigationViewItem>(view.ViewModel.SelectedNavigationViewItem);
        Assert.Same(MainViewHarness.RollCall, selectedItem.Tag);

        var lotteryItem = Assert.Single(
            view.ViewModel.NavigationViewFooterItems.OfType<FANavigationViewItem>(),
            item => item.Tag is PageInfo { Id: "main.lottery" });
        var settingsItem = Assert.Single(
            view.ViewModel.NavigationViewFooterItems.OfType<FANavigationViewItem>(),
            item => item.Tag is PageInfo { Id: "settings" });
        Assert.False(lotteryItem.IsVisible);

        // Invoking an already-selected entry (the settings entry behaves the same way, because it
        // does not select itself) leaves FluentAvalonia's pending-invoke flag set, so the footer
        // items must not be rebuilt afterwards.
        ActivateSelectedEntry(selectedItem);
        Assert.Same(selectedItem, navigation.SelectedItem);

        availability.SetLotteryEnabled(true);
        Dispatcher.UIThread.RunJobs();

        Assert.True(lotteryItem.IsVisible);
        Assert.True(lotteryItem.IsEffectivelyVisible);
        Assert.Same(selectedItem, view.ViewModel.SelectedNavigationViewItem);
        Assert.Same(selectedItem, navigation.SelectedItem);

        var settingsTopWithLottery = settingsItem.Bounds.Y;

        availability.SetLotteryEnabled(false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(lotteryItem.IsVisible);
        Assert.False(lotteryItem.IsEffectivelyVisible);
        // The hidden entry must not keep its slot: the entries after it move up.
        Assert.True(settingsItem.Bounds.Y < settingsTopWithLottery);
        Assert.Same(selectedItem, view.ViewModel.SelectedNavigationViewItem);
        Assert.Same(selectedItem, navigation.SelectedItem);
    }

    [AvaloniaFact]
    public void DisablingLotteryLeavesTheLotteryPage()
    {
        var availability = new FakeFeatureAvailabilityService { IsLotteryEnabled = true };
        using var harness = new MainViewHarness(availability);

        var view = harness.View;
        view.SelectNavigationItemById("main.lottery");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("main.lottery", view.ViewModel.SelectedPageInfo?.Id);

        availability.SetLotteryEnabled(false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("main.rollCall", view.ViewModel.SelectedPageInfo?.Id);
        Assert.Same(MainViewHarness.RollCall, ((FANavigationViewItem)view.ViewModel.SelectedNavigationViewItem!).Tag);
    }

    private static void ActivateSelectedEntry(FANavigationViewItem item)
    {
        item.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class MainViewHarness : IDisposable
    {
        public static readonly PageInfo RollCall =
            new("main.rollCall", FluentIcons.PeopleFilled, location: PageLocation.Bottom) { Name = "Roll call" };

        public static readonly PageInfo Lottery =
            new("main.lottery", FluentIcons.GiftFilled, location: PageLocation.Bottom) { Name = "Lottery" };

        private readonly ServiceProvider _provider;
        private readonly IHost? _previousHost;
        private readonly List<PageInfo> _previousItems;
        private readonly bool _previousIsDesktop;

        public MainViewHarness(FakeFeatureAvailabilityService availability)
        {
            var configHandler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new FakeConfigService());

            var services = new ServiceCollection();
            services.AddSingleton<IFeatureAvailabilityService>(availability);
            services.AddSingleton(new MainViewModel(configHandler));
            services.AddKeyedSingleton<UserControl>("main.rollCall", new UserControl());
            services.AddKeyedSingleton<UserControl>("main.lottery", new UserControl());
            _provider = services.BuildServiceProvider();

            _previousHost = IAppHost.Host;
            _previousItems = PagesRegistryService.MainItems.ToList();
            _previousIsDesktop = App.IsDesktop;

            IAppHost.Host = new TestHost(_provider);
            App.IsDesktop = true;

            PagesRegistryService.MainItems.Clear();
            PagesRegistryService.MainItems.Add(RollCall);
            PagesRegistryService.MainItems.Add(Lottery);

            View = new MainView();
            Window = new Window
            {
                Width = 900,
                Height = 600,
                Content = View
            };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public MainView View { get; }
        public Window Window { get; }

        public void Dispose()
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();

            App.IsDesktop = _previousIsDesktop;
            PagesRegistryService.MainItems.Clear();
            foreach (var info in _previousItems)
                PagesRegistryService.MainItems.Add(info);
            IAppHost.Host = _previousHost;
            _provider.Dispose();
        }
    }

    private sealed class FakeFeatureAvailabilityService : IFeatureAvailabilityService
    {
        public bool IsLotteryEnabled { get; set; }
        public event EventHandler? Changed;

        public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

        public void SetLotteryEnabled(bool enabled)
        {
            IsLotteryEnabled = enabled;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeConfigService : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => false;
        public override T LoadConfig<T>(T fallback) => fallback;
        public override void SaveConfig<T>(T config) { }
        public override void DeleteConfig<T>(T config) { }
    }

    private sealed class TestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
