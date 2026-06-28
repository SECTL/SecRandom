using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.ListManagement.RollCallList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

[PageInfo("settings.listManagement.rollCallList", FluentIcons.PeopleListRegular, "settings.listManagement")]
public partial class RollCallListSettingsPage : UserControl, INotifyPropertyChanged
{
    private string _selectedStudentListName = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<RollCallListSettingsPage> _logger =
        IAppHost.GetService<ILogger<RollCallListSettingsPage>>();

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

        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("data", "list", "roll_call_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            StudentListNames.Add(Path.GetFileNameWithoutExtension(file));

        if (StudentListNames.Count == 0)
        {
            var config = new StudentListConfig("default");
            config.Save();
            StudentListNames.Add(config.Name);
        }

        SelectedStudentListName = StudentListNames.Contains(selectedName) ? selectedName : StudentListNames[0];
        LoadSelectedStudentList();
    }

    private void LoadSelectedStudentList()
    {
        if (string.IsNullOrWhiteSpace(SelectedStudentListName))
            return;

        SelectedStudentListConfig?.Save();
        SelectedStudentListConfig = new StudentListConfig(SelectedStudentListName);
        OnPropertyChanged(nameof(SelectedStudentList));
    }

    private void SaveSelectedStudentList()
    {
        SelectedStudentListConfig?.Save();

        var service = IAppHost.TryGetService<IProfileService>();
        if (service?.StudentListConfig?.Name == SelectedStudentListName)
            service.LoadStudentProfile(SelectedStudentListName, saveCurrent: false);

        var config = IAppHost.TryGetService<MainConfigHandler>();
        if (config != null && config.Data.RollCallSettings.DefaultClass != SelectedStudentListName)
        {
            config.Data.RollCallSettings.DefaultClass = SelectedStudentListName;
            config.Save();
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        SaveSelectedStudentList();
    }

    private void AttachedSettings_OnSettingsChanged(object? sender, EventArgs e)
    {
        SaveSelectedStudentList();
    }

    private void StudentListComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LoadSelectedStudentList();
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshStudentLists();
    }

    private async void AddListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var listName = await ShowListNameDialogAsync(LR.M_ListNameDialogTitle_Add, LR.M_ListNameDialogPrimary_Add,
            CreateDefaultListName());
        if (listName == null)
            return;

        if (!ValidateNewListName(listName))
            return;

