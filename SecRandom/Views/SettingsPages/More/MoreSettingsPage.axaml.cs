using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Stats;
using MoreResources = SecRandom.Langs.SettingsPages.More.Resources;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Core.Services.FeatureConfiguration;
using SecRandom.Mobile;
using SecRandom.Platforms.Abstractions;
using SecRandom.ViewModels;
using SecRandom.Views.Mobile;

namespace SecRandom.Views.SettingsPages.More;

[PageInfo("settings.more", FluentIcons.MoreHorizontalFilled)]
public partial class MoreSettingsPage : UserControl, INotifyPropertyChanged
{
    private enum OverviewClearKind
    {
        All,
        RollCall,
        Lottery,
        RandomNumbers
    }

    private sealed record OverviewClearTarget(OverviewClearKind Kind, string? ProfileName = null);

    private static readonly string[] ShortcutConflictPropertyNames =
    [
        nameof(IsOpenRollCallPageShortcutConflicted),
        nameof(IsQuickDrawShortcutConflicted),
        nameof(IsOpenLotteryPageShortcutConflicted),
        nameof(IsIncreaseRollCallCountShortcutConflicted),
        nameof(IsDecreaseRollCallCountShortcutConflicted),
        nameof(IsIncreaseLotteryCountShortcutConflicted),
        nameof(IsDecreaseLotteryCountShortcutConflicted),
        nameof(IsStartRollCallShortcutConflicted),
        nameof(IsStartLotteryShortcutConflicted)
    ];

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public MoreSettingsPage()
    {
        Settings = ConfigHandler.Data.MoreSettings;
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        ConfigHandler.Reloaded += ConfigHandlerOnReloaded;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public MoreSettingsConfig Settings { get; private set; }
    public bool IsLotteryFeatureAvailable => IAppHost.TryGetService<FeatureRuntimeSnapshot>()
        ?.IsEnabled(StartupFeatureCatalog.Lottery) != false;
    public bool IsLotteryAvailable => IsLotteryFeatureAvailable && Settings.LotteryEnabled;
    public bool IsQuickDrawAvailable => IAppHost.TryGetService<FeatureRuntimeSnapshot>()
        ?.IsEnabled(StartupFeatureCatalog.QuickDraw) != false;
    public bool IsRemoteDrawControlAvailable => IsDesktop && IAppHost.TryGetService<FeatureRuntimeSnapshot>()
        ?.IsEnabled(StartupFeatureCatalog.ControlNode) == false;
    public bool RemoteDrawPageEnabled
    {
        get => IsRemoteDrawControlAvailable && IAppHost.TryGetService<IControlNodeStateStore>()?.Current.RemoteDrawPageEnabled == true;
        set
        {
            if (!IsRemoteDrawControlAvailable || IAppHost.TryGetService<IControlNodeStateStore>() is not { } store
                || store.Current.RemoteDrawPageEnabled == value)
                return;
            store.Update(state => state with { RemoteDrawPageEnabled = value });
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemoteDrawPageEnabled)));
        }
    }
    public bool IsOpenRollCallPageShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.OpenRollCallPageShortcut));
    public bool IsQuickDrawShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.QuickDrawShortcut));
    public bool IsOpenLotteryPageShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.OpenLotteryPageShortcut));
    public bool IsIncreaseRollCallCountShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.IncreaseRollCallCountShortcut));
    public bool IsDecreaseRollCallCountShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.DecreaseRollCallCountShortcut));
    public bool IsIncreaseLotteryCountShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.IncreaseLotteryCountShortcut));
    public bool IsDecreaseLotteryCountShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.DecreaseLotteryCountShortcut));
    public bool IsStartRollCallShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.StartRollCallShortcut));
    public bool IsStartLotteryShortcutConflicted => IsShortcutConflicted(nameof(MoreSettingsConfig.StartLotteryShortcut));
    public bool IsDesktop => App.IsDesktop;
    public bool UseDesktopUI =>
        (IAppHost.TryGetService<IPlatformServiceRoot>() as MobilePlatformServiceRoot)?.UsesDesktopMainView ?? IsDesktop;

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        ConfigHandler.Reloaded -= ConfigHandlerOnReloaded;
    }

    private void ConfigHandlerOnReloaded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Settings.PropertyChanged -= SettingsOnPropertyChanged;
            Settings = ConfigHandler.Data.MoreSettings;
            Settings.PropertyChanged += SettingsOnPropertyChanged;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Settings)));
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLotteryAvailable)));
            foreach (var propertyName in ShortcutConflictPropertyNames)
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        });
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, ConfigHandler.Data.MoreSettings))
            return;

        ConfigHandler.Save();
        if (e.PropertyName == nameof(MoreSettingsConfig.LotteryEnabled))
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLotteryAvailable)));
        foreach (var propertyName in ShortcutConflictPropertyNames)
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void ShortcutBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string propertyName })
            return;

        if (e.Key == Key.Escape)
        {
            EndShortcutInput();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            SetShortcut(propertyName, string.Empty);
            EndShortcutInput();
            e.Handled = true;
            return;
        }

        if (!TryFormatShortcut(e, out var shortcut))
            return;

        SetShortcut(propertyName, shortcut);
        EndShortcutInput();
        e.Handled = true;
    }

    private void ClearShortcutButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string propertyName })
        {
            SetShortcut(propertyName, string.Empty);
            EndShortcutInput();
        }
    }

    private void OverviewMenuFlyout_OnOpened(object? sender, EventArgs e)
    {
        PopulateOverviewMenu(M_ClearOverviewRollCall, OverviewClearKind.RollCall,
            IAppHost.GetService<IProfileCatalogManager>().GetStudentListNames());
        PopulateOverviewMenu(M_ClearOverviewLottery, OverviewClearKind.Lottery,
            IAppHost.GetService<IProfileCatalogManager>().GetPrizeListNames());
        PopulateOverviewMenu(M_ClearOverviewRandomNumbers, OverviewClearKind.RandomNumbers, []);
    }

    private void PopulateOverviewMenu(MenuItem parent, OverviewClearKind kind,
        IEnumerable<string> profileNames)
    {
        parent.Items.Clear();
        parent.Items.Add(CreateOverviewMenuItem(MoreResources.C_ClearOverview_All,
            new OverviewClearTarget(kind)));
        foreach (var profileName in profileNames)
        {
            if (!string.IsNullOrWhiteSpace(profileName))
                parent.Items.Add(CreateOverviewMenuItem(profileName,
                    new OverviewClearTarget(kind, profileName)));
        }
    }

    private MenuItem CreateOverviewMenuItem(string header, OverviewClearTarget target)
    {
        var item = new MenuItem
        {
            Header = header,
            Tag = target
        };
        item.Click += ClearOverviewMenuItem_OnClick;
        return item;
    }

    private async void ClearOverviewMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        var target = sender switch
        {
            MenuItem { Tag: string tag } when string.Equals(tag, "all", StringComparison.Ordinal) =>
                new OverviewClearTarget(OverviewClearKind.All),
            MenuItem { Tag: OverviewClearTarget value } => value,
            _ => null
        };
        if (target is null)
            return;

        var page = sender is Control control ? TopLevel.GetTopLevel(control) : null;
        var result = await new FAContentDialog
        {
            Title = MoreResources.M_ClearOverview_Title,
            Content = GetOverviewClearContent(target),
            PrimaryButtonText = target.ProfileName is null
                ? MoreResources.C_ClearOverview_All
                : MoreResources.C_ClearOverview,
            CloseButtonText = SecRandom.Langs.Common.Resources.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(page);

        if (result != FAContentDialogResult.Primary)
            return;

        try
        {
            var overview = IAppHost.GetService<IOverviewStatisticsService>();
            switch (target.Kind)
            {
                case OverviewClearKind.All:
                    overview.Clear();
                    break;
                case OverviewClearKind.RollCall:
                    overview.ClearStudentStatistics(target.ProfileName);
                    break;
                case OverviewClearKind.Lottery:
                    overview.ClearPrizeStatistics(target.ProfileName);
                    break;
                case OverviewClearKind.RandomNumbers:
                    overview.ClearRandomNumberStatistics();
                    break;
            }
        }
        catch (Exception exception)
        {
            await new FAContentDialog
            {
                Title = MoreResources.M_ClearOverview_Failed,
                Content = exception.Message,
                CloseButtonText = SecRandom.Langs.Common.Resources.C_Close,
                DefaultButton = FAContentDialogButton.Close
            }.ShowAsync(page);
        }
    }

    private static string GetOverviewClearContent(OverviewClearTarget target)
    {
        return target.Kind switch
        {
            OverviewClearKind.All => MoreResources.M_ClearOverview_Content,
            OverviewClearKind.RollCall when string.IsNullOrWhiteSpace(target.ProfileName) =>
                MoreResources.M_ClearOverview_RollCallContent,
            OverviewClearKind.Lottery when string.IsNullOrWhiteSpace(target.ProfileName) =>
                MoreResources.M_ClearOverview_LotteryContent,
            OverviewClearKind.RandomNumbers => MoreResources.M_ClearOverview_RandomNumbersContent,
            _ => string.Format(MoreResources.M_ClearOverview_ProfileContent,
                target.ProfileName ?? string.Empty)
        };
    }

    private void EndShortcutInput()
    {
        Focus(NavigationMethod.Unspecified);
    }

    private void SetShortcut(string propertyName, string value)
    {
        switch (propertyName)
        {
            case nameof(MoreSettingsConfig.OpenRollCallPageShortcut): Settings.OpenRollCallPageShortcut = value; break;
            case nameof(MoreSettingsConfig.QuickDrawShortcut): Settings.QuickDrawShortcut = value; break;
            case nameof(MoreSettingsConfig.OpenLotteryPageShortcut): Settings.OpenLotteryPageShortcut = value; break;
            case nameof(MoreSettingsConfig.IncreaseRollCallCountShortcut): Settings.IncreaseRollCallCountShortcut = value; break;
            case nameof(MoreSettingsConfig.DecreaseRollCallCountShortcut): Settings.DecreaseRollCallCountShortcut = value; break;
            case nameof(MoreSettingsConfig.IncreaseLotteryCountShortcut): Settings.IncreaseLotteryCountShortcut = value; break;
            case nameof(MoreSettingsConfig.DecreaseLotteryCountShortcut): Settings.DecreaseLotteryCountShortcut = value; break;
            case nameof(MoreSettingsConfig.StartRollCallShortcut): Settings.StartRollCallShortcut = value; break;
            case nameof(MoreSettingsConfig.StartLotteryShortcut): Settings.StartLotteryShortcut = value; break;
        }
    }

    private bool HasDuplicateShortcut(string propertyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var (name, shortcut) in GetShortcuts())
        {
            if (name != propertyName && string.Equals(shortcut, value, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool IsShortcutConflicted(string propertyName)
    {
        foreach (var (name, shortcut) in GetShortcuts())
        {
            if (name == propertyName)
                return HasDuplicateShortcut(propertyName, shortcut);
        }

        return false;
    }

    private IEnumerable<(string Name, string Shortcut)> GetShortcuts()
    {
        yield return (nameof(MoreSettingsConfig.OpenRollCallPageShortcut), Settings.OpenRollCallPageShortcut);
        yield return (nameof(MoreSettingsConfig.IncreaseRollCallCountShortcut), Settings.IncreaseRollCallCountShortcut);
        yield return (nameof(MoreSettingsConfig.DecreaseRollCallCountShortcut), Settings.DecreaseRollCallCountShortcut);
        yield return (nameof(MoreSettingsConfig.StartRollCallShortcut), Settings.StartRollCallShortcut);
        if (IsQuickDrawAvailable)
            yield return (nameof(MoreSettingsConfig.QuickDrawShortcut), Settings.QuickDrawShortcut);
        if (IsLotteryAvailable)
        {
            yield return (nameof(MoreSettingsConfig.OpenLotteryPageShortcut), Settings.OpenLotteryPageShortcut);
            yield return (nameof(MoreSettingsConfig.IncreaseLotteryCountShortcut), Settings.IncreaseLotteryCountShortcut);
            yield return (nameof(MoreSettingsConfig.DecreaseLotteryCountShortcut), Settings.DecreaseLotteryCountShortcut);
            yield return (nameof(MoreSettingsConfig.StartLotteryShortcut), Settings.StartLotteryShortcut);
        }
    }

    private static bool TryFormatShortcut(KeyEventArgs e, out string shortcut)
    {
        shortcut = string.Empty;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
            return false;

        var key = e.Key switch
        {
            Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
            Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
            Key.Space => "Space", Key.Tab => "Tab", Key.Enter => "Enter", Key.Delete => "Delete",
            Key.Home => "Home", Key.End => "End", Key.PageUp => "PageUp", Key.PageDown => "PageDown",
            Key.Left => "Left", Key.Up => "Up", Key.Right => "Right", Key.Down => "Down",
            _ when e.Key is >= Key.A and <= Key.Z => e.Key.ToString(),
            _ when e.Key is >= Key.F1 and <= Key.F24 => e.Key.ToString(),
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(key))
            return false;

        var modifiers = new System.Collections.Generic.List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers.Add("Win");
        modifiers.Add(key);
        shortcut = string.Join("+", modifiers);
        return true;
    }
}
