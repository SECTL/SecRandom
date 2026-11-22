using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Presentation;
using SecRandom.Helpers;
using SecRandom.Services.Notification;
using SecRandom.Shared;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

/// <summary>
///     内置通知浮窗（闪抽结果浮窗）的窗口设置。位置、显示器、偏移与不透明度只有一份，写在
///     <see cref="NotificationSettingsConfig.Default" /> 上；各通知渠道只保留启用、动画、通知服务与交付时长，
///     渠道的窗口覆盖字段已无界面入口，因此新页面的每次编辑就是所有渠道看到的浮窗几何。
/// </summary>
[PageInfo("settings.notification.floatingWindow", FluentIcons.PictureInPictureFilled, "settings.notification")]
public partial class NotificationFloatingWindowSettingsPage : UserControl, INotifyPropertyChanged
{
    private const string MonitorLabelFormatKey = "O_Monitor_Label";
    private const string MonitorPrimaryKey = "O_Monitor_Primary";
    private const string BackgroundImageFolderName = "notification";

    private bool _savePending;
    private DispatcherTimer? _saveTimer;
    private bool _hasBackgroundImage;
    private Bitmap? _backgroundImagePreview;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public NotificationFloatingWindowSettingsPage()
    {
        InitializeComponent();
        Settings = ConfigHandler.Data.NotificationSettings.Default;
        CountdownStyleOptions =
        [
            new CountdownStyleOption((int)NotificationCountdownStyle.Text, Text("Text hint", "O_CountdownStyle_Text")),
            new CountdownStyleOption((int)NotificationCountdownStyle.Edge, Text("Window outline", "O_CountdownStyle_Edge"))
        ];
        CountdownColorModeOptions =
        [
            new CountdownColorModeOption((int)NotificationCountdownColorMode.Gradient,
                Text("Gradient", "O_CountdownColorMode_Gradient")),
            new CountdownColorModeOption((int)NotificationCountdownColorMode.Solid,
                Text("Single color", "O_CountdownColorMode_Solid"))
        ];
        BackgroundStyleOptions = NotificationBackgroundCatalog.FloatingStyles.Select(style =>
            new BackgroundStyleOption(style, Text(style.ToString(),
                $"O_FullscreenBackground_{NotificationBackgroundCatalog.ToFullscreenMode(style)}"))).ToArray();
        MonitorOptions = [new MonitorOption("", Text("Not specified", "O_Monitor_Unspecified"))];
        if (!string.IsNullOrWhiteSpace(Settings.EnabledMonitor))
            MonitorOptions.Add(new MonitorOption(Settings.EnabledMonitor, Settings.EnabledMonitor));

        // 顺序必须与 NotificationWindowAnchorLayout.GridOrder 一致：左上 → 上 → 右上 → 左 → 中 → 右 → 左下 → 下 → 右下。
        WindowPositionLabels =
        [
            Text("Top left", "O_WindowPosition_TopLeft"),
            Text("Top", "O_WindowPosition_Top"),
            Text("Top right", "O_WindowPosition_TopRight"),
            Text("Left", "O_WindowPosition_Left"),
            Text("Center", "O_WindowPosition_Center"),
            Text("Right", "O_WindowPosition_Right"),
            Text("Bottom left", "O_WindowPosition_BottomLeft"),
            Text("Bottom", "O_WindowPosition_Bottom"),
            Text("Bottom right", "O_WindowPosition_BottomRight")
        ];

        DataContext = this;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    public NotificationChannelSettings Settings { get; }

    public string PreviewTitle => Text("Floating notification preview", "S_FloatingWindow_Preview");
    public string PreviewDescription => Text(
        "Show a sample result in the built-in floating notification window",
        "S_FloatingWindow_Preview_D");
    public string PreviewButtonText => Text("Show preview", "C_FloatingWindowPreview");

    public IReadOnlyList<BackgroundStyleOption> BackgroundStyleOptions { get; }

    public bool IsThemeBackgroundMode => Settings.BackgroundStyle == WindowBackgroundStyle.Theme;
    public bool IsSolidBackgroundMode => Settings.BackgroundStyle == WindowBackgroundStyle.Solid;
    public bool IsGradientBackgroundMode => Settings.BackgroundStyle == WindowBackgroundStyle.Gradient;
    public bool IsBackgroundImageMode => Settings.BackgroundStyle == WindowBackgroundStyle.Image;

    public BackgroundStyleOption? SelectedBackgroundStyle
    {
        get => BackgroundStyleOptions.FirstOrDefault(option => option.Value == Settings.BackgroundStyle);
        set
        {
            if (value is null || Settings.BackgroundStyle == value.Value)
                return;

            Settings.BackgroundStyle = value.Value;
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
            Notify(nameof(HasBackgroundImage));
        }
    }

    public Bitmap? BackgroundImagePreview
    {
        get => _backgroundImagePreview;
        private set
        {
            _backgroundImagePreview = value;
            Notify(nameof(BackgroundImagePreview));
        }
    }

    public ObservableCollection<MonitorOption> MonitorOptions { get; }

    /// <summary>九宫格各格的文本，顺序与 <see cref="NotificationWindowAnchorLayout.GridOrder" /> 一致。</summary>
    public IReadOnlyList<string> WindowPositionLabels { get; }

    public MonitorOption? SelectedMonitor
    {
        get => MonitorOptions.FirstOrDefault(option => option.Value == Settings.EnabledMonitor);
        set
        {
            if (value is null || value.Value == Settings.EnabledMonitor)
                return;

            Settings.EnabledMonitor = value.Value;
        }
    }

    /// <summary>低配模式会强制关闭自适应前景色，开关因此置灰。</summary>
    public bool IsAdaptiveForegroundAvailable => !UiRenderQuality.IsLowSpecMode;

    /// <summary>
    ///     开关显示的生效值：低配模式下一律显示为关闭，但不去改用户已保存的偏好，
    ///     退出低配模式后开关重新启用并恢复用户自己的选择。
    /// </summary>
    public bool AdaptiveForegroundEnabled
    {
        get => IsAdaptiveForegroundAvailable && Settings.AdaptiveForeground;
        set
        {
            if (!IsAdaptiveForegroundAvailable || Settings.AdaptiveForeground == value)
                return;

            Settings.AdaptiveForeground = value;
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    /// <summary>倒计时样式下拉框的候选：文字提示 / 四周描边。</summary>
    public IReadOnlyList<CountdownStyleOption> CountdownStyleOptions { get; }

    public CountdownStyleOption? SelectedCountdownStyleOption
    {
        get => CountdownStyleOptions.FirstOrDefault(option => option.Value == Settings.CountdownStyle);
        set
        {
            if (value is null || value.Value == Settings.CountdownStyle)
                return;

            Settings.CountdownStyle = value.Value;
        }
    }

    /// <summary>颜色模式下拉框的候选：渐变 / 统一纯色。</summary>
    public IReadOnlyList<CountdownColorModeOption> CountdownColorModeOptions { get; }

    public CountdownColorModeOption? SelectedCountdownColorModeOption
    {
        get => CountdownColorModeOptions.FirstOrDefault(option => option.Value == Settings.CountdownColorMode);
        set
        {
            if (value is null || value.Value == Settings.CountdownColorMode)
                return;

            Settings.CountdownColorMode = value.Value;
        }
    }

    /// <summary>颜色卡只在选中「四周描边」时出现：文字倒计时没有可设置的颜色。</summary>
    public bool ShowCountdownColors => Settings.UsesEdgeCountdown;

    public bool IsCountdownGradientMode =>
        Settings.UsesEdgeCountdown
        && Settings.CountdownColorMode == (int)NotificationCountdownColorMode.Gradient;

    public bool IsCountdownSolidMode =>
        Settings.UsesEdgeCountdown
        && Settings.CountdownColorMode == (int)NotificationCountdownColorMode.Solid;

    /// <summary>
    ///     设置卡里的预览条，与浮窗四周使用同一组颜色：渐变时起始色在左、终止色在右，纯色时满条同色。
    /// </summary>
    public IBrush CountdownPreviewBrush
    {
        get
        {
            var start = Settings.ResolvedCountdownStart;
            var end = Settings.ResolvedCountdownEnd;
            if (Settings.CountdownColorMode != (int)NotificationCountdownColorMode.Gradient)
                return new SolidColorBrush(start);

            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(start, 0),
                    new GradientStop(end, 1)
                }
            };
        }
    }

    private void Notify(string propertyName)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPageLoaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        Settings.PropertyChanged += SettingsOnPropertyChanged;

        RefreshMonitorOptions();

        // 低配模式可能在基本设置页刚被改过；回到本页时按当前值重算开关的可用性与显示态。
        Notify(nameof(IsAdaptiveForegroundAvailable));
        Notify(nameof(AdaptiveForegroundEnabled));

        // 页面可能被缓存后重新显示，倒计时相关的派生值同样按当前配置重算。
        Notify(nameof(SelectedCountdownStyleOption));
        Notify(nameof(SelectedCountdownColorModeOption));
        Notify(nameof(ShowCountdownColors));
        Notify(nameof(IsCountdownGradientMode));
        Notify(nameof(IsCountdownSolidMode));
        Notify(nameof(CountdownPreviewBrush));
        RefreshBackgroundImagePreview();
    }

