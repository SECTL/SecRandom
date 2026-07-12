using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Plugins;
using SecRandom.Services.Plugins;
using LR = SecRandom.Langs.SettingsPages.Plugins.Overview.Resources;

namespace SecRandom.Views.SettingsPages.Plugins.Overview;

[PageInfo("settings.plugin", FluentIcons.AppsListFilled, useFullWidth: true, hidePageTitle: true)]
public partial class PluginsSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly IPluginManager _pluginManager = IAppHost.GetService<IPluginManager>();
    private readonly IPluginCatalogService _pluginCatalog = IAppHost.GetService<IPluginCatalogService>();
    private readonly PluginSelectionState _selectionState = IAppHost.GetService<PluginSelectionState>();
    private PluginOverviewItem? _selectedItem;
    private PluginCatalogMirror? _selectedCatalogMirror;
    private PluginCatalogSource? _selectedCatalogSource;
    private string _searchText = string.Empty;
    private string _sourceEditorUrl = string.Empty;
    private string _sourceEditorMirrorUrl = string.Empty;
    private PluginOverviewFilter _filter = PluginOverviewFilter.Market;
    private bool _isImporting;
    private bool _isCatalogBusy;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public PluginsSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        RefreshCatalogSources();
        RefreshCatalogEntries();
        RefreshPlugins();
    }

    public ObservableCollection<PluginDescriptor> InstalledPlugins { get; } = [];
    public ObservableCollection<PluginOverviewItem> PluginList { get; } = [];
    public ObservableCollection<PluginCatalogEntry> CatalogEntries { get; } = [];
    public ObservableCollection<PluginCatalogMirror> CatalogMirrors { get; } = [];
    public ObservableCollection<PluginCatalogSource> CatalogSources { get; } = [];
    public ObservableCollection<PluginFilterChip> FilterChips { get; } =
    [
        new(PluginOverviewFilter.Market, LR.C_FilterMarket),
        new(PluginOverviewFilter.Installed, LR.C_FilterInstalled),
        new(PluginOverviewFilter.NotInstalled, LR.C_FilterNotInstalled)
    ];

    public int TotalPluginCount => PluginList.Count;
    public int EnabledPluginCount => InstalledPlugins.Count(x => x.IsEnabled && x.Status != PluginStatus.LoadFailed && x.Status != PluginStatus.Incompatible);
    public int LoadedPluginCount => InstalledPlugins.Count(x => x.Status == PluginStatus.Loaded);
    public int PendingRestartPluginCount => InstalledPlugins.Count(x => x.RequiresRestart || x.Status == PluginStatus.PendingRestart);
    public int ErrorPluginCount => InstalledPlugins.Count(x => x.Status is PluginStatus.LoadFailed or PluginStatus.Incompatible);
    public int DisabledPluginCount => InstalledPlugins.Count(x => !x.IsEnabled && x.Status == PluginStatus.Disabled);
    public int VisiblePluginCount => PluginList.Count;
    public string VisiblePluginCountText => string.Format(LR.C_PluginCount, VisiblePluginCount);
    public bool IsPluginListEmpty => PluginList.Count == 0;
    public bool IsCatalogEmpty => CatalogEntries.Count == 0;
    public bool CanSaveCatalogSource => !string.IsNullOrWhiteSpace(SourceEditorUrl);
    public bool CanEditSelectedCatalogSource => SelectedCatalogSource != null;

    public bool IsImporting
    {
        get => _isImporting;
        private set => SetField(ref _isImporting, value);
    }

    public bool IsCatalogBusy
    {
        get => _isCatalogBusy;
        private set => SetField(ref _isCatalogBusy, value);
    }

    public PluginCatalogMirror? SelectedCatalogMirror
    {
        get => _selectedCatalogMirror;
        set
        {
            if (!SetField(ref _selectedCatalogMirror, value) || value == null)
                return;

            _pluginCatalog.SelectMirror(value.Id);
        }
    }

    public PluginCatalogSource? SelectedCatalogSource
    {
        get => _selectedCatalogSource;
        set
        {
            if (!SetField(ref _selectedCatalogSource, value))
                return;

            SourceEditorUrl = value?.Url ?? string.Empty;
            SourceEditorMirrorUrl = value?.MirrorUrl ?? string.Empty;
            OnPropertyChanged(nameof(CanEditSelectedCatalogSource));
        }
    }

    public string SourceEditorUrl
    {
        get => _sourceEditorUrl;
        set
        {
            if (!SetField(ref _sourceEditorUrl, value))
                return;

            OnPropertyChanged(nameof(CanSaveCatalogSource));
        }
    }

    public string SourceEditorMirrorUrl
    {
        get => _sourceEditorMirrorUrl;
        set => SetField(ref _sourceEditorMirrorUrl, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value))
                return;

            ApplyFilterAndSelection();
        }
    }

    public PluginOverviewFilter Filter
    {
        get => _filter;
        set
        {
            if (!SetField(ref _filter, value))
                return;

            ApplyFilterAndSelection(SelectedItem?.Id);
            OnPropertyChanged(nameof(SelectedFilterChip));
        }
    }

    public PluginFilterChip? SelectedFilterChip
    {
        get => FilterChips.FirstOrDefault(x => x.Filter == Filter);
        set
        {
            if (value == null || value.Filter == Filter)
                return;

            Filter = value.Filter;
            OnPropertyChanged();
        }
    }

    public PluginOverviewItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetField(ref _selectedItem, value))
                return;

            _selectionState.SelectedPluginId = value?.Id;
            OnPropertyChanged(nameof(HasSelectedPlugin));
            OnPropertyChanged(nameof(HasNoSelectedPlugin));
            OnPropertyChanged(nameof(SelectedPluginTitle));
            OnPropertyChanged(nameof(SelectedPluginSummary));
            OnPropertyChanged(nameof(SelectedPluginMetaLine));
            OnPropertyChanged(nameof(SelectedPluginDescription));
            OnPropertyChanged(nameof(SelectedPluginDirectory));
            OnPropertyChanged(nameof(SelectedPluginStatus));
            OnPropertyChanged(nameof(SelectedPluginError));
            OnPropertyChanged(nameof(HasSelectedPluginError));
            OnPropertyChanged(nameof(SelectedPluginReadme));
            OnPropertyChanged(nameof(SelectedPluginAuthor));
            OnPropertyChanged(nameof(SelectedPluginApiVersion));
            OnPropertyChanged(nameof(SelectedPluginMinimumHostVersion));
            OnPropertyChanged(nameof(CanEnableSelectedPlugin));
            OnPropertyChanged(nameof(CanDisableSelectedPlugin));
            OnPropertyChanged(nameof(CanInstallSelectedPlugin));
            OnPropertyChanged(nameof(CanOpenSelectedFolder));
            OnPropertyChanged(nameof(CanToggleSelectedPlugin));
            OnPropertyChanged(nameof(IsSelectedPluginEnabled));
            OnPropertyChanged(nameof(SelectedPluginStatsText));
        }
    }

    public bool HasSelectedPlugin => SelectedItem != null;
    public bool HasNoSelectedPlugin => SelectedItem == null;
    public bool CanEnableSelectedPlugin => SelectedItem is { IsInstalled: true, InstalledPlugin.IsEnabled: false };
    public bool CanDisableSelectedPlugin => SelectedItem is { IsInstalled: true, InstalledPlugin.IsEnabled: true };
    public bool CanInstallSelectedPlugin => SelectedItem is { IsCatalog: true };
    public bool CanOpenSelectedFolder => SelectedItem is { IsInstalled: true };
    public bool CanToggleSelectedPlugin => SelectedItem is { IsInstalled: true };
    public string PluginDirectory => PluginManagerService.PluginsDirectory;

    public string SelectedPluginTitle => SelectedItem == null
        ? LR.C_NoPluginSelected
        : SelectedItem.Name;

    public string SelectedPluginSummary => SelectedItem == null
        ? LR.Page_Title
        : $"{SelectedPluginStatus} · {SelectedPluginAuthor}";

    public string SelectedPluginMetaLine => SelectedItem == null
        ? string.Empty
        : $"{SelectedItem.Version} | {SelectedPluginAuthor}";
    public string SelectedPluginStatsText => SelectedItem?.StatsText ?? string.Empty;
    public bool IsSelectedPluginEnabled
    {
        get => SelectedItem?.InstalledPlugin?.IsEnabled == true;
        set
        {
            if (SelectedItem?.InstalledPlugin == null || SelectedItem.InstalledPlugin.IsEnabled == value)
                return;

            _pluginManager.SetEnabled(SelectedItem.InstalledPlugin.Id, value);
            SettingsView.Current?.RequestRestartApp();
            RefreshPlugins(SelectedItem.Id);
        }
    }

    public string SelectedPluginDescription => string.IsNullOrWhiteSpace(SelectedItem?.Description)
        ? LR.C_NoDescription
        : SelectedItem.Description;

    public string SelectedPluginAuthor => string.IsNullOrWhiteSpace(SelectedItem?.Author) ? LR.C_Unknown : SelectedItem.Author;
    public string SelectedPluginApiVersion => SelectedItem?.ApiVersion ?? "-";
    public string SelectedPluginMinimumHostVersion => SelectedItem?.MinimumHostVersion ?? "-";
    public string SelectedPluginDirectory => SelectedItem?.DirectoryPath ?? "-";
    public string SelectedPluginStatus => SelectedItem == null
        ? "-"
        : SelectedItem.StatusText;
    public string SelectedPluginError => string.IsNullOrWhiteSpace(SelectedItem?.ErrorMessage)
        ? LR.C_None
        : SelectedItem.ErrorMessage;
    public bool HasSelectedPluginError => !string.IsNullOrWhiteSpace(SelectedItem?.ErrorMessage);
    public string SelectedPluginReadme => BuildSelectedPluginReadme();

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        RefreshCatalogEntries();
        RefreshPlugins(_selectionState.SelectedPluginId);
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshPlugins(SelectedItem?.Id);
    }

    private async void RefreshCatalogButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshCatalogAsync();
    }

    private void AddCatalogSourceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var source = _pluginCatalog.AddSource(SourceEditorUrl, SourceEditorMirrorUrl);
            RefreshCatalogSources();
            SelectedCatalogSource = CatalogSources.FirstOrDefault(x => x.Id == source.Id);
        }
        catch (Exception ex)
        {
            this.ShowWarningToast(string.Format(LR.M_SourceAddFailed, ex.Message));
        }
    }

    private void UpdateCatalogSourceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedCatalogSource == null)
            return;

        try
        {
            var source = _pluginCatalog.UpdateSource(SelectedCatalogSource.Id, SourceEditorUrl, SourceEditorMirrorUrl);
            RefreshCatalogSources();
            SelectedCatalogSource = CatalogSources.FirstOrDefault(x => x.Id == source.Id);
        }
        catch (Exception ex)
        {
            this.ShowWarningToast(string.Format(LR.M_SourceAddFailed, ex.Message));
        }
    }

    private void DeleteCatalogSourceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedCatalogSource is not { } source)
            return;

        try
        {
            _pluginCatalog.RemoveSource(source.Id);
            RefreshCatalogSources();
            SelectedCatalogSource = CatalogSources.FirstOrDefault();
        }
        catch (Exception ex)
        {
            this.ShowWarningToast(string.Format(LR.M_SourceAddFailed, ex.Message));
        }
    }

    private async void InstallSelectedCatalogPluginButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem?.CatalogEntry == null)
            return;

        await InstallCatalogEntryAsync(SelectedItem.CatalogEntry);
    }

    private async Task InstallCatalogEntryAsync(PluginCatalogEntry entry)
    {
        IsCatalogBusy = true;
        try
        {
            var importedPluginId = await _pluginCatalog.InstallAsync(entry, _pluginManager);
            SettingsView.Current?.RequestRestartApp();
            RefreshPlugins(importedPluginId);
            this.ShowSuccessToast(LR.M_PluginImported);
        }
        catch (PluginImportException ex) when (ex.Reason is PluginImportFailureReason.AlreadyExists)
        {
            this.ShowWarningToast(LR.M_PluginImportExists);
        }
        catch (PluginImportException ex) when (ex.Reason is PluginImportFailureReason.InvalidPackage)
        {
            this.ShowWarningToast(LR.M_InvalidPluginPackage);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(string.Format(LR.M_PluginImportFailed, ex.Message));
        }
        finally
        {
            IsCatalogBusy = false;
        }
    }

    private async void ImportPluginButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LR.M_SelectPluginPackageTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LR.M_SecRandomPluginPackageFileType)
                {
                    Patterns = ["*.srpx"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsImporting = true;
        try
        {
            var importedPluginId = _pluginManager.ImportPluginPackage(path);
            SettingsView.Current?.RequestRestartApp();
            RefreshPlugins(importedPluginId);
            this.ShowSuccessToast(LR.M_PluginImported);
        }
        catch (PluginImportException ex) when (ex.Reason is PluginImportFailureReason.AlreadyExists)
        {
            this.ShowWarningToast(LR.M_PluginImportExists);
        }
        catch (PluginImportException ex) when (ex.Reason is PluginImportFailureReason.InvalidFolder or PluginImportFailureReason.InvalidManifest)
        {
            this.ShowWarningToast(LR.M_InvalidPluginFolder);
        }
        catch (PluginImportException ex) when (ex.Reason is PluginImportFailureReason.InvalidPackage)
        {
            this.ShowWarningToast(LR.M_InvalidPluginPackage);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(string.Format(LR.M_PluginImportFailed, ex.Message));
        }
        finally
        {
            IsImporting = false;
        }
    }

    private void OpenPluginsFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = Path.Combine(PluginDirectory),
            UseShellExecute = true
        });
    }

    private void OpenSelectedFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem?.InstalledPlugin == null)
            return;

        Process.Start(new ProcessStartInfo()
        {
            FileName = Path.Combine(SelectedItem.InstalledPlugin.DirectoryPath),
            UseShellExecute = true
        });
    }

    private void EnableSelectedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem?.InstalledPlugin == null)
            return;

        _pluginManager.SetEnabled(SelectedItem.InstalledPlugin.Id, true);
        SettingsView.Current?.RequestRestartApp();
        RefreshPlugins(SelectedItem.Id);
    }

    private void DisableSelectedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem?.InstalledPlugin == null)
            return;

        _pluginManager.SetEnabled(SelectedItem.InstalledPlugin.Id, false);
        SettingsView.Current?.RequestRestartApp();
        RefreshPlugins(SelectedItem.Id);
    }

    private void RefreshPlugins(string? preferredPluginId = null)
    {
        _pluginManager.Refresh();
        InstalledPlugins.Clear();

        foreach (var plugin in _pluginManager.Plugins.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            InstalledPlugins.Add(plugin);

        OnPropertyChanged(nameof(TotalPluginCount));
        OnPropertyChanged(nameof(EnabledPluginCount));
        OnPropertyChanged(nameof(LoadedPluginCount));
        OnPropertyChanged(nameof(PendingRestartPluginCount));
        OnPropertyChanged(nameof(ErrorPluginCount));
        OnPropertyChanged(nameof(DisabledPluginCount));
        ApplyFilterAndSelection(preferredPluginId);
    }

    private async Task RefreshCatalogAsync()
    {
        IsCatalogBusy = true;
        try
        {
            await _pluginCatalog.RefreshAsync();
            RefreshCatalogEntries();
            ApplyFilterAndSelection(SelectedItem?.Id);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(string.Format(LR.M_CatalogRefreshFailed, ex.Message));
        }
        finally
        {
            IsCatalogBusy = false;
        }
    }

    private void RefreshCatalogEntries()
    {
        CatalogEntries.Clear();
        foreach (var entry in _pluginCatalog.Entries)
            CatalogEntries.Add(entry);

        OnPropertyChanged(nameof(IsCatalogEmpty));
    }

    private void RefreshCatalogSources()
    {
        var selectedSourceId = SelectedCatalogSource?.Id;

        CatalogMirrors.Clear();
        foreach (var mirror in _pluginCatalog.Mirrors)
            CatalogMirrors.Add(mirror);

        CatalogSources.Clear();
        foreach (var source in _pluginCatalog.Sources)
            CatalogSources.Add(source);

        SelectedCatalogMirror = CatalogMirrors.FirstOrDefault(x => x.Id == _pluginCatalog.SelectedMirror.Id);
        SelectedCatalogSource = CatalogSources.FirstOrDefault(x => x.Id == selectedSourceId) ?? CatalogSources.FirstOrDefault();
    }

    private void OpenCatalogSourcesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.OpenDrawer(BuildCatalogSourcesDrawer());
    }

    private Control BuildCatalogSourcesDrawer()
    {
        ComboBox builtInMirrorBox = new()
        {
            ItemsSource = CatalogMirrors.ToList(),
            SelectedItem = SelectedCatalogMirror,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        builtInMirrorBox.SelectionChanged += (_, _) => SelectedCatalogMirror = builtInMirrorBox.SelectedItem as PluginCatalogMirror;

        CheckBox ignoreTlsCheckBox = new()
        {
            Content = LR.C_IgnoreTlsCertificateErrors,
            IsChecked = _pluginCatalog.IgnoreTlsCertificateErrors
        };
        ignoreTlsCheckBox.IsCheckedChanged += (_, _) =>
            _pluginCatalog.SetIgnoreTlsCertificateErrors(ignoreTlsCheckBox.IsChecked == true);

        ListBox customSourceList = new()
        {
            ItemsSource = CatalogSources.ToList(),
            SelectedItem = SelectedCatalogSource,
            MinHeight = CatalogSources.Count == 0 ? 36 : 96,
            MaxHeight = 220,
            Margin = new Avalonia.Thickness(0, 0, 0, 2)
        };
        customSourceList.SelectionChanged += (_, _) =>
        {
            if (customSourceList.SelectedItem is PluginCatalogSource source)
                SelectedCatalogSource = source;
        };
        customSourceList.ItemTemplate = new FuncDataTemplate<PluginCatalogSource>((source, _) =>
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 12,
                Margin = new Avalonia.Thickness(0, 4)
            };
            grid.Children.Add(new TextBlock
            {
                Text = source?.Url ?? string.Empty,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
            var mirrorText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(source?.MirrorUrl) ? "-" : source.MirrorUrl,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(mirrorText, 1);
            grid.Children.Add(mirrorText);
            return grid;
        });

        TextBox sourceUrlBox = new()
        {
            PlaceholderText = LR.C_SourceUrl,
            Text = SourceEditorUrl
        };
        sourceUrlBox.TextChanged += (_, _) => SourceEditorUrl = sourceUrlBox.Text ?? string.Empty;

        TextBox sourceMirrorUrlBox = new()
        {
            PlaceholderText = LR.C_SourceMirrorUrl,
            Text = SourceEditorMirrorUrl
        };
        sourceMirrorUrlBox.TextChanged += (_, _) => SourceEditorMirrorUrl = sourceMirrorUrlBox.Text ?? string.Empty;

        Button addButton = new()
        {
            Content = LR.C_Add,
            IsEnabled = CanSaveCatalogSource
        };
        addButton.Click += (_, _) =>
        {
            AddCatalogSourceButton_OnClick(null, new RoutedEventArgs());
            customSourceList.ItemsSource = CatalogSources.ToList();
        };

        Button updateButton = new()
        {
            Content = LR.C_UpdateSource,
            IsEnabled = CanEditSelectedCatalogSource
        };
        updateButton.Click += (_, _) =>
        {
            UpdateCatalogSourceButton_OnClick(null, new RoutedEventArgs());
            customSourceList.ItemsSource = CatalogSources.ToList();
        };

        Button deleteButton = new()
        {
            Content = LR.C_Delete,
            IsEnabled = CanEditSelectedCatalogSource
        };
        deleteButton.Click += (_, _) =>
        {
            DeleteCatalogSourceButton_OnClick(null, new RoutedEventArgs());
            customSourceList.ItemsSource = CatalogSources.ToList();
        };

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(CanEditSelectedCatalogSource))
            {
                updateButton.IsEnabled = CanEditSelectedCatalogSource;
                deleteButton.IsEnabled = CanEditSelectedCatalogSource;
            }
            if (args.PropertyName == nameof(CanSaveCatalogSource))
                addButton.IsEnabled = CanSaveCatalogSource;
            if (args.PropertyName == nameof(SourceEditorUrl))
                sourceUrlBox.Text = SourceEditorUrl;
            if (args.PropertyName == nameof(SourceEditorMirrorUrl))
                sourceMirrorUrlBox.Text = SourceEditorMirrorUrl;
            if (args.PropertyName == nameof(SelectedCatalogMirror))
                builtInMirrorBox.SelectedItem = SelectedCatalogMirror;
            if (args.PropertyName == nameof(SelectedCatalogSource))
            {
                customSourceList.SelectedItem = SelectedCatalogSource;
            }
        };
        NotifyPropertyChanged += handler;

        StackPanel buttons = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        buttons.Children.Add(addButton);
        buttons.Children.Add(updateButton);
        buttons.Children.Add(deleteButton);

        var customHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };
        customHeader.Children.Add(new TextBlock
        {
            Text = LR.C_SourceUrlColumn,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        var mirrorHeader = new TextBlock
        {
            Text = LR.C_SourceMirrorColumn,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        Grid.SetColumn(mirrorHeader, 1);
        customHeader.Children.Add(mirrorHeader);

        var sourceEditor = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Margin = new Avalonia.Thickness(0, 2, 0, 0)
        };
        sourceEditor.Children.Add(sourceUrlBox);
        Grid.SetColumn(sourceMirrorUrlBox, 1);
        sourceEditor.Children.Add(sourceMirrorUrlBox);

        StackPanel panel = new()
        {
            Width = 420,
            Margin = new Avalonia.Thickness(18),
            Spacing = 10
        };
        panel.Children.Add(new TextBlock
        {
            Text = LR.C_ManagePluginSources,
            FontSize = 20,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = LR.C_BuiltInPluginSourceMirror,
            FontSize = 15,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        });
        panel.Children.Add(builtInMirrorBox);
        panel.Children.Add(ignoreTlsCheckBox);
        panel.Children.Add(new Separator { Margin = new Avalonia.Thickness(0, 8, 0, 8) });
        panel.Children.Add(new TextBlock
        {
            Text = LR.C_CustomPluginSource,
            FontSize = 15,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        panel.Children.Add(sourceEditor);
        panel.Children.Add(buttons);
        panel.Children.Add(customHeader);
        panel.Children.Add(customSourceList);
        panel.DetachedFromVisualTree += (_, _) => NotifyPropertyChanged -= handler;
        return panel;
    }

    private void ApplyFilterAndSelection(string? preferredPluginId = null)
    {
        var installedIds = InstalledPlugins.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        IEnumerable<PluginOverviewItem> items = InstalledPlugins.Select(plugin =>
            PluginOverviewItem.FromInstalled(plugin, FormatStatus(plugin.Status, plugin.IsEnabled, plugin.RequiresRestart)));

        items = items.Concat(CatalogEntries
            .Where(entry => !installedIds.Contains(entry.Id))
            .Select(entry => PluginOverviewItem.FromCatalog(entry, LR.S_StatusAvailable)));

        IEnumerable<PluginOverviewItem> filtered = string.IsNullOrWhiteSpace(SearchText)
            ? items
            : items.Where(MatchesSearch);

        filtered = Filter switch
        {
            PluginOverviewFilter.Installed => filtered.Where(x => x.IsInstalled),
            PluginOverviewFilter.NotInstalled => filtered.Where(x => x.IsCatalog),
            _ => filtered
        };

        var filteredList = filtered
            .OrderByDescending(x => x.IsInstalled)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        PluginList.Clear();
        foreach (var plugin in filteredList)
            PluginList.Add(plugin);

        OnPropertyChanged(nameof(IsPluginListEmpty));
        OnPropertyChanged(nameof(VisiblePluginCount));
        OnPropertyChanged(nameof(VisiblePluginCountText));
        OnPropertyChanged(nameof(TotalPluginCount));

        var preferred = filteredList.FirstOrDefault(x => x.Id == preferredPluginId);
        var current = filteredList.FirstOrDefault(x => x.Id == SelectedItem?.Id);
        SelectedItem = preferred ?? current;
    }

    private bool MatchesSearch(PluginOverviewItem plugin)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var query = SearchText.Trim();
        return plugin.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               plugin.Id.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               plugin.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               plugin.Version.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               plugin.StatusText.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               plugin.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private string BuildSelectedPluginReadme()
    {
        if (SelectedItem == null)
            return string.Empty;

        if (SelectedItem.IsInstalled && !string.IsNullOrWhiteSpace(SelectedItem.DirectoryPath))
        {
            foreach (var fileName in new[] { "README.md", "Readme.md", "readme.md", "README.txt", "readme.txt" })
            {
                var path = Path.Combine(SelectedItem.DirectoryPath, fileName);
                if (File.Exists(path))
                    return File.ReadAllText(path, Encoding.UTF8);
            }
        }

        if (!string.IsNullOrWhiteSpace(SelectedItem.Readme))
            return SelectedItem.Readme;

        return $"# {SelectedItem.Name}{Environment.NewLine}{Environment.NewLine}{SelectedPluginDescription}";
    }

    private static string FormatStatus(PluginStatus status, bool isEnabled, bool requiresRestart)
    {
        if (requiresRestart)
            return LR.S_StatusPendingRestart;

        return status switch
        {
            PluginStatus.Discovered => isEnabled ? LR.S_StatusDiscovered : LR.S_StatusNotEnabled,
            PluginStatus.Disabled => LR.S_StatusDisabled,
            PluginStatus.Loaded => LR.S_StatusLoaded,
            PluginStatus.LoadFailed => LR.S_StatusLoadFailed,
            PluginStatus.Incompatible => LR.S_StatusIncompatible,
            PluginStatus.PendingRestart => LR.S_StatusPendingRestart,
            _ => status.ToString()
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum PluginOverviewFilter
{
    Market,
    Installed,
    NotInstalled
}

public sealed record PluginFilterChip(PluginOverviewFilter Filter, string Text);

public sealed class PluginOverviewItem
{
    private PluginOverviewItem()
    {
    }

    public PluginDescriptor? InstalledPlugin { get; private init; }
    public PluginCatalogEntry? CatalogEntry { get; private init; }
    public string Id { get; private init; } = string.Empty;
    public string Name { get; private init; } = string.Empty;
    public string Version { get; private init; } = string.Empty;
    public string Author { get; private init; } = string.Empty;
    public string Description { get; private init; } = string.Empty;
    public string Readme { get; private init; } = string.Empty;
    public string ApiVersion { get; private init; } = string.Empty;
    public string MinimumHostVersion { get; private init; } = string.Empty;
    public string StatusText { get; private init; } = string.Empty;
    public int Stars { get; private init; }
    public int Downloads { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? DirectoryPath => InstalledPlugin?.DirectoryPath;
    public bool IsInstalled => InstalledPlugin != null;
    public bool IsCatalog => CatalogEntry != null;
    public string ListSubtitle => string.IsNullOrWhiteSpace(Author) ? Id : Author;
    public bool HasStats => Stars > 0 || Downloads > 0;
    public string StarsText => FormatCount(Stars);
    public string DownloadsText => FormatCount(Downloads);
    public string StatsText => HasStats ? $"{DownloadsText} / {StarsText}" : string.Empty;

    public static PluginOverviewItem FromInstalled(PluginDescriptor plugin, string statusText)
    {
        return new PluginOverviewItem
        {
            InstalledPlugin = plugin,
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            Author = plugin.Author,
            Description = plugin.Manifest.Description,
            Readme = string.Empty,
            ApiVersion = plugin.Manifest.ApiVersion,
            MinimumHostVersion = plugin.Manifest.MinimumHostVersion,
            StatusText = statusText,
            Stars = 0,
            Downloads = 0,
            ErrorMessage = plugin.ErrorMessage
        };
    }

    public static PluginOverviewItem FromCatalog(PluginCatalogEntry entry, string statusText)
    {
        return new PluginOverviewItem
        {
            CatalogEntry = entry,
            Id = entry.Id,
            Name = entry.DisplayName,
            Version = entry.Version,
            Author = entry.Author,
            Description = entry.Description,
            Readme = entry.Readme,
            ApiVersion = entry.ApiVersion,
            MinimumHostVersion = entry.MinimumHostVersion,
            StatusText = statusText,
            Stars = entry.Stars,
            Downloads = entry.Downloads
        };
    }

    private static string FormatCount(int value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000d:0.#}M";
        if (value >= 1_000)
            return $"{value / 1_000d:0.#}K";
        return value.ToString();
    }
}
