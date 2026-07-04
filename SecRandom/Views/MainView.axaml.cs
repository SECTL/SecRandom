using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DynamicData;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Controls;
using SecRandom.Core.Enums;
using SecRandom.Core.Extensions;
using SecRandom.Core.Icons;
using SecRandom.Core.Services;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class MainView : UserControl, IFANavigationPageFactory
{
    private const string DefaultMainPageId = "main.rollCall";

    private readonly FAFrame? _navigationFrame;
    private readonly FANavigationView? _navigationView;

    private AppToastAdorner? _appToastAdorner;
    private bool _isAdornerAdded;

    public MainView()
    {
        Current = this;
        DataContext = this;
        InitializeComponent();

        _navigationFrame = this.FindControl<FAFrame>("NavigationFrame");
        _navigationView = this.FindControl<FANavigationView>("NavigationView");

        _navigationFrame?.NavigationPageFactory = this;

        BuildNavigationMenuItems();
        SelectNavigationItemById(DefaultMainPageId);

        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);
    }

    public static MainView? Current { get; private set; }
    public MainViewModel ViewModel { get; } = IAppHost.GetService<MainViewModel>();

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

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPageInfo is null) SelectNavigationItemById(DefaultMainPageId);

        if (Content is not Control element || _isAdornerAdded) return;

        var layer = AdornerLayer.GetAdornerLayer(element);
        _appToastAdorner = new AppToastAdorner(this);
        layer?.Children.Add(_appToastAdorner);
        AdornerLayer.SetAdornedElement(_appToastAdorner, this);

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

    private void BuildNavigationMenuItems()
    {
        ViewModel.NavigationViewItems.Clear();
        ViewModel.NavigationViewFooterItems.Clear();

        ViewModel.NavigationViewItems
            .AddRange(PagesRegistryService.MainItems
                .Where(info => info.Location == PageLocation.Top)
                .ToNavigationViewItems(ViewModel.FlattenNavigationItems));

        ViewModel.NavigationViewFooterItems
            .AddRange(PagesRegistryService.MainItems
                .Where(info => info.Location == PageLocation.Bottom)
                .ToNavigationViewItems(ViewModel.FlattenNavigationItems));

        var settingsPageInfo = new PageInfo("settings", FluentIcons.SettingsFilled, null, PageLocation.Bottom)
        {
            Name = Langs.Common.Resources.Feat_Settings
        };
        ViewModel.NavigationViewFooterItems.Add(settingsPageInfo.ToNavigationViewItemBase());
    }

    public void SelectNavigationItemById(string id)
    {
        var info = PagesRegistryService.MainItems.FirstOrDefault(info => info.Id == id);

        if (info != null) CoreNavigate(info);
    }

    private void SelectNavigationItem(PageInfo info)
    {
        var item = ViewModel.FlattenNavigationItems.FirstOrDefault(item => Equals(item.Tag, info));
        ViewModel.SelectedNavigationViewItem = item;
    }

    public void OpenDrawer(object content)
    {
        ViewModel.DrawerContent = content;
        ViewModel.IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        ViewModel.IsDrawerOpen = false;
    }

    private void CoreNavigate(PageInfo info)
    {
        if (info.Id == "settings")
        {
            App.ShowSettingsWindow();
            return;
        }

        ViewModel.FrameContent = null;
        SelectNavigationItem(info);
        ViewModel.SelectedPageInfo = info;
        _navigationFrame?.NavigateFromObject(info);
    }

    private void NavigationView_OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        PageInfo? info = null;

        if (e.InvokedItemContainer is FANavigationViewItem { Tag: PageInfo containerInfo })
            info = containerInfo;
        else if (e.InvokedItem is PageInfo invokedInfo) info = invokedInfo;

        if (info != null) CoreNavigate(info);
    }

    private void TogglePaneButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _navigationView?.IsPaneOpen = !_navigationView.IsPaneOpen;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
