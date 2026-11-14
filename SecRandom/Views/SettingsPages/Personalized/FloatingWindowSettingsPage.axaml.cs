using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Models;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.FloatingWindow;
using SecRandom.Services.Desktop;
using SecRandom.ViewModels;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.FloatingWindow.Resources;

namespace SecRandom.Views.SettingsPages.Personalized;

[PageInfo("settings.personalized.floatingWindow", FluentIcons.WindowAppsFilled, "settings.personalized")]
public partial class FloatingWindowSettingsPage : UserControl
{
    private const string CustomButtonsPageId = "settings.personalized.floatingWindowButtons";
    private bool _isSettingsSubscribed;
    private readonly IFloatingWindowButtonRegistry _floatingWindowButtonRegistry = IAppHost.GetService<IFloatingWindowButtonRegistry>();

    public FloatingWindowSettingsPage()
    {
        Settings = ViewModel.Config.FloatingWindowSettings;
        var migratedSize = NormalizeFloatingWindowSize() | NormalizeDockedWindowSize() | NormalizeDockHandleText();
        ButtonOptions = BuildButtonOptions();
        SelectedButtonOptions = BuildSelectedOptions(ButtonOptions);
        SelectedButtonOptions.CollectionChanged += SelectedButtonOptions_OnCollectionChanged;
        _floatingWindowButtonRegistry.Changed += FloatingWindowButtonRegistry_OnChanged;
        DataContext = this;
        InitializeComponent();
        RefreshCustomButtonsSummary();
        SubscribeSettings();
        if (migratedSize)
            ConfigHandler.Save();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public FloatingWindowSettingsConfig Settings { get; }
    public AvaloniaList<MultiSelectSettingOption> ButtonOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedButtonOptions { get; }
    public bool IsUiAccessSupported => OperatingSystem.IsWindows();
    public string TopmostModeDescription => IsUiAccessSupported
        ? LR.S_Display_TopmostMode_Windows_D
        : LR.S_Display_TopmostMode_D;
    public bool SupportsProgrammaticWindowPositioning => App.SupportsProgrammaticWindowPositioning;

    /// <summary>
    ///     无焦点模式依赖平台层的免激活能力（Windows 上是 <c>WS_EX_NOACTIVATE</c>）。
    ///     平台不具备这项能力时隐藏该行，而不是给用户一个打开后点击仍会抢焦点的开关。
    /// </summary>
    public bool SupportsNoActivate => IAppHost.TryGetService<IWindowFeatureService>()
        ?.SupportedFeatures.HasFlag(WindowFeatures.NoActivate) == true;

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private DesktopIntegrationService DesktopIntegration { get; } = IAppHost.GetService<DesktopIntegrationService>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SubscribeSettings();
        RebuildButtonOptions();
        RefreshCustomButtonsSummary();
        RefreshDockCards();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _floatingWindowButtonRegistry.Changed -= FloatingWindowButtonRegistry_OnChanged;
        if (!_isSettingsSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSettingsSubscribed = false;
    }

    private void SubscribeSettings()
    {
        if (_isSettingsSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSettingsSubscribed = true;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
        if (e.PropertyName == nameof(Settings.CustomButtons))
            RefreshCustomButtonsSummary();
        // 收纳手柄的两张条件卡跟着样式切换显隐；文字/图标变化时刷新预览
        if (e.PropertyName is nameof(Settings.StickToEdgeDisplayStyle)
            or nameof(Settings.StickToEdgeDisplayText)
            or nameof(Settings.StickToEdgeDisplayIconGlyph)
            or nameof(Settings.StickToEdgeDisplayIconImage))
            RefreshDockCards();
        if (e.PropertyName == nameof(Settings.FloatingWindowTopmostMode)
            && Settings.FloatingWindowTopmostMode == TopmostMode.UiAccess
            && !DesktopIntegration.IsUiAccessAvailable())
            SettingsView.Current?.RequestRestartApp();
    }

    private void RefreshCustomButtonsSummary()
    {
        var count = FloatingWindowButtonCatalog
            .GetRenderableButtons(Settings.CustomButtons)
            .Count;
        CustomButtonsSummary.Text = string.Format(
            CultureInfo.CurrentCulture,
            LR.M_Buttons_CustomSummary,
            count,
            FloatingWindowButtonCatalog.MaxButtons);
    }

    private void OpenCustomButtons_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            SettingsView.Current?.NavigateToPage(CustomButtonsPageId);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(LR.M_Buttons_CustomOpenFailed, ex);
        }
    }

