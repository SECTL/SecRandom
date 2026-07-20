using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Linkage;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.Linkage.Resources;

namespace SecRandom.Views.SettingsPages.Linkage;

[PageInfo("settings.linkage", FluentIcons.CalendarLtrFilled)]
public partial class LinkageSettingsPage : UserControl
{
    private bool _isSubscribed;
    public static readonly StyledProperty<string> CsesSummaryProperty =
        AvaloniaProperty.Register<LinkageSettingsPage, string>(nameof(CsesSummary));

    public LinkageSettingsPage()
    {
        Settings = ViewModel.Config.LinkageSettings;
        DataContext = this;
        InitializeComponent();
        RefreshCsesSummary();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public LinkageSettingsConfig Settings { get; }
    public string CsesSummary
    {
        get => GetValue(CsesSummaryProperty);
        private set => SetValue(CsesSummaryProperty, value);
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private ICsesScheduleStore CsesStore { get; } = IAppHost.GetService<ICsesScheduleStore>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
            return;
        _isSubscribed = true;
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        CsesStore.ScheduleChanged += CsesStoreOnScheduleChanged;
        RefreshCsesSummary();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
            return;
        _isSubscribed = false;
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        CsesStore.ScheduleChanged -= CsesStoreOnScheduleChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
        _ = IAppHost.GetService<CourseLinkageService>().RefreshAsync();
    }

    private void CsesStoreOnScheduleChanged(object? sender, EventArgs e)
    {
        RefreshCsesSummary();
    }

    private async void ImportCses_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = (await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LR.C_CsesImport,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSES YAML")
                {
                    Patterns = ["*.yml", "*.yaml"],
                    MimeTypes = ["application/x-yaml", "text/yaml", "text/plain"]
                }
            ]
        })).FirstOrDefault();
        if (file is null)
            return;

        string? temporaryPath = null;
        try
        {
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                temporaryPath = Path.Combine(Path.GetTempPath(), $"SecRandom-{Guid.NewGuid():N}{Path.GetExtension(file.Name)}");
                await using var source = await file.OpenReadAsync();
                await using var target = File.Create(temporaryPath);
                await source.CopyToAsync(target);
                path = temporaryPath;
            }

            var schedule = await CsesStore.ImportAsync(path);
            CsesSummary = FormatCsesSummary(schedule);
            this.ShowSuccessToast(string.Format(CultureInfo.CurrentCulture, LR.M_CsesImported, CsesSummary));
        }
        catch (InvalidDataException exception)
        {
            this.ShowErrorToast(FormatCsesError(exception));
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async void ViewCses_OnClick(object? sender, RoutedEventArgs e)
    {
        await new FAContentDialog
        {
            Title = LR.S_Cses,
            Content = CsesSummary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private async void ClearCses_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CsesStore.Load() is null)
        {
            this.ShowWarningToast(LR.M_CsesMissing);
            return;
        }

        var result = await new FAContentDialog
        {
            Title = LR.M_CsesClearTitle,
            Content = LR.M_CsesClearContent,
            PrimaryButtonText = LR.C_Confirm,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return;

        CsesStore.Clear();
        CsesSummary = LR.M_CsesMissing;
        this.ShowSuccessToast(LR.M_CsesCleared);
    }

    private void RefreshCsesSummary()
    {
        var schedule = CsesStore.Load();
        CsesSummary = schedule is null ? LR.M_CsesMissing : FormatCsesSummary(schedule);
    }

    private static string FormatCsesSummary(CsesSchedule schedule) => string.Format(
        CultureInfo.CurrentCulture,
        LR.ResourceManager.GetString("M_CsesSummary", LR.Culture) ?? "{0} {1:HH:mm} {2:HH:mm}",
        schedule.PeriodCount,
        schedule.Earliest,
        schedule.Latest);

    private static string FormatCsesError(InvalidDataException exception)
    {
        if (!CsesScheduleException.TryGetError(exception, out var error, out var argument))
            return exception.Message;

        var key = error switch
        {
            CsesScheduleError.Empty => "M_CsesErrorEmpty",
            CsesScheduleError.RootNotObject => "M_CsesErrorRoot",
            CsesScheduleError.NoValidItems => "M_CsesErrorNoItems",
            CsesScheduleError.InvalidItem => "M_CsesErrorInvalidItem",
            CsesScheduleError.InvalidTime => "M_CsesErrorInvalidTime",
            CsesScheduleError.OverlappingItems => "M_CsesErrorOverlap",
            _ => throw new ArgumentOutOfRangeException(nameof(exception))
        };
        var format = LR.ResourceManager.GetString(key, LR.Culture) ?? key;
        return argument is null
            ? format
            : string.Format(CultureInfo.CurrentCulture, format, argument);
    }
}
