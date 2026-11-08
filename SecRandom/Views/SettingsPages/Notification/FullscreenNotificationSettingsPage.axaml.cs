using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Notification;
using SecRandom.Helpers;
using SecRandom.Models;
using SecRandom.Services.Notification;
using SecRandom.Shared;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

[PageInfo("settings.notification.fullscreen", FluentIcons.FullScreenMaximizeFilled, "settings.notification")]
public partial class FullscreenNotificationSettingsPage : UserControl, INotifyPropertyChanged
{
    /// <summary>预览会话固定 3 秒自动关闭，避免用户为了看一次效果而临时改掉真实设置。</summary>
    private const int PreviewAutoCloseSeconds = 3;

    private const string BackgroundImageFolderName = "fullscreen";
    private const string TextLibraryPageId = "settings.notification.texts";

    private readonly TextLibraryService? _textLibraryService;
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private bool _hasBackgroundImage;
    private IBrush _themeAccentColorBrush = new SolidColorBrush(ThemeAccentColor.Resolve());
    private Bitmap? _backgroundImagePreview;
    private string _textLibrarySummary = string.Empty;
    private bool _suppressSave;
    private bool _savePending;
    private DispatcherTimer? _saveTimer;

    public FullscreenNotificationSettingsPage()
    {
        InitializeComponent();
        Settings = ConfigHandler.Data.NotificationSettings.Fullscreen;
        _textLibraryService = IAppHost.TryGetService<TextLibraryService>();
        AnimationStyleOptions = BuildAnimationStyleOptions();
        BackgroundModeOptions = BuildBackgroundModeOptions();
        AccentColorModeOptions = BuildAccentColorModeOptions();
        ContentOptions = BuildContentOptions();
        SelectedContentOptions = BuildSelectedContentOptions(ContentOptions);
        SelectedContentOptions.CollectionChanged += SelectedContentOptions_OnCollectionChanged;
        RefreshThemeAccentColorBrush();

        DataContext = this;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public FullscreenNotificationSettings Settings { get; }

    /// <summary>结果里可勾选的展示元素（序号 / 名称 / 头像），复用浮窗那张多选下拉框。</summary>
    public AvaloniaList<MultiSelectSettingOption> ContentOptions { get; }

    public AvaloniaList<MultiSelectSettingOption> SelectedContentOptions { get; }

    public IReadOnlyList<AnimationStyleOption> AnimationStyleOptions { get; private set; }

    public IReadOnlyList<BackgroundModeOption> BackgroundModeOptions { get; private set; }

    public IReadOnlyList<AccentColorModeOption> AccentColorModeOptions { get; private set; }

    /// <summary>文案分区（含打字机速度）只在打字机样式下有意义，其余样式直接展示结果画面。</summary>
    public bool IsTypewriterStyle => Settings.AnimationStyle == FullscreenAnimationStyle.Typewriter;

    /// <summary>背景图导入、模糊与背景不透明度只在自定义图片模式下出现。</summary>
    public bool IsBackgroundImageMode => Settings.BackgroundMode == FullscreenBackgroundMode.Image;

    /// <summary>纯色卡片只在纯色模式下出现（不属于该模式的内容一律不弹出来）。</summary>
    public bool IsSolidBackgroundMode => Settings.BackgroundMode == FullscreenBackgroundMode.Solid;

    /// <summary>渐变两色与角度只在渐变模式下出现。</summary>
    public bool IsGradientBackgroundMode => Settings.BackgroundMode == FullscreenBackgroundMode.Gradient;

    /// <summary>跟随主题时左侧显示主题色色块，自定义时换成取色器。</summary>
    public bool IsThemeAccentColorVisible => Settings.UseThemeAccentColor;

    /// <summary>选中自定义颜色时才出现取色器。</summary>
    public bool IsCustomAccentColorVisible => !Settings.UseThemeAccentColor;

    public string TypewriterSpeedText => string.Format(
        Text("{0} chars/sec", "M_FullscreenTypewriterSpeedFormat"),
        Settings.EffectiveTypewriterCharactersPerSecond);

    public string ResultFontSizeText => string.Format(
        Text("{0} px", "M_FullscreenResultFontSizeFormat"),
        Settings.EffectiveResultFontSize);

    public IBrush ThemeAccentColorBrush => _themeAccentColorBrush;

    public AnimationStyleOption SelectedAnimationStyleOption
    {
        get => AnimationStyleOptions.FirstOrDefault(option => option.Value == Settings.AnimationStyle)
               ?? AnimationStyleOptions[0];
        set
        {
            if (value is null || Settings.AnimationStyle == value.Value)
                return;

            Settings.AnimationStyle = value.Value;
        }
    }

    public BackgroundModeOption SelectedBackgroundMode
    {
        get => BackgroundModeOptions.FirstOrDefault(option => option.Value == Settings.BackgroundMode)
               ?? BackgroundModeOptions[0];
        set
        {
            if (value is null || Settings.BackgroundMode == value.Value)
                return;

            Settings.BackgroundMode = value.Value;
        }
    }

    public bool HasBackgroundImage
    {
        get => _hasBackgroundImage;
        private set
        {
            if (_hasBackgroundImage == value)
                return;

            _hasBackgroundImage = value;
            OnPropertyChanged(nameof(HasBackgroundImage));
        }
    }

    public Bitmap? BackgroundImagePreview
    {
        get => _backgroundImagePreview;
        private set
        {
            _backgroundImagePreview = value;
            OnPropertyChanged(nameof(BackgroundImagePreview));
        }
    }

    /// <summary>取色器直接读写设置里的十六进制串；跟随主题时它被隐藏，值也不落盘。</summary>
    public Color AccentColorValue
    {
        get => Color.TryParse(Settings.AccentColor, out var color) ? color : ThemeAccentColor.Resolve();
        set
        {
            var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            if (Settings.AccentColor == hex)
                return;

            Settings.AccentColor = hex;
        }
    }

    /// <summary>强调色模式下拉框：跟随主题色 / 自定义颜色。</summary>
    public AccentColorModeOption SelectedAccentColorMode
    {
        get => AccentColorModeOptions.FirstOrDefault(option => option.FollowTheme == Settings.UseThemeAccentColor)
               ?? AccentColorModeOptions[0];
        set
        {
            if (value is null || Settings.UseThemeAccentColor == value.FollowTheme)
                return;

            Settings.UseThemeAccentColor = value.FollowTheme;
        }
    }

    public string TextLibrarySummary
    {
        get => _textLibrarySummary;
        private set
        {
            if (_textLibrarySummary == value)
                return;

            _textLibrarySummary = value;
            OnPropertyChanged(nameof(TextLibrarySummary));
        }
    }

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private static string BackgroundImageDirectory => Utils.GetDirectoryPath("images", BackgroundImageFolderName);

    protected void OnPageUnloaded(object? sender, RoutedEventArgs e)
    {
        // 切页/关窗时把挂起的编辑立刻落盘：光靠防抖定时器会丢最后 400ms 内的拖动。
        CommitSettings();

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        if (_textLibraryService is not null)
            _textLibraryService.Changed -= TextLibraryOnChanged;

        _saveTimer?.Stop();
        _saveTimer = null;

        SelectedContentOptions.CollectionChanged -= SelectedContentOptions_OnCollectionChanged;
    }

    private void OnPageLoaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        Settings.PropertyChanged += SettingsOnPropertyChanged;

        if (_textLibraryService is not null)
        {
            _textLibraryService.Changed -= TextLibraryOnChanged;
            _textLibraryService.Changed += TextLibraryOnChanged;
        }

        // 语言可以在不重建页面的情况下改变，选项文案因此每次加载都重读一次。
        var refreshedOptions = BuildAnimationStyleOptions();
        if (!AnimationStyleOptions.SequenceEqual(refreshedOptions))
        {
            AnimationStyleOptions = refreshedOptions;
            OnPropertyChanged(nameof(AnimationStyleOptions));
            OnPropertyChanged(nameof(SelectedAnimationStyleOption));
        }

        var refreshedBackgroundModes = BuildBackgroundModeOptions();
        if (!BackgroundModeOptions.SequenceEqual(refreshedBackgroundModes))
        {
            BackgroundModeOptions = refreshedBackgroundModes;
            OnPropertyChanged(nameof(BackgroundModeOptions));
            OnPropertyChanged(nameof(SelectedBackgroundMode));
        }

        var refreshedAccentModes = BuildAccentColorModeOptions();
        if (!AccentColorModeOptions.SequenceEqual(refreshedAccentModes))
        {
            AccentColorModeOptions = refreshedAccentModes;
            OnPropertyChanged(nameof(AccentColorModeOptions));
            OnPropertyChanged(nameof(SelectedAccentColorMode));
        }

        RebuildContentOptions();
        RefreshBackgroundImagePreview();
        RefreshThemeAccentColorBrush();
        RefreshTextLibrarySummary();
        NotifyDerivedSettingsProperties();
    }

