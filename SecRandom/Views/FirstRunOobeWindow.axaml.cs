using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Services.ImportExport;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.FirstRunOobe.Resources;

namespace SecRandom.Views;

public partial class FirstRunOobeWindow : Window
{
    private bool _canClose;
    private bool _isLanguageSelectionReady;
    private bool _refreshLanguageWhenDrawerCloses;

    public FirstRunOobeWindow()
    {
        DataContext = this;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        Closed += WindowOnClosed;
        Opened += (_, _) => _isLanguageSelectionReady = true;
    }

    public FirstRunOobeViewModel ViewModel { get; } = IAppHost.GetService<FirstRunOobeViewModel>();
    public bool IsCompleted { get; private set; }
    public bool IsReplacingForLanguageChange { get; private set; }
    public event EventHandler? Completed;
    public event EventHandler? LanguageChanged;
    private IImportExportService ImportExportService { get; } = IAppHost.GetService<IImportExportService>();

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

    private static void OpenLink_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string url })
            return;

        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else if (OperatingSystem.IsLinux())
            Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
        else if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
    }

    private void ImportRoster_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenImportDrawer(new SettingsPages.ListManagement.RollCallListImportView(
            ViewModel.SelectedStudentListName,
            students =>
            {
                ViewModel.ImportStudents(students);
                CloseImportDrawer();
            }));
    }

    private void ImportPrizePool_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenImportDrawer(new SettingsPages.ListManagement.LotteryListImportView(
            ViewModel.SelectedPrizeListName,
            prizes =>
            {
                ViewModel.ImportPrizes(prizes);
                CloseImportDrawer();
            }));
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

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FirstRunOobeViewModel.SelectedStudentListName) or nameof(FirstRunOobeViewModel.SelectedPrizeListName))
            CloseImportDrawer();
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
        ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
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
