using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
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
using SecRandom.Core.Services;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class SettingsView : UserControl, INavigationPageFactory
{
    public static SettingsView? Current { get; private set; }
    
    public SettingsViewModel ViewModel { get; } = IAppHost.GetService<SettingsViewModel>();
    private const string DefaultMainPageId = "settings.basic";
    
    private AppToastAdorner? _appToastAdorner;
    private bool _isAdornerAdded;
    
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

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPageInfo is null)
        {
            SelectNavigationItemById(DefaultMainPageId);
        }
        
        if (Content is not Control element || _isAdornerAdded)
        {
            return;
        }

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

    public void SelectNavigationItemById(string id, bool isBack = false)
    {
        var info = PagesRegistryService.SettingsItems.FirstOrDefault(info => info.Id == id);
        
        if (info != null)
        {
            CoreNavigate(info, isBack);
        }
    }

    private void CoreNavigate(PageInfo info, bool isBack = false)
    {
        if (ViewModel.SelectedPageInfo?.Id == info.Id)
        {
            return;
        }

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

    private void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var history = ViewModel.NavigationHistory;
        if (history.Any())
        {
            var item = history.Last();
            history.RemoveAt(history.Count - 1);
            SelectNavigationItemById(item, true);
        }
        
        if (!history.Any())
        {
            ViewModel.CanGoBack = false;
        }
    }
    
    private void NavigationView_OnItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        PageInfo? info = null;
        
        if (e.InvokedItemContainer is NavigationViewItem { Tag: PageInfo containerInfo })
        {
            info = containerInfo;
        }
        else if (e.InvokedItem is PageInfo invokedInfo)
        {
            info = invokedInfo;
        }
        
        if (info != null)
        {
            CoreNavigate(info);
        }
    }

    public Control? GetPage(Type srcType)
    {
        return Activator.CreateInstance(srcType) as Control;
    }

    public Control? GetPageFromObject(object target)
    {
        if (target is not PageInfo info)
        {
            return null;
        }

        var page = IAppHost.Host!.Services.GetKeyedService<UserControl>(info.Id);
        if (page == null)
        {
            // 如果页面未注册，返回一个占位符控件
            return new TextBlock { Text = $"页面 {info.Id} 未找到" };
        }
        return page;
    }
}