    /// <summary>把随设置派生的可见性与文案一次性推给界面，用于页面加载与语言刷新。</summary>
    private void NotifyDerivedSettingsProperties()
    {
        OnPropertyChanged(nameof(IsTypewriterStyle));
        OnPropertyChanged(nameof(IsBackgroundImageMode));
        OnPropertyChanged(nameof(IsSolidBackgroundMode));
        OnPropertyChanged(nameof(IsGradientBackgroundMode));
        OnPropertyChanged(nameof(IsCustomAccentColorVisible));
        OnPropertyChanged(nameof(IsThemeAccentColorVisible));
        OnPropertyChanged(nameof(AccentColorValue));
        OnPropertyChanged(nameof(TypewriterSpeedText));
        OnPropertyChanged(nameof(ResultFontSizeText));
    }

    /// <summary>
    ///     显示内容的多选项：序号、名称、头像。设置页每次加载都按当前语言重建一次标签，
    ///     已勾选项按标签回填，语言切换不会把用户的勾选弄丢。
    /// </summary>
    private AvaloniaList<MultiSelectSettingOption> BuildContentOptions()
    {
        return new AvaloniaList<MultiSelectSettingOption>
        {
            new(Text("Number", "O_FullscreenContent_Number"),
                () => Settings.ShowResultNumber,
                value => Settings.ShowResultNumber = value),
            new(Text("Name", "O_FullscreenContent_Name"),
                () => Settings.ShowResultName,
                value => Settings.ShowResultName = value),
            new(Text("Avatar", "O_FullscreenContent_Avatar"),
                () => Settings.ShowResultAvatar,
                value => Settings.ShowResultAvatar = value)
        };
    }

