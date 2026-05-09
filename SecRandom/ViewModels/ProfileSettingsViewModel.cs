using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.ViewModels;

public partial class ProfileSettingsViewModel : ViewModelBase
{
    [ObservableProperty] private object? _drawerContent = false;
    [ObservableProperty] private bool _isDrawerOpen;
    [ObservableProperty] private int _selectedPageIndex;
    [ObservableProperty] private NavigationViewItemBase? _selectedPageItem;
    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private StudentList? _selectedStudentList;
    [ObservableProperty] private StudentListConfig? _selectedStudentListConfig;
    [ObservableProperty] private string _selectedStudentListName = string.Empty;

    // students
    [ObservableProperty] private ObservableCollection<string> _studentLists = [];

    public ProfileSettingsViewModel(MainConfigHandler configHandler)
        : base(configHandler)
    {
        RefreshLists();
    }

    public void RefreshLists()
    {
        StudentLists.AddRange(
            from i in Directory.GetFiles(Utils.GetDirectoryPath("data", "list", "roll_call_list"))
            where i.EndsWith(".json")
            orderby i
            select Path.GetFileName(i).Replace(".json", ""));
    }
}