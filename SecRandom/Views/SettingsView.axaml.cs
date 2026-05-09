using System;
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
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class SettingsView : UserControl, INavigationPageFactory
{
    private const string DefaultMainPageId = "settings.basic";

    private readonly ILogger<SettingsView> _logger = IAppHost.GetService<ILogger<SettingsView>>();
    private AppToastAdorner? _appToastAdorner;

    private Border? _currentHighlight;
    private Action? _highlightCleanup;
    private bool _isAdornerAdded;
    private bool _isShowingRestartDialog;

    public SettingsView()
    {
        Current = this;
        DataContext = this;
        InitializeComponent();

        NavigationFrame.NavigationPageFactory = this;
        BuildNavigationMenuItems();
        SelectNavigationItemById(DefaultMainPageId);

        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);
    }

    public static SettingsView? Current { get; private set; }
    public static AutoCompleteFilterPredicate<object?> SettingsFilterProperty => SearchFilter;

    public SettingsViewModel ViewModel { get; } = IAppHost.GetService<SettingsViewModel>();

    #region Misc

    public static bool SearchFilter(string? search, object? item)
    {
        if (string.IsNullOrWhiteSpace(search) || item is not SettingsMetadata metadata) return false;

        search = search.Trim();

        const StringComparison mode = StringComparison.OrdinalIgnoreCase;

        if (metadata.ToString().StartsWith(search, mode)) return true;

        // 按名称搜素
        var part1 =
            metadata.PageName.Contains(search, mode) ||
            metadata.CategoryName.Contains(search, mode) ||
            metadata.Name.Contains(search, mode) ||
            metadata.Description.Contains(search, mode);
        if (part1) return part1;

        // 按 id 搜索
        if (search == @".") return false;
        return metadata.PageId.Contains(search, mode) ||
               metadata.Id.Contains(search, mode);
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

        if (!settings.IsCategory)
        {
            var categoryControl = pageRoot?.FindControl<Control>(settings.CategoryId);
            _logger.LogInformation("分类控件: {Control}", categoryControl);

            if (categoryControl is SettingsExpander settingsExpander) settingsExpander.IsExpanded = true;
        }

        Dispatcher.UIThread.Post(() =>
        {
            settingsControl?.BringIntoView();
            settingsControl?.Focus();

            HighlightControl(settingsControl, TimeSpan.FromSeconds(3));
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

        var color = IAppHost.GetService<MainConfigHandler>().Data.BasicSettings.ThemeColor;
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

        return control.GetVisualRoot() as Control;
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

        var r = await new ContentDialog
        {
            Title = Langs.SettingsView.Resources.M_NeedsRestarting,
            Content = Langs.SettingsView.Resources.M_NeedsRestarting_D,
            PrimaryButtonText = Langs.SettingsView.Resources.M_NeedsRestarting_Primary,
            CloseButtonText = Langs.SettingsView.Resources.M_NeedsRestarting_Close,
            DefaultButton = ContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        _isShowingRestartDialog = false;
        if (r != ContentDialogResult.Primary)
            return;

        App.Restart();
    }

    private void ButtonRestartApp_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = ShowRestartDialog();
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

    private void NavigationView_OnItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        PageInfo? info = null;

        if (e.InvokedItemContainer is NavigationViewItem { Tag: PageInfo containerInfo })
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
            return new TextBlock { Text = $"页面 {info.Id} 未找到" };

        return page;
    }

    #endregion
}