    private static AvaloniaList<MultiSelectSettingOption> BuildSelectedContentOptions(
        IEnumerable<MultiSelectSettingOption> options)
        => new(options.Where(option => option.IsSelected));

    private void RebuildContentOptions()
    {
        if (!IsLoaded)
            return;

        var wasSelected = SelectedContentOptions.Select(option => option.Label).ToHashSet();
        SelectedContentOptions.CollectionChanged -= SelectedContentOptions_OnCollectionChanged;
        ContentOptions.Clear();
        foreach (var option in BuildContentOptions())
            ContentOptions.Add(option);
        SelectedContentOptions.Clear();
        foreach (var option in ContentOptions.Where(option => wasSelected.Contains(option.Label)))
            SelectedContentOptions.Add(option);
        SelectedContentOptions.CollectionChanged += SelectedContentOptions_OnCollectionChanged;
        OnPropertyChanged(nameof(ContentOptions));
        OnPropertyChanged(nameof(SelectedContentOptions));
    }

    private void SelectedContentOptions_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var option in ContentOptions)
            option.SetSelected(SelectedContentOptions.Contains(option));

        ScheduleSave();
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FullscreenNotificationSettings.AnimationStyle):
                OnPropertyChanged(nameof(SelectedAnimationStyleOption));
                OnPropertyChanged(nameof(IsTypewriterStyle));
                break;
            case nameof(FullscreenNotificationSettings.BackgroundMode):
                OnPropertyChanged(nameof(SelectedBackgroundMode));
                OnPropertyChanged(nameof(IsBackgroundImageMode));
                OnPropertyChanged(nameof(IsSolidBackgroundMode));
                OnPropertyChanged(nameof(IsGradientBackgroundMode));
                break;
            case nameof(FullscreenNotificationSettings.TypewriterCharactersPerSecond):
                OnPropertyChanged(nameof(TypewriterSpeedText));
                break;
            case nameof(FullscreenNotificationSettings.ResultFontSize):
                OnPropertyChanged(nameof(ResultFontSizeText));
                break;
            case nameof(FullscreenNotificationSettings.UseThemeAccentColor):
                OnPropertyChanged(nameof(IsCustomAccentColorVisible));
                OnPropertyChanged(nameof(IsThemeAccentColorVisible));
                OnPropertyChanged(nameof(SelectedAccentColorMode));
                if (!Settings.UseThemeAccentColor)
                    EnsureCustomAccentColor();
                OnPropertyChanged(nameof(AccentColorValue));
                break;
            case nameof(FullscreenNotificationSettings.AccentColor):
                OnPropertyChanged(nameof(AccentColorValue));
                break;
        }

        if (_suppressSave)
            return;

        // 子配置（NotificationSettings.Fullscreen）不会自动落盘，页面的每一次编辑都要显式保存一次。
        // 这里只排一个防抖保存：取色器与滑杆拖动时一秒会改几十次属性，逐次写整份 settings.json 太浪费。
        ScheduleSave();
    }

    /// <summary>
    ///     排一次 400ms 后的落盘。连续编辑会被下一次调用重置，只有手停下来才真正写文件。
    /// </summary>
    private void ScheduleSave()
    {
        _savePending = true;

        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) => CommitSettings();
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    ///     立刻把挂起的编辑落盘；没有挂起编辑就什么都不做（避免切页时白写一次 settings.json）。
    /// </summary>
    private void CommitSettings()
    {
        _saveTimer?.Stop();
        if (!_savePending)
            return;

        _savePending = false;

        // 从 settings.json 手工改出来的越界值在这里夹回去；NormalizeBackground 自身会改属性，
        // 必须屏蔽掉一次保存，否则会再排一个定时器。
        _suppressSave = true;
        try
        {
            Settings.NormalizeBackground();
        }
        finally
        {
            _suppressSave = false;
        }

        ConfigHandler.Save();
    }

    /// <summary>
    ///     从"跟随主题"切到自定义颜色时，设置里不能是空的：用当前主题色作为初始手选值，
    ///     否则取色器会停在一个没有来源的默认色上。
    /// </summary>
    private void EnsureCustomAccentColor()
    {
        if (Settings.UseThemeAccentColor || !string.IsNullOrWhiteSpace(Settings.AccentColor))
            return;

        var color = ThemeAccentColor.Resolve();
        Settings.AccentColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void TextLibraryOnChanged(object? sender, EventArgs e) => RefreshTextLibrarySummary();

    private void RefreshTextLibrarySummary()
    {
        if (_textLibraryService is null)
        {
            TextLibrarySummary = string.Empty;
            return;
        }

        TextLibrarySummary = string.Format(
            Text("{0} texts, {1} rotating", "M_FullscreenTextSummary"),
            _textLibraryService.Entries.Count,
            _textLibraryService.EligibleCount);
    }

    private void RefreshThemeAccentColorBrush()
    {
        _themeAccentColorBrush = new SolidColorBrush(ThemeAccentColor.Resolve());
        OnPropertyChanged(nameof(ThemeAccentColorBrush));
        OnPropertyChanged(nameof(AccentColorValue));
    }

    private void OpenTextLibrary_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            SettingsView.Current?.NavigateToPage(TextLibraryPageId);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Text("Text library is not available", "M_TextLibraryUnavailable"), ex);
        }
    }

    private async void ImportBackgroundImage_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Text("Import image", "C_FullscreenImportImage"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"]
                    }
                ]
            });

            var file = files.FirstOrDefault();
            if (file is null)
                return;

            var targetPath = await CopyImageToBackgroundFolderAsync(file);
            if (targetPath is null)
            {
                this.ShowErrorToast(Text("Failed to import the background image", "M_FullscreenImageImportFailed"));
                return;
            }

            var previousFileName = Settings.BackgroundImage;
            Settings.BackgroundImage = Path.GetFileName(targetPath);
            // 选图即切到图片模式：用户刚挑了一张图，界面上的背景却还是纯色会很意外。
            Settings.BackgroundMode = FullscreenBackgroundMode.Image;
            RefreshBackgroundImagePreview();
            DeleteBackgroundImageFile(previousFileName);
            this.ShowSuccessToast(Text("Background image updated", "M_FullscreenImageImported"));
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Text("Failed to import the background image", "M_FullscreenImageImportFailed"), ex);
        }
    }

    private void ClearBackgroundImage_OnClick(object? sender, RoutedEventArgs e)
    {
        var previousFileName = Settings.BackgroundImage;
        Settings.BackgroundImage = string.Empty;
        // 图被清掉了，图片模式就没有内容可画：回到跟随主题，而不是留一张空背景。
        Settings.BackgroundMode = FullscreenBackgroundMode.Theme;
        RefreshBackgroundImagePreview();
        DeleteBackgroundImageFile(previousFileName);
    }

    private void Preview_OnClick(object? sender, RoutedEventArgs e)
    {
        var presenter = IAppHost.TryGetService<FullscreenNotificationPresenter>();
        if (presenter is null || !NotificationService.IsFullscreenPresentationAvailable)
        {
            this.ShowWarningToast(Text("The fullscreen preview is not available", "M_FullscreenPreviewUnavailable"));
            return;
        }

        try
        {
            if (presenter.HasActiveSession)
                presenter.Cancel(NotificationSettingsType.RollCall);

            var items = new List<FullscreenResultItem>
            {
                new(Text("Preview student 1", "M_FullscreenPreviewStudent1"), "0001"),
                new(Text("Preview student 2", "M_FullscreenPreviewStudent2"), "0002"),
                new(Text("Preview student 3", "M_FullscreenPreviewStudent3"), "0003")
            };

            // 预览必须和真实展示同形：先 Begin 打开待揭晓窗口，再 Complete 播结果，否则网格会缺少冻结池。
            var seed = FullscreenNotificationComposer.CreateSeed(
                NotificationSettingsType.RollCall,
                items,
                items.Count,
                items.Count,
                null);

            var snapshot = new FullscreenResultSnapshot(
                NotificationSettingsType.RollCall,
                items,
                items,
                items.Count,
                items.Count,
                null,
                null,
                null,
                DateTimeOffset.Now,
                FullscreenNotificationComposer.FallbackTemplate,
                Settings.ShowText,
                null);

            // 预览走一次性的 3 秒倒计时覆盖值，不动任何真实设置：正式倒计时永远取自通知渠道的显示时长。
            presenter.Begin(NotificationSettingsType.RollCall, seed);
            presenter.Complete(NotificationSettingsType.RollCall, snapshot, PreviewAutoCloseSeconds);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Text("The fullscreen preview is not available", "M_FullscreenPreviewUnavailable"), ex);
        }
    }

    /// <summary>解析背景图文件名到 <c>data/images/fullscreen/</c>，与全屏窗口的解析规则保持一致。</summary>
    private static string ResolveBackgroundImagePath(string fileName)
        => Path.Combine(Utils.DataRoot, "images", BackgroundImageFolderName, fileName.Trim());

    private void RefreshBackgroundImagePreview()
    {
        var previous = BackgroundImagePreview;
        var fileName = Settings.BackgroundImage;
        Bitmap? bitmap = null;

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                var path = ResolveBackgroundImagePath(fileName);
                if (File.Exists(path))
                    bitmap = new Bitmap(path);
            }
            catch (Exception)
            {
                // 图片损坏时预览留空，但不能妨碍其他设置。
                bitmap = null;
            }
        }

        HasBackgroundImage = !string.IsNullOrWhiteSpace(fileName);
        BackgroundImagePreview = bitmap;
        previous?.Dispose();
    }

    private static async Task<string?> CopyImageToBackgroundFolderAsync(IStorageFile file)
    {
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif"))
            return null;

        var directory = BackgroundImageDirectory;
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");

        await using var source = await file.OpenReadAsync();
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target);
        if (target.Length == 0)
        {
            target.Close();
            File.Delete(targetPath);
            return null;
        }

        return targetPath;
    }

    private void DeleteBackgroundImageFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        try
        {
            var directory = Path.GetFullPath(BackgroundImageDirectory);
            var path = Path.GetFullPath(ResolveBackgroundImagePath(fileName));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;

            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // 旧图删不掉只是留下一个孤儿文件，不能影响设置切换。
        }
    }

    private static IReadOnlyList<AnimationStyleOption> BuildAnimationStyleOptions()
    {
        return
        [
            new AnimationStyleOption(FullscreenAnimationStyle.Direct, Text("Direct", "O_FullscreenAnimation_Direct")),
            new AnimationStyleOption(FullscreenAnimationStyle.Typewriter, Text("Typewriter", "O_FullscreenAnimation_Typewriter")),
            new AnimationStyleOption(FullscreenAnimationStyle.Grid, Text("Grid", "O_FullscreenAnimation_Grid"))
        ];
    }

    private static IReadOnlyList<BackgroundModeOption> BuildBackgroundModeOptions()
    {
        return
        [
            new BackgroundModeOption(FullscreenBackgroundMode.Theme, Text("Follow theme", "O_FullscreenBackground_Theme")),
            new BackgroundModeOption(FullscreenBackgroundMode.Solid, Text("Solid color", "O_FullscreenBackground_Solid")),
            new BackgroundModeOption(FullscreenBackgroundMode.Gradient, Text("Gradient", "O_FullscreenBackground_Gradient")),
            new BackgroundModeOption(FullscreenBackgroundMode.Image, Text("Custom image", "O_FullscreenBackground_Image"))
        ];
    }

    private static IReadOnlyList<AccentColorModeOption> BuildAccentColorModeOptions()
    {
        return
        [
            new AccentColorModeOption(true, Text("Follow theme color", "O_FullscreenAccent_FollowTheme")),
            new AccentColorModeOption(false, Text("Custom color", "O_FullscreenAccent_Custom"))
        ];
    }

    private static string Text(string fallback, string key)
        => LR.ResourceManager.GetString(key, LR.Culture) ?? fallback;

    private void OnPropertyChanged(string propertyName)
        => NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record AnimationStyleOption(FullscreenAnimationStyle Value, string Label);

public sealed record BackgroundModeOption(FullscreenBackgroundMode Value, string Label);

/// <summary>强调色模式：跟随主题色，或用取色器手选。</summary>
public sealed record AccentColorModeOption(bool FollowTheme, string Label);
