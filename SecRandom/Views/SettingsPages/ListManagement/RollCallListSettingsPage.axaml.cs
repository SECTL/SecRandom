using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Views.SettingsPages.ListManagement;

[PageInfo("settings.listManagement.rollCallList", FluentIcons.PeopleListRegular, "settings.listManagement")]
public partial class RollCallListSettingsPage : UserControl, INotifyPropertyChanged
{
    private string _selectedStudentListName = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public RollCallListSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        RefreshStudentLists();
    }

    public ObservableCollection<string> StudentListNames { get; } = [];

    public string SelectedStudentListName
    {
        get => _selectedStudentListName;
        set
        {
            if (_selectedStudentListName == value)
                return;

            _selectedStudentListName = value;
            OnPropertyChanged();
        }
    }

    public StudentList? SelectedStudentList => SelectedStudentListConfig?.Data;

    private StudentListConfig? SelectedStudentListConfig { get; set; }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void RefreshStudentLists()
    {
        var previous = SelectedStudentListName;
        RefreshStudentLists(previous);
    }

    private void RefreshStudentLists(string selectedName)
    {
        StudentListNames.Clear();

        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "roll_call_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            StudentListNames.Add(Path.GetFileNameWithoutExtension(file));

        if (StudentListNames.Count == 0)
        {
            SelectedStudentListConfig = null;
            SelectedStudentListName = string.Empty;
            OnPropertyChanged(nameof(SelectedStudentList));
            return;
        }

        SelectedStudentListName = StudentListNames.Contains(selectedName) ? selectedName : StudentListNames[0];
        LoadSelectedStudentList();
    }

    private void LoadSelectedStudentList()
    {
        if (string.IsNullOrWhiteSpace(SelectedStudentListName))
            return;

        // 设置页只负责查看名单；名单的创建、编辑和保存统一放在“编辑档案”窗口。
        SelectedStudentListConfig = new StudentListConfig(SelectedStudentListName);
        OnPropertyChanged(nameof(SelectedStudentList));
    }

    private void StudentListComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LoadSelectedStudentList();
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshStudentLists();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
