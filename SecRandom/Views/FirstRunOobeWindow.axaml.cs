using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Services.ImportExport;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.FirstRunOobe.Resources;

namespace SecRandom.Views;

public partial class FirstRunOobeWindow : Window
{
    private bool _canClose;
    private bool _isDevelopmentAdornerAdded;
    private bool _isLanguageSelectionReady;
    private bool _refreshLanguageWhenDrawerCloses;

    public FirstRunOobeWindow()
    {
        DataContext = this;
        InitializeComponent();
        Closed += WindowOnClosed;
        Opened += WindowOnOpened;
    }

    public FirstRunOobeViewModel ViewModel { get; } = IAppHost.GetService<FirstRunOobeViewModel>();
    public bool IsCompleted { get; private set; }
    public bool IsReplacingForLanguageChange { get; private set; }
    public event EventHandler? Completed;
    public event EventHandler? LanguageChanged;
    private IImportExportService ImportExportService { get; } = IAppHost.GetService<IImportExportService>();

    private void WindowOnOpened(object? sender, EventArgs e)
    {
        _isLanguageSelectionReady = true;

        if (!GlobalConstants.IsDevelopment || _isDevelopmentAdornerAdded || Content is not Control element)
            return;

        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null)
            return;

        var adorner = new DevelopmentBuildAdorner();
        layer.Children.Add(adorner);
        AdornerLayer.SetAdornedElement(adorner, this);
        _isDevelopmentAdornerAdded = true;
    }

    private void Previous_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Previous();
    }

    private async void Next_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsFinalStep)
        {
            ViewModel.Next();
            return;
        }

        if (!await ViewModel.FinishAsync())
            return;

        IsCompleted = true;
        Completed?.Invoke(this, EventArgs.Empty);
        _canClose = true;
        Close();
    }

    private void Appearance_OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        ViewModel.RefreshAppearance();
    }

    private void Language_OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isLanguageSelectionReady)
            return;

        if (sender is not ComboBox { SelectedIndex: >= 0 } selector ||
            !ViewModel.SetLanguage((LanguageMode)selector.SelectedIndex))
            return;

        var culture = ViewModel.Basic.Language switch
        {
            LanguageMode.ChineseSimplified => "zh-Hans",
            LanguageMode.English => "en-US",
            LanguageMode.Japanese => "ja-JP",
            _ => "zh-Hans"
        };

        App.InitializeLanguages(new CultureInfo(culture));
        ViewModel.RefreshLocalizedText();
        if (ImportDrawerHost.IsDrawerOpen)
        {
            _refreshLanguageWhenDrawerCloses = true;
            return;
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseForLanguageChange()
    {
        IsReplacingForLanguageChange = true;
        _canClose = true;
        Close();
    }

    private void OpenLink_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string url })
            return;

        IAppHost.GetService<Services.Desktop.IExternalLauncher>().TryOpenUri(url);
    }

    private void ImportRoster_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenImportDrawer(new SettingsPages.ListManagement.RollCallListImportView(
            ViewModel.SelectedStudentListName,
            students => _ = ImportStudentsAsync(students)));
    }

    private void ImportPrizePool_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenImportDrawer(new SettingsPages.ListManagement.LotteryListImportView(
            ViewModel.SelectedPrizeListName,
            prizes => _ = ImportPrizesAsync(prizes)));
    }

    private void RefreshStudentLists_OnClick(object? sender, RoutedEventArgs e) => ViewModel.RefreshListSelectors();

    private void RefreshPrizeLists_OnClick(object? sender, RoutedEventArgs e) => ViewModel.RefreshListSelectors();

    private async void AddStudentList_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = await PromptListNameAsync(LR.C_AddStudentListTitle, LR.C_AddList, LR.C_DefaultStudentListName);
        if (name is not null)
            CreateList(name, ViewModel.StudentListNames, ViewModel.CreateStudentList);
    }

    private async void AddPrizeList_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = await PromptListNameAsync(LR.C_AddPrizeListTitle, LR.C_AddList, LR.C_DefaultPrizeListName);
        if (name is not null)
            CreateList(name, ViewModel.PrizeListNames, ViewModel.CreatePrizeList);
    }

    private async void RenameStudentList_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = await PromptListNameAsync(LR.C_RenameListTitle, LR.C_RenameList, ViewModel.SelectedStudentListName);
        if (name is not null && name != ViewModel.SelectedStudentListName)
            CreateList(name, ViewModel.StudentListNames, ViewModel.RenameStudentList, ViewModel.SelectedStudentListName);
    }

    private async void RenamePrizeList_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = await PromptListNameAsync(LR.C_RenameListTitle, LR.C_RenameList, ViewModel.SelectedPrizeListName);
        if (name is not null && name != ViewModel.SelectedPrizeListName)
            CreateList(name, ViewModel.PrizeListNames, ViewModel.RenamePrizeList, ViewModel.SelectedPrizeListName);
    }

    private async void DeleteStudentList_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDeleteListAsync(ViewModel.SelectedStudentListName, ViewModel.StudentListNames.Count))
            return;

        ViewModel.DeleteStudentList();
    }

    private async void DeletePrizeList_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDeleteListAsync(ViewModel.SelectedPrizeListName, ViewModel.PrizeListNames.Count))
            return;

        ViewModel.DeletePrizeList();
    }

    private void CreateList(string name, IEnumerable<string> existingNames, Action<string> action, string? currentName = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _ = ShowDialogAsync(LR.C_ListNameTitle, LR.M_ListNameEmpty);
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _ = ShowDialogAsync(LR.C_ListNameTitle, LR.M_ListNameInvalid);
            return;
        }

        if (existingNames.Any(existingName => existingName != currentName && existingName == name))
        {
            _ = ShowDialogAsync(LR.C_ListNameTitle, LR.M_ListNameExists);
            return;
        }

        action(name);
    }

    private async Task<string?> PromptListNameAsync(string title, string primaryButtonText, string initialName)
    {
        var input = new TextBox { Text = initialName, PlaceholderText = LR.C_ListNamePlaceholder, MinWidth = 320 };
        var result = await new FAContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(this);
        return result == FAContentDialogResult.Primary ? input.Text?.Trim() : null;
    }

    private async Task<bool> ConfirmDeleteListAsync(string listName, int listCount)
    {
        if (listCount <= 1)
        {
            await ShowDialogAsync(LR.C_DeleteListTitle, LR.M_KeepOneList);
            return false;
        }

        var result = await new FAContentDialog
        {
            Title = LR.C_DeleteListTitle,
            Content = string.Format(LR.M_DeleteListContent, listName),
            PrimaryButtonText = LR.C_DeleteList,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(this);
        return result == FAContentDialogResult.Primary;
    }

    private async Task ImportStudentsAsync(IReadOnlyList<Shared.Models.Profile.Student> students)
    {
        if (!await ConfirmListOverwriteAsync(ViewModel.SelectedStudentListName, ViewModel.SelectedStudentListCount, students.Count))
            return;

        ViewModel.ImportStudents(students);
        CloseImportDrawer();
    }

    private async Task ImportPrizesAsync(IReadOnlyList<Shared.Models.Profile.Prize> prizes)
    {
        if (!await ConfirmListOverwriteAsync(ViewModel.SelectedPrizeListName, ViewModel.SelectedPrizeListCount, prizes.Count))
            return;

        ViewModel.ImportPrizes(prizes);
        CloseImportDrawer();
    }

    private async Task<bool> ConfirmListOverwriteAsync(string listName, int currentCount, int importCount)
    {
        if (currentCount == 0)
            return true;

        var result = await new FAContentDialog
        {
            Title = LR.C_OverwriteTitle,
            Content = string.Format(LR.M_OverwriteListContent, listName, currentCount, importCount),
            PrimaryButtonText = LR.C_Overwrite,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(this);
        return result == FAContentDialogResult.Primary;
    }

    private void OpenImportDrawer(Control importView)
    {
        switch (importView)
        {
            case SettingsPages.ListManagement.RollCallListImportView rollCallImport:
                rollCallImport.CloseHandler = CloseImportDrawer;
                break;
            case SettingsPages.ListManagement.LotteryListImportView lotteryImport:
                lotteryImport.CloseHandler = CloseImportDrawer;
                break;
        }

        ImportDrawerHost.DrawerContent = importView;
        ImportDrawerHost.IsDrawerOpen = true;
    }

    private void CloseImportDrawer()
    {
        ImportDrawerHost.IsDrawerOpen = false;
        ImportDrawerHost.DrawerContent = null;
        if (!_refreshLanguageWhenDrawerCloses)
            return;

        _refreshLanguageWhenDrawerCloses = false;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void ImportSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        var path = await PickPathAsync(LR.C_ImportSettingsTitle, "*.json", LR.C_JsonFileType);
        if (path is not null)
            await ImportAsync(path, isAllData: false);
    }

    private async void ImportAllData_OnClick(object? sender, RoutedEventArgs e)
    {
        var path = await PickPathAsync(LR.C_ImportAllDataTitle, "*.zip", LR.C_ZipFileType);
        if (path is not null)
            await ImportAsync(path, isAllData: true);
    }

    private async Task ImportAsync(string path, bool isAllData)
    {
        try
        {
            var inspection = isAllData
                ? await ImportExportService.InspectAllDataAsync(path)
                : await ImportExportService.InspectSettingsAsync(path);
            if (!inspection.IsSupportedV3)
            {
                await ShowDialogAsync(LR.M_UnsupportedImportTitle, BuildUnsupportedMessage(inspection));
                return;
            }

            var confirmed = await new FAContentDialog
            {
                Title = LR.C_ImportTitle,
                Content = string.Format(LR.M_ImportConfirmation, inspection.ProducerVersion, inspection.FileCount),
                PrimaryButtonText = LR.C_Import,
                CloseButtonText = LR.C_Cancel,
                DefaultButton = FAContentDialogButton.Close
            }.ShowAsync(this);
            if (confirmed != FAContentDialogResult.Primary)
                return;

            if (isAllData)
                await ImportExportService.ImportAllDataAsync(path);
            else
                await ImportExportService.ImportSettingsAsync(path);

            ViewModel.RefreshFromConfig();
            ViewModel.StatusMessage = LR.M_ImportCompleted;
        }
        catch (Exception ex)
        {
            await ShowDialogAsync(LR.M_ImportFailed, ex.Message);
        }
    }

    private async Task<string?> PickPathAsync(string title, string pattern, string fileType)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(fileType) { Patterns = [pattern] }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        await new FAContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(this);
    }

    private static string BuildUnsupportedMessage(ImportInspection inspection)
    {
        var version = string.IsNullOrWhiteSpace(inspection.ProducerVersion) ? LR.M_UnknownVersion : inspection.ProducerVersion;
        var details = inspection.Warnings.Count == 0 ? LR.M_UnsupportedImportDetail : string.Join(Environment.NewLine, inspection.Warnings);
        return string.Format(LR.M_UnsupportedImport, version, details);
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_canClose)
            return;

        e.Cancel = true;
        _ = ConfirmExitAsync();
    }

    private void WindowOnClosed(object? sender, EventArgs e)
    {
        Closed -= WindowOnClosed;
    }

    private async Task ConfirmExitAsync()
    {
        var result = await new FAContentDialog
        {
            Title = LR.C_ExitTitle,
            Content = LR.C_ExitDescription,
            PrimaryButtonText = LR.C_Exit,
            CloseButtonText = LR.C_ContinueSetup,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(this);
        if (result != FAContentDialogResult.Primary)
            return;

        _canClose = true;
        App.Current.Stop();
        Close();
    }
}
