using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Views;
using SecRandom.Mobile.Views.Settings;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Mobile.Views;

public sealed class MobileShellView : ViewBase
{
    public const string Id = "mobile.shell";

    private readonly IProfileService _profileService;
    private readonly IHistoryQueryService _historyQueryService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly IFeatureAvailabilityService _featureAvailabilityService;
    private readonly MainConfigHandler _configHandler;
    private readonly DrawEngine _drawEngine;
    private readonly MobileUpdateService _updateService;
    private readonly EventHandler _featureAvailabilityChanged;
    private Grid _pageHost = null!;
    private TextBlock _pageTitle = null!;
    private AvaloniaButton _drawTab = null!;
    private AvaloniaButton _historyTab = null!;
    private AvaloniaButton _overviewTab = null!;
    private AvaloniaButton _settingsTab = null!;
    private MobileDestination _destination = MobileDestination.Draw;
    private DrawSurface _drawSurface = DrawSurface.RollCall;
    private MobileSettingsSection? _settingsSection;

    public MobileShellView(
        IProfileService profileService,
        IHistoryQueryService historyQueryService,
        IDrawTemporaryRecordService temporaryRecordService,
        IFeatureAvailabilityService featureAvailabilityService,
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        MobileUpdateService updateService)
    {
        _profileService = profileService;
        _historyQueryService = historyQueryService;
        _temporaryRecordService = temporaryRecordService;
        _featureAvailabilityService = featureAvailabilityService;
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _updateService = updateService;
        MobileTheme.Apply(_configHandler.Data.Appearance.Theme);

        _featureAvailabilityChanged = (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_featureAvailabilityService.IsLotteryEnabled && _drawSurface == DrawSurface.Lottery)
                _drawSurface = DrawSurface.RollCall;
            if (_destination == MobileDestination.Draw)
                RenderCurrentDestination();
        });
        _featureAvailabilityService.Changed += _featureAvailabilityChanged;
        Closed += (_, _) => _featureAvailabilityService.Changed -= _featureAvailabilityChanged;

        BuildShellLayout();
        RenderCurrentDestination();
    }

    private void BuildShellLayout()
    {
        _pageTitle = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = MobileTheme.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        _pageHost = new Grid();
        _drawTab = MobileUi.CreateNavigationButton(LR.N_Draw);
        _historyTab = MobileUi.CreateNavigationButton(LR.N_History);
        _overviewTab = MobileUi.CreateNavigationButton(LR.N_Overview);
        _settingsTab = MobileUi.CreateNavigationButton(LR.N_Settings);
        _drawTab.Click += (_, _) => NavigateTo(MobileDestination.Draw);
        _historyTab.Click += (_, _) => NavigateTo(MobileDestination.History);
        _overviewTab.Click += (_, _) => NavigateTo(MobileDestination.Overview);
        _settingsTab.Click += (_, _) => NavigateTo(MobileDestination.Settings);

        var header = new Border
        {
            Padding = new Thickness(20, 14),
            Background = MobileTheme.Surface,
            BorderBrush = MobileTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Image
                    {
                        Source = new Bitmap(AssetLoader.Open(new Uri("avares://SecRandom.Mobile/Assets/AppLogo.png"))),
                        Width = 32,
                        Height = 32,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    _pageTitle
                }
            }
        };
        Grid.SetColumn(_pageTitle, 1);

        var bottomBar = new Border
        {
            Padding = new Thickness(8, 8, 8, 10),
            Background = MobileTheme.Surface,
            BorderBrush = MobileTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
                Children = { _drawTab, _historyTab, _overviewTab, _settingsTab }
            }
        };
        Grid.SetColumn(_historyTab, 1);
        Grid.SetColumn(_overviewTab, 2);
        Grid.SetColumn(_settingsTab, 3);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Background = MobileTheme.Canvas,
            Children = { header, _pageHost, bottomBar }
        };
        Grid.SetRow(_pageHost, 1);
        Grid.SetRow(bottomBar, 2);
        Content = root;
    }

    private void NavigateTo(MobileDestination destination)
    {
        _destination = destination;
        _settingsSection = null;
        RenderCurrentDestination();
    }

    private void OpenSettings(MobileSettingsSection section)
    {
        _destination = MobileDestination.Settings;
        _settingsSection = section;
        RenderCurrentDestination();
    }

    private void SelectDrawSurface(DrawSurface surface)
    {
        if (surface == DrawSurface.Lottery && !_featureAvailabilityService.IsLotteryEnabled)
            return;

        _drawSurface = surface;
        RenderCurrentDestination();
    }

    private void ApplyTheme(ThemeMode theme)
    {
        _configHandler.Data.Appearance.Theme = theme;
        _configHandler.Save();
        MobileTheme.Apply(theme);
        BuildShellLayout();
        RenderCurrentDestination();
    }

    private void RenderCurrentDestination()
    {
        _drawTab.Foreground = _destination == MobileDestination.Draw ? MobileTheme.Primary : MobileTheme.MutedText;
        _historyTab.Foreground = _destination == MobileDestination.History ? MobileTheme.Primary : MobileTheme.MutedText;
        _overviewTab.Foreground = _destination == MobileDestination.Overview ? MobileTheme.Primary : MobileTheme.MutedText;
        _settingsTab.Foreground = _destination == MobileDestination.Settings ? MobileTheme.Primary : MobileTheme.MutedText;
        _pageTitle.Text = _destination switch
        {
            MobileDestination.Draw => LR.P_Draw,
            MobileDestination.History => LR.P_History,
            MobileDestination.Overview => LR.P_Overview,
            MobileDestination.Settings => LR.P_Settings,
            _ => throw new ArgumentOutOfRangeException()
        };

        _pageHost.Children.Clear();
        _pageHost.Children.Add(_destination switch
        {
            MobileDestination.Draw => new MobileDrawPage(
                _profileService,
                _temporaryRecordService,
                _featureAvailabilityService,
                _configHandler,
                _drawEngine,
                _drawSurface,
                SelectDrawSurface,
                () => OpenSettings(MobileSettingsSection.ListManagement)),
            MobileDestination.History => new MobileHistoryPage(
            _profileService,
                _historyQueryService,
                _temporaryRecordService),
            MobileDestination.Overview => new MobileOverviewPage(_profileService),
            MobileDestination.Settings => CreateSettingsPage(),
            _ => throw new ArgumentOutOfRangeException()
        });
    }

    private Control CreateSettingsPage()
    {
        return _settingsSection switch
        {
            null => new MobileSettingsCatalogPage(OpenSettings),
            MobileSettingsSection.General => new MobileGeneralSettingsPage(_configHandler, ReturnToSettingsCatalog),
            MobileSettingsSection.Personalization => new MobilePersonalizationSettingsPage(
                _configHandler, ReturnToSettingsCatalog, ApplyTheme),
            MobileSettingsSection.ListManagement => new MobileListManagementSettingsPage(
                _profileService, ReturnToSettingsCatalog, RenderCurrentDestination),
            MobileSettingsSection.Draw => new MobileDrawSettingsPage(
                _configHandler, _temporaryRecordService, _profileService, ReturnToSettingsCatalog, RenderCurrentDestination),
            MobileSettingsSection.Backup => new MobileBackupSettingsPage(ReturnToSettingsCatalog),
            MobileSettingsSection.Update => new MobileUpdateSettingsPage(
                _updateService, ReturnToSettingsCatalog, RenderCurrentDestination),
            MobileSettingsSection.About => new MobileAboutSettingsPage(ReturnToSettingsCatalog),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private void ReturnToSettingsCatalog()
    {
        _settingsSection = null;
        RenderCurrentDestination();
    }
}

internal enum MobileDestination
{
    Draw,
    History,
    Overview,
    Settings
}

internal enum DrawSurface
{
    RollCall,
    Lottery
}

internal enum MobileSettingsSection
{
    General,
    Personalization,
    ListManagement,
    Draw,
    Backup,
    Update,
    About
}