    private AvaloniaList<MultiSelectSettingOption> BuildButtonOptions()
    {
        var options = new AvaloniaList<MultiSelectSettingOption>
        {
            new(LR.S_Buttons_RollCall, () => Settings.ShowRollCallButton,
                value => Settings.ShowRollCallButton = value),
            new(LR.S_Buttons_QuickDraw, () => Settings.ShowQuickDrawButton,
                value => Settings.ShowQuickDrawButton = value),
            new(LR.S_Buttons_Lottery, () => Settings.ShowLotteryButton,
                value => Settings.ShowLotteryButton = value),
            new(LR.S_Buttons_Timer, () => Settings.ShowTimerButton,
                value => Settings.ShowTimerButton = value)
        };
        foreach (var button in _floatingWindowButtonRegistry.Buttons)
        {
            options.Add(new MultiSelectSettingOption(
                button.Label,
                () => Settings.VisiblePluginButtonIds.Contains(button.Id, StringComparer.OrdinalIgnoreCase),
                value =>
                {
                    if (value)
                    {
                        if (!Settings.VisiblePluginButtonIds.Contains(button.Id, StringComparer.OrdinalIgnoreCase))
                            Settings.VisiblePluginButtonIds.Add(button.Id);
                    }
                    else
                    {
                        Settings.VisiblePluginButtonIds.Remove(button.Id);
                    }
                }));
        }

        return options;
    }

    private void FloatingWindowButtonRegistry_OnChanged(object? sender, EventArgs e)
    {
        RebuildButtonOptions();
    }

    private void RebuildButtonOptions()
    {
        if (!IsLoaded)
            return;

        var wasSelected = SelectedButtonOptions.Select(option => option.Label).ToHashSet();
        SelectedButtonOptions.CollectionChanged -= SelectedButtonOptions_OnCollectionChanged;
        ButtonOptions.Clear();
        foreach (var option in BuildButtonOptions())
            ButtonOptions.Add(option);
        SelectedButtonOptions.Clear();
        foreach (var option in ButtonOptions.Where(option => wasSelected.Contains(option.Label)))
            SelectedButtonOptions.Add(option);
        SelectedButtonOptions.CollectionChanged += SelectedButtonOptions_OnCollectionChanged;
    }

    private static AvaloniaList<MultiSelectSettingOption> BuildSelectedOptions(
        IEnumerable<MultiSelectSettingOption> options)
    {
        return new AvaloniaList<MultiSelectSettingOption>(options.Where(option => option.IsSelected));
    }

    private bool NormalizeFloatingWindowSize()
    {
        var size = Settings.FloatingWindowSize <= 6
            ? Settings.FloatingWindowSize switch
            {
                0 => 28,
                1 => 32,
                2 => 40,
                3 => 48,
                4 => 56,
                5 => 64,
                _ => 72
            }
            : System.Math.Clamp(Settings.FloatingWindowSize, 32, 160);

        if (size == Settings.FloatingWindowSize)
            return false;

        Settings.FloatingWindowSize = size;
        return true;
    }

    private bool NormalizeDockedWindowSize()
    {
        var size = System.Math.Clamp(Settings.DockedWindowSize, 28, 96);
        if (size == Settings.DockedWindowSize)
            return false;

        Settings.DockedWindowSize = size;
        return true;
    }

