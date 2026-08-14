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
using SecRandom.Langs.SettingsPages.ListManagement.RosterTransfer;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.ListManagement.RollCallList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

[PageInfo("settings.listManagement.rollCallList", FluentIcons.PeopleListFilled, "settings.listManagement")]
public partial class RollCallListSettingsPage : UserControl, INotifyPropertyChanged
{
    private string _selectedStudentListName = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<RollCallListSettingsPage> _logger =
        IAppHost.GetService<ILogger<RollCallListSettingsPage>>();
    private readonly IProfileCatalogManager _catalogManager =
        IAppHost.GetService<IProfileCatalogManager>();
    private readonly IHistoryQueryService _historyQueryService =
        IAppHost.GetService<IHistoryQueryService>();

    public bool IsDesktop => App.IsDesktop;
    public string ExportListLabel => RosterTransferText.Get("C_Export");

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

    public StudentList? SelectedStudentList { get; private set; }

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

        foreach (var name in _catalogManager.GetStudentListNames())
            StudentListNames.Add(name);

        if (StudentListNames.Count == 0)
        {
            _catalogManager.CreateStudentList("default");
            StudentListNames.Add("default");
        }

        SelectedStudentListName = StudentListNames.Contains(selectedName) ? selectedName : StudentListNames[0];
        LoadSelectedStudentList();
    }

    private void LoadSelectedStudentList()
    {
        if (string.IsNullOrWhiteSpace(SelectedStudentListName))
            return;

        if (SelectedStudentList != null)
            _catalogManager.SaveStudentList(SelectedStudentList);
        SelectedStudentList = _catalogManager.LoadStudentList(SelectedStudentListName);

        OnPropertyChanged(nameof(SelectedStudentList));
    }

    private void SaveSelectedStudentList()
    {
        if (SelectedStudentList != null)
            _catalogManager.SaveStudentList(SelectedStudentList);
        _catalogManager.SetDefaultStudentList(SelectedStudentListName);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        SaveSelectedStudentList();
    }

    private void AttachedSettings_OnSettingsChanged(object? sender, EventArgs e)
    {
        SaveSelectedStudentList();
    }

    private void StudentListListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LoadSelectedStudentList();
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshStudentLists();
    }

    private async void EditStudentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: Student student } || SelectedStudentList == null)
            return;

        var form = new StackPanel { Spacing = 8 };
        var exists = new CheckBox { IsChecked = student.Exists, Content = LR.C_Exists };
        form.Children.Add(exists);
        var id = AddInputField(form, LR.C_StudentId, student.Id);
        var name = AddInputField(form, LR.C_Name, student.Name);
        var gender = AddInputField(form, LR.C_Gender, student.Gender);
        var group = AddInputField(form, LR.C_Group, student.Group);
        var tags = AddInputField(form, LR.C_Tags, student.Tags);

        var result = await new FAContentDialog
        {
            Title = LR.C_Edit,
            Content = form,
            PrimaryButtonText = LR.M_ListNameDialogPrimary_Rename,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return;

        var studentId = id.Text?.Trim() ?? string.Empty;
        var studentName = name.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(studentId) && string.IsNullOrWhiteSpace(studentName))
        {
            this.ShowWarningToast(LR.M_AddMemberRequired);
            return;
        }

        student.Exists = exists.IsChecked == true;
        student.Id = studentId;
        student.Name = studentName;
        student.Gender = gender.Text?.Trim() ?? string.Empty;
        student.Group = group.Text?.Trim() ?? string.Empty;
        student.Tags = tags.Text?.Trim() ?? string.Empty;
        SaveSelectedStudentList();
        OnPropertyChanged(nameof(SelectedStudentList));
    }

    private async void DeleteStudentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: Student student } || SelectedStudentList == null)
            return;

        var displayName = string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name;
        if (!await ConfirmDeleteMemberAsync(displayName))
            return;

        if (!SelectedStudentList.Students.Remove(student))
        {
            this.ShowWarningToast(LR.M_DeleteMemberNotFound);
            OnPropertyChanged(nameof(SelectedStudentList));
            return;
        }

        SaveSelectedStudentList();
        OnPropertyChanged(nameof(SelectedStudentList));
        _logger.LogInformation("已删除点名名单成员：名单={ListName}，记录={RecordId}。", SelectedStudentListName, student.RecordId);
        this.ShowSuccessToast(string.Format(LR.M_DeleteMemberSuccess, displayName));
    }

    private void ChangeCurrentListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var control = (Control)Resources["ChangeCurrentListDrawer"]!;
        control.DataContext = this;
        SettingsView.Current?.OpenDrawer(control);
    }

    private async void AddListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var listName = await ShowListNameDialogAsync(LR.M_ListNameDialogTitle_Add, LR.M_ListNameDialogPrimary_Add,
            GetNewListName());
        if (listName == null)
            return;

        if (!ValidateNewListName(listName))
            return;

        SaveSelectedStudentList();
        _catalogManager.CreateStudentList(listName);
        RefreshStudentLists(listName);
        _logger.LogInformation("已创建点名名单：名单={ListName}。", listName);
        this.ShowSuccessToast(string.Format(LR.M_AddListSuccess, listName));
    }

    private async void DeleteListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedStudentListName) || SelectedStudentList == null)
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

        SelectedStudentList = null;
        _catalogManager.DeleteStudentList(deleteName, deleteHistory: true);

        var nextName = StudentListNames.FirstOrDefault(name => name != deleteName) ?? string.Empty;
        RefreshStudentLists(nextName);

        _logger.LogInformation("已删除点名名单：名单={ListName}。", deleteName);
        this.ShowSuccessToast(string.Format(LR.M_DeleteListSuccess, deleteName));
    }

    private async void RenameListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedStudentListName) || SelectedStudentList == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var oldName = SelectedStudentListName;
        var newName = await ShowListNameDialogAsync(LR.M_ListNameDialogTitle_Rename,
            LR.M_ListNameDialogPrimary_Rename, oldName);
        if (newName == null || string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        if (!ValidateNewListName(newName) || !_catalogManager.RenameStudentList(oldName, newName))
            return;

        SelectedStudentList = null;
        RefreshStudentLists(newName);
        _logger.LogInformation("已重命名点名名单：旧名单={OldListName}，新名单={NewListName}。", oldName, newName);
        this.ShowSuccessToast(string.Format(LR.M_RenameListSuccess, newName));
    }

    private void ImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedStudentList == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var view = new RollCallListImportView(SelectedStudentListName, OnStudentsImported);
        SettingsView.Current?.OpenDrawer(view);
        _logger.LogInformation("打开点名名单导入面板：目标名单={ListName}。", SelectedStudentListName);
    }

    private void ExportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedStudentList is null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        SettingsView.Current?.OpenDrawer(new RollCallListExportView(SelectedStudentListName,
            SelectedStudentList.Students.ToList()));
        _logger.LogInformation("打开点名名单导出面板：名单={ListName}，成员数={Count}。", SelectedStudentListName,
            SelectedStudentList.Students.Count);
    }

    private async void AddMemberButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedStudentList == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var form = new StackPanel { Spacing = 8 };
        var id = AddInputField(form, LR.C_StudentId);
        var name = AddInputField(form, LR.C_Name);
        var gender = AddInputField(form, LR.C_Gender);
        var group = AddInputField(form, LR.C_Group);
        var tags = AddInputField(form, LR.C_Tags);

        var dialog = new FAContentDialog
        {
            Title = LR.M_AddMemberTitle,
            Content = form,
            PrimaryButtonText = LR.C_AddMember,
            CloseButtonText = LR.C_Cancel,
            IsPrimaryButtonEnabled = false,
            DefaultButton = FAContentDialogButton.Primary
        };

        void UpdateCanConfirm()
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(id.Text) ||
                                            !string.IsNullOrWhiteSpace(name.Text);
        }

        id.TextChanged += (_, _) => UpdateCanConfirm();
        name.TextChanged += (_, _) => UpdateCanConfirm();
        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this));

        var studentId = id.Text?.Trim() ?? string.Empty;
        var studentName = name.Text?.Trim() ?? string.Empty;
        if (result != FAContentDialogResult.Primary)
            return;

        if (string.IsNullOrWhiteSpace(studentId) && string.IsNullOrWhiteSpace(studentName))
        {
            this.ShowWarningToast(LR.M_AddMemberRequired);
            return;
        }

        var shouldOfferHistoryClear = ShouldOfferStudentHistoryClear(SelectedStudentList);
        SelectedStudentList.Students.Add(new Student
        {
            Id = studentId,
            Name = studentName,
            Gender = gender.Text?.Trim() ?? string.Empty,
            Group = group.Text?.Trim() ?? string.Empty,
            Tags = tags.Text?.Trim() ?? string.Empty
        });
        SaveSelectedStudentList();
        OnPropertyChanged(nameof(SelectedStudentList));
        if (shouldOfferHistoryClear && await ConfirmClearStudentHistoryAsync(SelectedStudentListName))
            _catalogManager.ClearStudentHistory(SelectedStudentListName);
        _logger.LogInformation("已向点名名单新增成员：名单={ListName}，当前成员数={Count}。", SelectedStudentListName,
            SelectedStudentList.Students.Count);
        this.ShowSuccessToast(LR.M_AddMemberSuccess);
    }

    private bool ShouldOfferStudentHistoryClear(StudentList list)
    {
        if (list.Students.Count == 0)
            return false;

        var history = _historyQueryService.LoadStudentHistory(SelectedStudentListName);
        if (history is null)
            return false;

        var uniqueLegacyKeys = ProfileRecordIdentity.BuildUniqueStudentLegacyKeySet(list.Students);
        return list.Students.All(student =>
            (ProfileRecordIdentity.GetStudentHistory(history, student, uniqueLegacyKeys.Contains)?.TotalCount ?? 0) > 0);
    }

    private async Task<bool> ConfirmClearStudentHistoryAsync(string listName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_AddMemberHistoryResetTitle,
            Content = string.Format(LR.M_AddMemberHistoryResetContent, listName),
            PrimaryButtonText = LR.M_AddMemberHistoryResetPrimary,
            CloseButtonText = LR.M_AddMemberHistoryResetSecondary,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private static TextBox AddInputField(StackPanel form, string label, string initialText = "")
    {
        var field = new StackPanel { Spacing = 4 };
        field.Children.Add(new TextBlock { Text = label });

        var input = new TextBox { Text = initialText, MinWidth = 320 };
        field.Children.Add(input);
        form.Children.Add(field);
        return input;
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

    private async Task<bool> ConfirmDeleteMemberAsync(string memberName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_DeleteMemberTitle,
            Content = string.Format(LR.M_DeleteMemberContent, memberName, SelectedStudentListName),
            PrimaryButtonText = LR.M_DeleteMemberPrimary,
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

        if (_catalogManager.StudentListExists(listName))
        {
            this.ShowWarningToast(string.Format(LR.M_ListNameExists, listName));
            return false;
        }

        return true;
    }

    private string GetNewListName()
    {
        var defaultName = LR.C_DefaultListName;
        var candidateName = defaultName;
        var suffix = 2;

        while (_catalogManager.StudentListExists(candidateName))
        {
            candidateName = $"{defaultName} {suffix}";
            suffix++;
        }

        return candidateName;
    }

    private async void OnStudentsImported(IReadOnlyList<Student> students)
    {
        if (SelectedStudentList == null)
            return;

        var currentCount = SelectedStudentList.Students.Count;
        if (currentCount > 0 && !await ConfirmOverwriteAsync(currentCount, students.Count))
            return;

        _catalogManager.ReplaceStudents(SelectedStudentListName, students);
        SelectedStudentList = _catalogManager.LoadStudentList(SelectedStudentListName);
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