    private void OnPageUnloaded(object? sender, RoutedEventArgs e)
    {
        // 切页或关窗时把挂起的编辑立刻落盘，否则防抖窗口里的最后一次拖动会丢。
        CommitSettings();

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _saveTimer?.Stop();
        _saveTimer = null;
        var preview = BackgroundImagePreview;
        BackgroundImagePreview = null;
        preview?.Dispose();
    }

    /// <summary>显示器名称随显示器插拔变化，每次加载与打开下拉框时都重建一次候选列表。</summary>
    private void RefreshMonitorOptions()
    {
        var selectedMonitor = Settings.EnabledMonitor;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Screens is not null)
        {
            var nativeNames = WindowsMonitorNameProvider.GetNames();
            for (var index = 0; index < topLevel.Screens.All.Count; index++)
            {
                var screen = topLevel.Screens.All[index];
                nativeNames.TryGetValue(screen.Bounds.Position, out var nativeName);
                var displayName = string.IsNullOrWhiteSpace(screen.DisplayName) ? nativeName : screen.DisplayName;
                var identifier = NotificationMonitorIdentifier.Get(displayName, screen.Bounds);
                if (MonitorOptions.All(option => option.Value != identifier))
                {
                    var label = NotificationMonitorIdentifier.GetLabel(
                        displayName,
                        screen.Bounds,
                        screen.IsPrimary,
                        index,
                        Text("{0}: {1}x{2} @ {3},{4}{5}", MonitorLabelFormatKey),
                        Text("（主显示器）", MonitorPrimaryKey));
                    MonitorOptions.Add(new MonitorOption(identifier, label));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedMonitor)
            && MonitorOptions.All(option => option.Value != selectedMonitor))
            MonitorOptions.Add(new MonitorOption(selectedMonitor, selectedMonitor));

        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitor)));
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {

        if (e.PropertyName == nameof(NotificationChannelSettings.EnabledMonitor))
            Notify(nameof(SelectedMonitor));

        if (e.PropertyName == nameof(NotificationChannelSettings.BackgroundImage))
            RefreshBackgroundImagePreview();

        if (e.PropertyName == nameof(NotificationChannelSettings.BackgroundStyle))
        {
            Notify(nameof(SelectedBackgroundStyle));
            Notify(nameof(IsThemeBackgroundMode));
            Notify(nameof(IsSolidBackgroundMode));
            Notify(nameof(IsGradientBackgroundMode));
            Notify(nameof(IsBackgroundImageMode));
        }

        if (e.PropertyName == nameof(NotificationChannelSettings.CountdownStyle))
        {
            Notify(nameof(ShowCountdownColors));
            Notify(nameof(IsCountdownGradientMode));
            Notify(nameof(IsCountdownSolidMode));
            Notify(nameof(SelectedCountdownStyleOption));
        }
        else if (e.PropertyName == nameof(NotificationChannelSettings.CountdownColorMode))
        {
            Notify(nameof(IsCountdownGradientMode));
            Notify(nameof(IsCountdownSolidMode));
            Notify(nameof(SelectedCountdownColorModeOption));
            Notify(nameof(CountdownPreviewBrush));
        }
        else if (e.PropertyName is nameof(NotificationChannelSettings.CountdownGradientStart)
                 or nameof(NotificationChannelSettings.CountdownGradientEnd)
                 or nameof(NotificationChannelSettings.CountdownSolidColor))
        {
            Notify(nameof(CountdownPreviewBrush));
        }

        // 子配置（NotificationSettings.Default）不会自动落盘，页面的每次编辑都要显式保存一次。
        ScheduleSave();
    }

    /// <summary>排一次 400ms 后的落盘，连续编辑（滑杆拖动）会被下一次调用重置。</summary>
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

    private void CommitSettings()
    {
        _saveTimer?.Stop();
        if (!_savePending)
            return;

        _savePending = false;
        ConfigHandler.Save();
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
                Title = Text("Import image", "C_FloatingWindow_ImportImage"),
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
                this.ShowErrorToast(Text("Failed to import the notification background image", "M_FloatingWindowImageImportFailed"));
                return;
            }

            var previousFileName = Settings.BackgroundImage;
            Settings.BackgroundImage = Path.GetFileName(targetPath);
            Settings.BackgroundStyle = WindowBackgroundStyle.Image;
            RefreshBackgroundImagePreview();
            DeleteBackgroundImageFile(previousFileName);
            this.ShowSuccessToast(Text("Notification background image updated", "M_FloatingWindowImageImported"));
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Text("Failed to import the notification background image", "M_FloatingWindowImageImportFailed"), ex);
        }
    }

    private void ClearBackgroundImage_OnClick(object? sender, RoutedEventArgs e)
    {
        var previousFileName = Settings.BackgroundImage;
        Settings.BackgroundImage = string.Empty;
        RefreshBackgroundImagePreview();
        DeleteBackgroundImageFile(previousFileName);
    }

    private void TestNotification_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var notificationService = IAppHost.GetService<NotificationService>();
            if (!notificationService.ShowBuiltInTestNotification(Settings))
                this.ShowWarningToast(Text("The floating notification is not available", "M_NotificationTestUnavailable"));
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Text("The floating notification test is not available", "M_NotificationTestUnavailable"), ex);
        }
    }

    private static string BackgroundImageDirectory => Utils.GetDirectoryPath("images", BackgroundImageFolderName);

    private static string? ResolveBackgroundImagePath(string fileName)
    {
        var directory = Path.GetFullPath(BackgroundImageDirectory);
        var path = Path.GetFullPath(Path.Combine(directory, fileName.Trim()));
        return path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

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
                if (path is not null && File.Exists(path))
                {
                    using var stream = SharedFileReader.OpenRead(path);
                    bitmap = new Bitmap(stream);
                }
            }
            catch
            {
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
        var temporaryPath = targetPath + ".tmp";

        try
        {
            await using var source = await file.OpenReadAsync();
            await using (var target = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.WriteThrough))
            {
                await source.CopyToAsync(target);
                await target.FlushAsync();
                target.Flush(flushToDisk: true);
            }

            if (new FileInfo(temporaryPath).Length == 0)
                return null;

            File.Move(temporaryPath, targetPath);
            return targetPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void DeleteBackgroundImageFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        try
        {
            var path = ResolveBackgroundImagePath(fileName);
            if (path is not null && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 删除旧图失败只会留下孤儿文件，不能影响设置切换。
        }
    }

    private static string Text(string fallback, string key)
    {
        return LR.ResourceManager.GetString(key, LR.Culture) ?? fallback;
    }
}

/// <summary>显示器下拉框的一项。<see cref="Value" /> 是持久化的显示器标识，空串表示跟随主显示器。</summary>
public sealed record MonitorOption(string Value, string Label);

/// <summary>倒计时样式下拉框的一项。<see cref="Value" /> 是持久化的 <see cref="NotificationCountdownStyle" />。</summary>
public sealed record CountdownStyleOption(int Value, string Label);

/// <summary>倒计时配色模式下拉框的一项。</summary>
public sealed record CountdownColorModeOption(int Value, string Label);

/// <summary>通知浮窗背景材质选项。</summary>
public sealed record BackgroundStyleOption(WindowBackgroundStyle Value, string Label);
