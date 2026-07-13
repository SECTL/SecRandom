using System;
using System.IO;
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
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels;
using StudentListResources = SecRandom.Langs.SettingsPages.ListManagement.RollCallList.Resources;

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

        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
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

    private void AttachedSettings_OnSettingsChanged(object? sender, EventArgs e)
    {
        SaveSelectedStudentListConfig();
    }

    private void Command_CreateList_OnClick(object? sender, RoutedEventArgs e)
    {
        // 创建新名单前先保存当前名单，避免左侧切换后丢掉右侧表格的修改。
        SaveSelectedStudentListConfig();

        // 这里只创建名单 JSON，不创建历史文件；历史仍由后续点名流程按需处理。
        var listName = GetNewStudentListName();
        var studentListConfig = new StudentListConfig(listName);
        studentListConfig.Save();

        // 把新名单加入左侧列表并立即选中，让用户可以直接编辑或添加学生。
        ViewModel.StudentLists.Add(listName);
        ViewModel.SelectedStudentListName = listName;
        ViewModel.SelectedStudentListConfig = studentListConfig;
        ViewModel.SelectedStudentList = studentListConfig.Data;
        ViewModel.SelectedStudent = null;
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

    private static string GetNewStudentListName()
    {
        var defaultName = StudentListResources.C_DefaultListName;
        var candidateName = defaultName;
        var suffix = 2;

        while (File.Exists(GetStudentListPath(candidateName)))
        {
            candidateName = $"{defaultName} {suffix}";
            suffix++;
        }

        return candidateName;
    }

    private static string GetStudentListPath(string listName)
    {
        return Utils.GetFilePath("list", "roll_call_list", $"{listName}.json");
    }
}
