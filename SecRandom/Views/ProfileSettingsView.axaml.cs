using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Controls;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class ProfileSettingsView : UserControl
{
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
                ViewModel.SelectedPageIndex = NavigationView.MenuItems.IndexOf(ViewModel.SelectedPageItem);
        };

        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);
    }

    public static ProfileSettingsView? Current { get; private set; }
    public ProfileSettingsViewModel ViewModel { get; } = IAppHost.GetService<ProfileSettingsViewModel>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
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
        SaveSelectedStudentListConfig();
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

    private void SaveSelectedStudentListConfig()
    {
        if (ViewModel.SelectedStudentListConfig == null)
            return;

        ViewModel.SelectedStudentListConfig.Save();

        var service = IAppHost.GetService<IProfileService>();
        if (service.StudentListConfig?.Name == ViewModel.SelectedStudentListName) service.StudentListConfig.Reload();
    }

    private void SaveProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveSelectedStudentListConfig();
        this.ShowSuccessToast(Langs.ProfileSettings.Resources.Message_SavedProfile);
    }

    private void StudentListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SaveSelectedStudentListConfig();
        ViewModel.SelectedStudentListConfig = new StudentListConfig(ViewModel.SelectedStudentListName);
        ViewModel.SelectedStudentList = ViewModel.SelectedStudentListConfig.Data;
    }

    private void CreateStudentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SelectedStudentList?.Students.Add(new Student());
    }
}