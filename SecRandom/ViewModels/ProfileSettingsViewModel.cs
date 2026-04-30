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
    [ObservableProperty] private int _selectedPageIndex = 0;
    [ObservableProperty] private NavigationViewItemBase? _selectedPageItem;
    [ObservableProperty] private bool _isDrawerOpen = false;
    [ObservableProperty] private object? _drawerContent = false;

    // students
    [ObservableProperty] private ObservableCollection<string> _studentLists = [];
    [ObservableProperty] private string _selectedStudentListName = string.Empty;
    [ObservableProperty] private StudentListConfig? _selectedStudentListConfig;
    [ObservableProperty] private StudentList? _selectedStudentList;
    [ObservableProperty] private Student? _selectedStudent;
    
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