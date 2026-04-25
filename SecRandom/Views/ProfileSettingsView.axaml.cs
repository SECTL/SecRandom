using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class ProfileSettingsView : UserControl
{
    public static ProfileSettingsView? Current { get; private set; }
    public ProfileSettingsViewModel ViewModel { get; } = IAppHost.GetService<ProfileSettingsViewModel>();
    
    private AppToastAdorner? _appToastAdorner;
    private bool _isAdornerAdded;
    
    public ProfileSettingsView()
    {
        Current = this;
        DataContext = this;
        InitializeComponent();

        NavigationView.SelectedItem = NavigationView.MenuItems[0];
        
        ViewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(ProfileSettingsViewModel.SelectedPageItem))
            {
                ViewModel.SelectedPageIndex = NavigationView.MenuItems.IndexOf(ViewModel.SelectedPageItem);
            }
        };

        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Content is not Control element || _isAdornerAdded)
        {
            return;
        }

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
    
    public void OpenDrawer(object content)
    {
        ViewModel.DrawerContent = content;
        ViewModel.IsDrawerOpen = true;
    }

    public void CloseDrawer()
    {
        ViewModel.IsDrawerOpen = false;
    }
}