        SaveSelectedStudentList();
        var config = new StudentListConfig(listName);
        config.Save();
        RefreshStudentLists(listName);
        _logger.LogInformation("已创建点名名单：名单={ListName}。", listName);
        this.ShowSuccessToast(string.Format(LR.M_AddListSuccess, listName));
    }

    private async void RenameListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedStudentListName) || SelectedStudentListConfig == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var oldName = SelectedStudentListName;
        var newName = await ShowListNameDialogAsync(LR.M_ListNameDialogTitle_Rename, LR.M_ListNameDialogPrimary_Rename,
            oldName);
        if (newName == null || newName == oldName)
            return;

        if (!ValidateNewListName(newName))
            return;

        SaveSelectedStudentList();

        RenameProfileFile(new StudentList(oldName), new StudentList(newName));
        RenameProfileFile(new StudentHistory(oldName), new StudentHistory(newName));

        var service = IAppHost.GetService<IProfileService>();
        if (service.StudentListConfig?.Name == oldName)
            service.LoadStudentProfile(newName, saveCurrent: false);

        RefreshStudentLists(newName);
        _logger.LogInformation("已重命名点名名单：原名单={OldListName}，新名单={NewListName}。", oldName, newName);
        this.ShowSuccessToast(string.Format(LR.M_RenameListSuccess, newName));
    }

    private async void DeleteListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedStudentListName) || SelectedStudentListConfig == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        if (StudentListNames.Count <= 1)
        {
            this.ShowWarningToast(LR.M_KeepOneList);
            return;
        }

        var deleteName = SelectedStudentListName;
        if (!await ConfirmDeleteAsync(deleteName))
            return;

        SelectedStudentListConfig = null;
        DeleteProfileFile(new StudentList(deleteName));
        DeleteProfileFile(new StudentHistory(deleteName));

        var nextName = StudentListNames.FirstOrDefault(name => name != deleteName) ?? string.Empty;
        RefreshStudentLists(nextName);

        var service = IAppHost.GetService<IProfileService>();
        if (service.StudentListConfig?.Name == deleteName)
            service.LoadStudentProfile(nextName, saveCurrent: false);

        _logger.LogInformation("已删除点名名单：名单={ListName}。", deleteName);
        this.ShowSuccessToast(string.Format(LR.M_DeleteListSuccess, deleteName));
    }

    private void ImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedStudentListConfig == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var view = new RollCallListImportView(SelectedStudentListName, OnStudentsImported);
        SettingsView.Current?.OpenDrawer(view);
        _logger.LogInformation("打开点名名单导入面板：目标名单={ListName}。", SelectedStudentListName);
    }

    private async Task<bool> ConfirmOverwriteAsync(int currentCount, int importCount)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_OverwriteTitle,
            Content = string.Format(LR.M_OverwriteContent, currentCount, importCount),
            PrimaryButtonText = LR.M_OverwritePrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmDeleteAsync(string listName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_DeleteListTitle,
            Content = string.Format(LR.M_DeleteListContent, listName),
            PrimaryButtonText = LR.M_DeleteListPrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async Task<string?> ShowListNameDialogAsync(string title, string primaryButtonText, string listName)
    {
        var textBox = new TextBox
        {
            Text = listName,
            PlaceholderText = LR.M_ListNamePlaceholder,
            MinWidth = 320
        };

        var result = await new FAContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary ? textBox.Text?.Trim() : null;
    }

    private bool ValidateNewListName(string listName)
    {
        if (string.IsNullOrWhiteSpace(listName))
        {
            this.ShowWarningToast(LR.M_ListNameEmpty);
            return false;
        }

        if (listName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            this.ShowWarningToast(LR.M_ListNameInvalid);
            return false;
        }

        if (File.Exists(GetStudentListPath(listName)))
        {
            this.ShowWarningToast(string.Format(LR.M_ListNameExists, listName));
            return false;
        }

        return true;
    }

    private string CreateDefaultListName()
    {
        var baseName = LR.C_DefaultListName;
        if (!File.Exists(GetStudentListPath(baseName)))
            return baseName;

        for (var i = 2;; i++)
        {
            var name = $"{baseName} {i}";
            if (!File.Exists(GetStudentListPath(name)))
                return name;
        }
    }

    private static string GetStudentListPath(string listName)
    {
        return Utils.GetFilePath("data", "list", "roll_call_list", $"{listName}.json");
    }

    private static void RenameProfileFile(ProfileConfigBase oldConfig, ProfileConfigBase newConfig)
    {
        var oldPath = oldConfig.ConfigFilePath;
        var newPath = newConfig.ConfigFilePath;

        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(oldPath))
            return;

        var directory = Path.GetDirectoryName(newPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(newPath))
            File.Delete(newPath);

        File.Move(oldPath, newPath);
    }

    private static void DeleteProfileFile(ProfileConfigBase config)
    {
        var path = config.ConfigFilePath;
        if (File.Exists(path))
            File.Delete(path);
    }

    private async void OnStudentsImported(IReadOnlyList<Student> students)
    {
        if (SelectedStudentListConfig?.Data == null)
            return;

        var currentCount = SelectedStudentListConfig.Data.Students.Count;
        if (currentCount > 0 && !await ConfirmOverwriteAsync(currentCount, students.Count))
            return;

        SelectedStudentListConfig.Data.Students.Clear();
        foreach (var student in students)
            SelectedStudentListConfig.Data.Students.Add(student);

        SaveSelectedStudentList();
        IAppHost.TryGetService<IProfileService>()?.LoadStudentProfile(SelectedStudentListName, saveCurrent: false);
        OnPropertyChanged(nameof(SelectedStudentList));
        SettingsView.Current?.CloseDrawer();
        _logger.LogInformation("已导入点名名单：目标名单={ListName}，导入数量={Count}。", SelectedStudentListName, students.Count);
        this.ShowSuccessToast(string.Format(LR.M_ImportSuccess, students.Count, SelectedStudentListName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
