using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Controls;
using SecRandom.Core.Enums;
using SecRandom.Core.Extensions;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.Services.ImportExport;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class SettingsView : UserControl, IFANavigationPageFactory
{
    private const string DefaultMainPageId = "settings.overview";

    private readonly ILogger<SettingsView> _logger = IAppHost.GetService<ILogger<SettingsView>>();
    private AppToastAdorner? _appToastAdorner;

    private Border? _currentHighlight;
    private Action? _highlightCleanup;
    private bool _isAdornerAdded;
    private bool _isShowingRestartDialog;
    private bool _isPreviewMode;

    public SettingsView()
    {
        Current = this;
        DataContext = this;
        InitializeComponent();

        NavigationFrame.NavigationPageFactory = this;
        BuildNavigationMenuItems();
        SelectNavigationItemById(DefaultMainPageId);

        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);
    }

    public static SettingsView? Current { get; private set; }
    public static AutoCompleteFilterPredicate<object?> SettingsFilterProperty => SearchFilter;

    public SettingsViewModel ViewModel { get; } = IAppHost.GetService<SettingsViewModel>();
    private IImportExportService ImportExportService { get; } = IAppHost.GetService<IImportExportService>();

    #region Misc

    public static bool SearchFilter(string? search, object? item)
    {
        if (string.IsNullOrWhiteSpace(search) || item is not SettingsMetadata metadata) return false;

        search = search.Trim();

        const StringComparison mode = StringComparison.OrdinalIgnoreCase;

        if (metadata.ToString().StartsWith(search, mode)) return true;

        // 按名称搜素
        return metadata.PageName.Contains(search, mode) ||
               metadata.CategoryName.Contains(search, mode) ||
               metadata.Name.Contains(search, mode) ||
               metadata.Description.Contains(search, mode);
    }

    private void SearchBox_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel.SelectedSettings == null) return;

        var settings = ViewModel.SelectedSettings;

        _logger.LogInformation("跳转到设置 [{PageId}] {Id}", settings.PageId, settings.Id);
        SelectNavigationItemById(settings.PageId);

        if (settings.IsPage) return;

        var pageRoot = NavigationFrame.Content as Control;

        var settingsControl = pageRoot?.FindControl<Control>(settings.Id);
        _logger.LogInformation("设置控件: {Control}", settingsControl);

        Control? categoryControl = null;
        if (!settings.IsCategory)
        {
            categoryControl = pageRoot?.FindControl<Control>(settings.CategoryId);
            _logger.LogInformation("分类控件: {Control}", categoryControl);

            if (categoryControl is FASettingsExpander settingsExpander) settingsExpander.IsExpanded = true;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var targetControl = settingsControl ?? categoryControl;
            targetControl?.BringIntoView();
            targetControl?.Focus();

            HighlightControl(targetControl, TimeSpan.FromSeconds(3));
        }, DispatcherPriority.Render);
    }

    public void HighlightControl(Control? target, TimeSpan? duration = null)
    {
        // Powered By DeepSeek V4

        RemoveHighlight();
        if (target == null) return;

        var controlRoot = FindControlRootPanel(target);
        if (controlRoot is not Panel panel) return;

        var overlay = panel.FindControl<Canvas>(@"__highlightOverlay");
        if (overlay == null)
        {
            overlay = new Canvas
            {
                Name = @"__highlightOverlay",
                ZIndex = 1000,
                IsHitTestVisible = false
            };
            panel.Children.Add(overlay);
        }

        var transform = target.TransformToVisual(overlay);
        if (transform == null) return;
        var position = transform.Value.Transform(new Point(0, 0));

        var color = IAppHost.GetService<MainConfigHandler>().Data.Appearance.ThemeColor;
        var highlight = new Border
        {
            Width = target.Bounds.Width,
            Height = target.Bounds.Height,
            Background = new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B)),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(highlight, position.X);
        Canvas.SetTop(highlight, position.Y);
        overlay.Children.Add(highlight);
        _currentHighlight = highlight;

        void OnLayoutUpdated(object? s, EventArgs e)
        {
            if (target == null || highlight == null) return;

            var newTransform = target.TransformToVisual(overlay);
            if (newTransform == null) return;
            var newPos = newTransform.Value.Transform(new Point(0, 0));

            Canvas.SetLeft(highlight, newPos.X);
            Canvas.SetTop(highlight, newPos.Y);
            highlight.Width = target.Bounds.Width;
            highlight.Height = target.Bounds.Height;
        }

        target.LayoutUpdated += OnLayoutUpdated;

        if (duration.HasValue)
        {
            var timer = new Timer(duration.Value.TotalMilliseconds) { AutoReset = false };
            timer.Elapsed += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_currentHighlight == highlight)
                        RemoveHighlight();
                });
            };
            timer.Start();
        }

        _highlightCleanup = () =>
        {
            target.LayoutUpdated -= OnLayoutUpdated;
            overlay.Children.Remove(highlight);
        };
    }

    private void RemoveHighlight()
    {
        _highlightCleanup?.Invoke();
        _currentHighlight = null;
    }

    private static Control? FindControlRootPanel(Control control)
    {
        var parent = control.Parent;
        while (parent != null)
        {
            if (parent is Panel panel)
                return panel;
            parent = parent.Parent;
        }

        return control.GetVisualParent() as Control;
    }

    #endregion

    #region Lifecycle

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPageInfo is null) SelectNavigationItemById(DefaultMainPageId);

        if (Content is not Control element || _isAdornerAdded) return;

        var layer = AdornerLayer.GetAdornerLayer(element);
        var appToastAdorner = _appToastAdorner = new AppToastAdorner(this);
        layer?.Children.Add(appToastAdorner);
        AdornerLayer.SetAdornedElement(appToastAdorner, this);

        if (GlobalConstants.IsDevelopment)
        {
            var adorner = new DevelopmentBuildAdorner();
            layer?.Children.Add(adorner);
            AdornerLayer.SetAdornedElement(adorner, this);
        }

        _isAdornerAdded = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        IAppHost.TryGetService<MainConfigHandler>()?.Save();
        DataContext = null;
    }

    #endregion

    #region Drawer

    public void OpenDrawer(object content)
    {
        ViewModel.DrawerContent = content;
        ViewModel.IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        ViewModel.IsDrawerOpen = false;
    }

    #endregion

    #region Restart App

    public void RequestRestartApp()
    {
        ViewModel.IsRequestedRestart = true;
        _ = ShowRestartDialog();
    }

    private async Task ShowRestartDialog()
    {
        if (_isShowingRestartDialog) return;
        _isShowingRestartDialog = true;

        var r = await new FAContentDialog
        {
            Title = Langs.SettingsView.Resources.M_NeedsRestarting,
            Content = Langs.SettingsView.Resources.M_NeedsRestarting_D,
            PrimaryButtonText = Langs.SettingsView.Resources.M_NeedsRestarting_Primary,
            CloseButtonText = Langs.SettingsView.Resources.M_NeedsRestarting_Close,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        _isShowingRestartDialog = false;
        if (r != FAContentDialogResult.Primary)
            return;

        App.Current.Restart();
    }

    private void ButtonRestartApp_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = ShowRestartDialog();
    }

    private void LogViewerMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        SelectNavigationItemById("settings.logs");
    }

    private async void ExportDiagnosticDataMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanTransferData())
            return;
        var includeExtendedData = await ConfirmDiagnosticExportAsync();
        if (includeExtendedData is null)
            return;
        var path = await PickSavePathAsync(
            GetResource("C_ExportDiagnosticDataFileTitle"),
            $"SecRandom_diagnostic_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip",
            "zip",
            GetResource("C_ZipFileType"));
        if (path is null)
            return;

        try
        {
            await ImportExportService.ExportDiagnosticAsync(path, includeExtendedData.Value);
            this.ShowSuccessToast(string.Format(GetResource("M_ExportSuccess"), Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出诊断数据失败。");
            this.ShowErrorToast(GetResource("M_ExportFailed"));
        }
    }

    private async void ExportSettingsMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanTransferData())
            return;
        var path = await PickSavePathAsync(
            GetResource("C_ExportSettingsFileTitle"),
            "SecRandom_v3_settings.json",
            "json",
            GetResource("C_JsonFileType"));
        if (path is null)
            return;

        try
        {
            await ImportExportService.ExportSettingsAsync(path);
            this.ShowSuccessToast(string.Format(GetResource("M_ExportSuccess"), Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出设置失败。");
            this.ShowErrorToast(GetResource("M_ExportFailed"));
        }
    }

    private async void ImportSettingsMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanTransferData())
            return;
        var path = await PickOpenPathAsync(
            GetResource("C_ImportSettingsFileTitle"),
            "*.json",
            GetResource("C_JsonFileType"));
        if (path is null)
            return;

        try
        {
            var inspection = await ImportExportService.InspectSettingsAsync(path);
            if (!inspection.IsSupportedV3)
            {
                await ShowUnsupportedImportAsync(inspection);
                return;
            }
            if (!await ConfirmImportAsync(BuildImportConfirmation(GetResource("M_ImportSettingsContent"), inspection)))
                return;
            var result = await ImportExportService.ImportSettingsAsync(path);
            this.ShowSuccessToast(string.Format(GetResource("M_ImportSuccess"), Path.GetFileName(result.SnapshotPath)));
            RequestRestartApp();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入设置失败。");
            await ShowImportFailureAsync(ex);
        }
    }

    private async void ExportAllDataMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanTransferData())
            return;
        var path = await PickSavePathAsync(
            GetResource("C_ExportAllDataFileTitle"),
            "SecRandom_v3_all_data.zip",
            "zip",
            GetResource("C_ZipFileType"));
        if (path is null)
            return;

        try
        {
            await ImportExportService.ExportAllDataAsync(path);
            this.ShowSuccessToast(string.Format(GetResource("M_ExportSuccess"), Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出全部数据失败。");
            this.ShowErrorToast(GetResource("M_ExportFailed"));
        }
    }

    private async void ImportAllDataMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanTransferData())
            return;
        var path = await PickOpenPathAsync(
            GetResource("C_ImportAllDataFileTitle"),
            "*.zip",
            GetResource("C_ZipFileType"));
        if (path is null)
            return;

        try
        {
            var inspection = await ImportExportService.InspectAllDataAsync(path);
            if (!inspection.IsSupportedV3)
            {
                await ShowUnsupportedImportAsync(inspection);
                return;
            }
            if (!await ConfirmImportAsync(BuildImportConfirmation(GetResource("M_ImportAllDataContent"), inspection)))
                return;
            var result = await ImportExportService.ImportAllDataAsync(path);
            this.ShowSuccessToast(string.Format(GetResource("M_ImportSuccess"), Path.GetFileName(result.SnapshotPath)));
            RequestRestartApp();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入全部数据失败。");
            await ShowImportFailureAsync(ex);
        }
    }

    private async Task<string?> PickSavePathAsync(string title, string suggestedFileName, string extension, string fileType)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(fileType) { Patterns = [$"*.{extension}"] }]
        });
        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickOpenPathAsync(string title, string pattern, string fileType)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(fileType) { Patterns = [pattern] }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<bool> ConfirmImportAsync(string content)
    {
        var result = await new FAContentDialog
        {
            Title = GetResource("M_ImportTitle"),
            Content = content,
            PrimaryButtonText = GetResource("C_Import"),
            CloseButtonText = GetResource("C_Cancel"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        return result == FAContentDialogResult.Primary;
    }

    private async Task ShowUnsupportedImportAsync(ImportInspection inspection)
    {
        var detectedVersion = string.IsNullOrWhiteSpace(inspection.ProducerVersion)
            ? GetResource("M_ImportUnsupportedUnknownVersion")
            : inspection.ProducerVersion;
        var details = inspection.Warnings.Count == 0
            ? GetResource("M_ImportUnsupportedContent")
            : string.Join(Environment.NewLine, inspection.Warnings);
        await new FAContentDialog
        {
            Title = GetResource("M_ImportUnsupportedTitle"),
            Content = string.Format(GetResource("M_ImportUnsupportedVersion"), detectedVersion, details),
            CloseButtonText = GetResource("C_Close"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private async Task ShowImportFailureAsync(Exception exception)
    {
        await new FAContentDialog
        {
            Title = GetResource("M_ImportFailed"),
            Content = exception.Message,
            CloseButtonText = GetResource("C_Close"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private async Task<bool?> ConfirmDiagnosticExportAsync()
    {
        var result = await new FAContentDialog
        {
            Title = GetResource("M_DiagnosticExportTitle"),
            Content = GetResource("M_DiagnosticExportContent"),
            PrimaryButtonText = GetResource("C_DiagnosticStandard"),
            SecondaryButtonText = GetResource("C_DiagnosticExtended"),
            CloseButtonText = GetResource("C_Cancel"),
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result switch
        {
            FAContentDialogResult.Primary => false,
            FAContentDialogResult.Secondary => true,
            _ => null
        };
    }

    private static string BuildImportConfirmation(string content, ImportInspection inspection)
    {
        var warningText = inspection.Warnings.Count == 0 ? string.Empty : $"\n\n{string.Join(Environment.NewLine, inspection.Warnings)}";
        return $"{content}\n\n来源版本：{inspection.ProducerVersion}\n将处理 {inspection.FileCount} 个文件。导入前会自动创建恢复快照，快照失败将取消导入。安全凭据不会导入。{warningText}";
    }

    private bool CanTransferData()
    {
        if (!_isPreviewMode)
            return true;
        this.ShowWarningToast("预览模式下不能导入或导出数据。");
        return false;
    }

    private static string GetResource(string key)
    {
        return Langs.SettingsView.Resources.ResourceManager.GetString(key) ?? key;
    }

    #endregion

    #region Navigation

    private void BuildNavigationMenuItems()
    {
        ViewModel.NavigationViewItems.Clear();
        ViewModel.NavigationViewFooterItems.Clear();

        ViewModel.NavigationViewItems
            .AddRange(PagesRegistryService.SettingsItems
                .Where(info => info.Location == PageLocation.Top)
                .ToNavigationViewItems(ViewModel.FlattenNavigationItems));

        ViewModel.NavigationViewFooterItems
            .AddRange(PagesRegistryService.SettingsItems
                .Where(info => info.Location == PageLocation.Bottom)
                .ToNavigationViewItems(ViewModel.FlattenNavigationItems));
    }

    private void CoreNavigate(PageInfo info, bool isBack = false)
    {
        if (ViewModel.SelectedPageInfo?.Id == info.Id) return;

        if (ViewModel.SelectedPageInfo != null && !isBack)
        {
            ViewModel.NavigationHistory.Add(ViewModel.SelectedPageInfo.Id);
            ViewModel.CanGoBack = true;
        }

        var item = ViewModel.FlattenNavigationItems.FirstOrDefault(item => Equals(item.Tag, info));
        ViewModel.FrameContent = null;
        ViewModel.SelectedNavigationViewItem = item;
        ViewModel.SelectedPageInfo = info;
        NavigationFrame.NavigateFromObject(info);
    }

    public void SelectNavigationItemById(string id, bool isBack = false)
    {
        var info = PagesRegistryService.SettingsItems.FirstOrDefault(info => info.Id == id);

        if (info != null) CoreNavigate(info, isBack);
    }

    public void NavigateToPage(string id)
    {
        ExitPreview();
        var info = PagesRegistryService.SettingsItems.FirstOrDefault(item => item.Id == id);
        if (info is not null)
            CoreNavigate(info, isBack: true);
    }

    public void NavigateToPreviewPage(string id)
    {
        _isPreviewMode = true;
        var info = PagesRegistryService.SettingsItems.FirstOrDefault(item => item.Id == id);
        if (info is not null)
            CoreNavigate(info, isBack: true);
        UpdatePreviewState();
    }

    private void UpdatePreviewState()
    {
        NavigationFrame.IsEnabled = !_isPreviewMode;
        NavigationFrame.IsHitTestVisible = !_isPreviewMode;
    }

    public void ExitPreview()
    {
        _isPreviewMode = false;
        UpdatePreviewState();
    }

    private void TogglePaneButton_OnClick(object? sender, RoutedEventArgs e)
    {
        NavigationView.IsPaneOpen = !NavigationView.IsPaneOpen;
    }

    private void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var history = ViewModel.NavigationHistory;
        if (history.Any())
        {
            var item = history.Last();
            history.RemoveAt(history.Count - 1);
            SelectNavigationItemById(item, true);
        }

        if (!history.Any()) ViewModel.CanGoBack = false;
    }

    private void NavigationView_OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        PageInfo? info = null;

        if (e.InvokedItemContainer is FANavigationViewItem { Tag: PageInfo containerInfo })
            info = containerInfo;
        else if (e.InvokedItem is PageInfo invokedInfo) info = invokedInfo;

        if (info != null) CoreNavigate(info);
    }

    public Control? GetPage(Type srcType)
    {
        return Activator.CreateInstance(srcType) as Control;
    }

    public Control? GetPageFromObject(object target)
    {
        if (target is not PageInfo info) return null;

        var page = IAppHost.Host!.Services.GetKeyedService<UserControl>(info.Id);
        if (page == null)
            // 如果页面未注册，返回一个占位符控件
            return new TextBlock { Text = string.Format(Langs.SettingsView.Resources.M_PageNotFound, info.Id) };

        return page;
    }

    #endregion
}
