using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile;
using SecRandom.Platforms.Abstractions;
using SecRandom.ViewModels;
using SecRandom.Views.Mobile;

namespace SecRandom.Views.SettingsPages.More;

[PageInfo("settings.more", FluentIcons.MoreHorizontalFilled)]
public partial class MoreSettingsPage : UserControl, INotifyPropertyChanged
{
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
            foreach (var propertyName in ShortcutConflictPropertyNames)
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        });
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, ConfigHandler.Data.MoreSettings))
            return;

        ConfigHandler.Save();
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

    private (string Name, string Shortcut)[] GetShortcuts() =>
    [
        (nameof(MoreSettingsConfig.OpenRollCallPageShortcut), Settings.OpenRollCallPageShortcut),
        (nameof(MoreSettingsConfig.QuickDrawShortcut), Settings.QuickDrawShortcut),
        (nameof(MoreSettingsConfig.OpenLotteryPageShortcut), Settings.OpenLotteryPageShortcut),
        (nameof(MoreSettingsConfig.IncreaseRollCallCountShortcut), Settings.IncreaseRollCallCountShortcut),
        (nameof(MoreSettingsConfig.DecreaseRollCallCountShortcut), Settings.DecreaseRollCallCountShortcut),
        (nameof(MoreSettingsConfig.IncreaseLotteryCountShortcut), Settings.IncreaseLotteryCountShortcut),
        (nameof(MoreSettingsConfig.DecreaseLotteryCountShortcut), Settings.DecreaseLotteryCountShortcut),
        (nameof(MoreSettingsConfig.StartRollCallShortcut), Settings.StartRollCallShortcut),
        (nameof(MoreSettingsConfig.StartLotteryShortcut), Settings.StartLotteryShortcut)
    ];

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