    private void SelectedButtonOptions_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var option in ButtonOptions)
            option.SetSelected(SelectedButtonOptions.Contains(option));

        ConfigHandler.Save();
    }

    /// <summary>
    ///     文字样式只允许中文一个字、其它语言四个字符；旧配置里超长或带 emoji 的值在打开页面时清掉。
    /// </summary>
    private bool NormalizeDockHandleText()
    {
        var text = FloatingWindowDockHandle.NormalizeText(
            Settings.StickToEdgeDisplayText,
            FloatingWindowDockHandle.StoredMaxTextLength);
        if (string.Equals(text, Settings.StickToEdgeDisplayText, StringComparison.Ordinal))
            return false;

        Settings.StickToEdgeDisplayText = text;
        return true;
    }

    /// <summary>两张条件卡只在对应样式下出现，箭头样式不额外占用卡片。</summary>
    private void RefreshDockCards()
    {
        var style = Settings.StickToEdgeDisplayStyle;
        S_Dock_Text.IsVisible = FloatingWindowDockHandle.IsTextStyle(style);
        S_Dock_Icon.IsVisible = FloatingWindowDockHandle.IsIconStyle(style);

        var maxLength = FloatingWindowDockHandle.GetMaxTextLength(CultureInfo.CurrentUICulture);
        DockTextInput.MaxLength = maxLength;
        DockTextLimitLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            LR.S_Dock_Text_Limit,
            maxLength);

        RefreshDockIconPreview();
    }

    private void RefreshDockIconPreview()
    {
        DockIconGlyphPreview.Glyph = string.IsNullOrWhiteSpace(Settings.StickToEdgeDisplayIconGlyph)
            ? FloatingWindowDockHandle.DefaultIconGlyph
            : Settings.StickToEdgeDisplayIconGlyph;

        var bitmap = IconService?.TryLoadBitmap(Settings.StickToEdgeDisplayIconImage);
        DockIconImagePreview.Source = bitmap;
        DockIconImagePreview.IsVisible = bitmap is not null;
        DockIconGlyphPreview.IsVisible = bitmap is null;
    }

    private void DockText_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        // 输入框自己挡住 emoji 和超长字符，配置里就不会留下渲染端要再处理的内容
        var text = FloatingWindowDockHandle.NormalizeText(
            box.Text,
            FloatingWindowDockHandle.GetMaxTextLength(CultureInfo.CurrentUICulture));
        if (string.Equals(text, box.Text, StringComparison.Ordinal))
            return;

        box.Text = text;
        box.CaretIndex = text.Length;
    }

    private async void DockIconPick_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var choice = await FloatingWindowIconPicker.PickBuiltInAsync(topLevel);
        if (choice is null)
            return;

        Settings.StickToEdgeDisplayIconGlyph = choice.Glyph;
        // 选内置图标就等于放弃自定义图片，旧文件一并删掉，避免 Prune 之外的孤儿文件
        ReplaceDockIconFile(string.Empty);
        SetDockIconStatus(string.Empty);
        RefreshDockCards();
    }

    private async void DockIconImport_OnClick(object? sender, RoutedEventArgs e)
    {
        var service = IconService;
        var topLevel = TopLevel.GetTopLevel(this);
        if (service is null || topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LR.C_DockImportImage,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LR.M_DockImageType)
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.ico"]
                }
            ]
        });
        if (files.Count == 0)
            return;

        FloatingWindowIconImportResult result;
        await using (var stream = await files[0].OpenReadAsync())
            result = await service.ImportImageAsync(stream);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.FileName))
        {
            SetDockIconStatus(FloatingWindowButtonUi.DescribeImportError(result.Error));
            return;
        }

        ReplaceDockIconFile(result.FileName);
        SetDockIconStatus(string.Empty);
        RefreshDockCards();
    }

    private void DockIconReset_OnClick(object? sender, RoutedEventArgs e)
    {
        Settings.StickToEdgeDisplayIconGlyph = string.Empty;
        ReplaceDockIconFile(string.Empty);
        SetDockIconStatus(string.Empty);
        RefreshDockCards();
    }

    /// <summary>替换自定义图片时删除被替换的旧文件；新文件名与旧值相同时保持原样。</summary>
    private void ReplaceDockIconFile(string fileName)
    {
        var previous = Settings.StickToEdgeDisplayIconImage;
        Settings.StickToEdgeDisplayIconImage = fileName;
        if (!string.IsNullOrEmpty(previous) && previous != fileName)
            IconService?.TryDelete(previous);
    }

    private void SetDockIconStatus(string message)
    {
        DockIconStatusText.Text = message;
        DockIconStatusText.IsVisible = !string.IsNullOrEmpty(message);
    }

    private static FloatingWindowIconService? IconService =>
        IAppHost.TryGetService<FloatingWindowIconService>();
}
