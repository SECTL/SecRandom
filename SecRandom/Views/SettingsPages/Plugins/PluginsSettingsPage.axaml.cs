using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.PluginSdk;
using SecRandom.Services.Desktop;
using SecRandom.Services.Plugins;
using SecRandom.Shared.Models.Plugins;
using LR = SecRandom.Langs.SettingsPages.Plugins.Overview.Resources;

namespace SecRandom.Views.SettingsPages.Plugins;

[PageInfo("settings.plugin", FluentIcons.AppsListFilled, useFullWidth: true, hidePageTitle: true)]
public partial class PluginsSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly IPluginManager _pluginManager = IAppHost.GetService<IPluginManager>();
    private readonly IExternalLauncher _externalLauncher = IAppHost.GetService<IExternalLauncher>();
    private readonly PluginMarketService _marketService = IAppHost.GetService<PluginMarketService>();
    private readonly ObservableCollection<PluginListItemBase> _pluginList = [];
    private PluginListItemBase? _selectedItem;
    private string _searchText = string.Empty;
    private PluginOverviewFilter _filter = PluginOverviewFilter.Installed;
    private bool _isMarketLoaded;
    private bool _isMarketRefreshing;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public PluginsSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
    }

    public ObservableCollection<PluginListItemBase> PluginList { get; } = [];

    public ObservableCollection<PluginFilterChip> FilterChips { get; } =
    [
        new(PluginOverviewFilter.Installed, LR.C_FilterInstalled),
        new(PluginOverviewFilter.Market, LR.C_FilterMarket)
    ];

    public int VisiblePluginCount => PluginList.Count;
    public string VisiblePluginCountText => string.Format(LR.C_PluginCount, VisiblePluginCount);
    public bool IsPluginListEmpty => PluginList.Count == 0;

    public string EmptyTitle => Filter == PluginOverviewFilter.Market
        ? LR.C_CatalogEmpty
        : LR.C_OverviewEmptyTitle;

    public string EmptyHint => Filter == PluginOverviewFilter.Market
        ? LR.C_CatalogRefreshHint
        : LR.C_OverviewEmptyHint;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                ApplyFilterAndSelection(SelectedItem?.Id);
        }
    }

    public PluginOverviewFilter Filter
    {
        get => _filter;
        set
        {
            if (!SetField(ref _filter, value))
                return;

            ApplyFilterAndSelection(SelectedItem?.Id, BuildListForCurrentFilter());
            OnPropertyChanged(nameof(SelectedFilterChip));
            OnPropertyChanged(nameof(EmptyTitle));
            OnPropertyChanged(nameof(EmptyHint));
            if (value == PluginOverviewFilter.Market && !_isMarketLoaded)
                _ = RefreshMarketAsync();
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

    public PluginListItemBase? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetField(ref _selectedItem, value))
                return;

            OnPropertyChanged(nameof(HasSelectedPlugin));
            OnPropertyChanged(nameof(HasNoSelectedPlugin));
            OnPropertyChanged(nameof(SelectedPluginTitle));
            OnPropertyChanged(nameof(SelectedPluginMetaLine));
            OnPropertyChanged(nameof(SelectedPluginStatus));
            OnPropertyChanged(nameof(SelectedPluginError));
            OnPropertyChanged(nameof(HasSelectedPluginError));
            OnPropertyChanged(nameof(SelectedPluginReadme));
            OnPropertyChanged(nameof(SelectedPluginIcon));
            OnPropertyChanged(nameof(IsInstalledItemSelected));
            OnPropertyChanged(nameof(IsMarketItemSelected));
            OnPropertyChanged(nameof(IsSelectedPluginEnabled));
            OnPropertyChanged(nameof(CanToggleSelectedPlugin));
            OnPropertyChanged(nameof(CanOpenSelectedFolder));
            OnPropertyChanged(nameof(CanUninstallSelectedPlugin));
            OnPropertyChanged(nameof(MarketActionText));
            OnPropertyChanged(nameof(CanRunMarketAction));
            OnPropertyChanged(nameof(MarketDependenciesText));
        }
    }

    public bool HasSelectedPlugin => SelectedItem != null;
    public bool HasNoSelectedPlugin => SelectedItem == null;
    public bool IsInstalledItemSelected => SelectedItem is PluginOverviewItem;
    public bool IsMarketItemSelected => SelectedItem is PluginMarketItem;
    public bool CanToggleSelectedPlugin => SelectedItem is PluginOverviewItem;
    public bool CanOpenSelectedFolder => SelectedItem is PluginOverviewItem;
    public bool CanUninstallSelectedPlugin => SelectedItem is PluginOverviewItem;
    public bool HasSelectedPluginError => SelectedItem is PluginOverviewItem { ErrorMessage: not null and not "" };
    public bool CanRunMarketAction => SelectedItem is PluginMarketItem { CanInstall: true } && !_isMarketRefreshing;

    public IImage? SelectedPluginIcon => SelectedItem?.Icon;

    public string SelectedPluginTitle => SelectedItem?.Name ?? LR.C_NoPluginSelected;
    public string SelectedPluginMetaLine => SelectedItem?.MetaLine ?? string.Empty;
    public string SelectedPluginStatus => SelectedItem?.StatusText ?? "-";
    public string SelectedPluginError => SelectedItem is PluginOverviewItem item ? item.ErrorMessage ?? string.Empty : string.Empty;
    public string SelectedPluginReadme => BuildSelectedPluginReadme();

    public string MarketActionText => SelectedItem is PluginMarketItem { HasUpdate: true } item
        ? LR.C_Update
        : LR.C_Install;

    public string MarketDependenciesText => SelectedItem is PluginMarketItem { DependencyText.Length: > 0 } item
        ? string.Format(LR.C_Dependencies, item.DependencyText)
        : string.Empty;

    public bool IsSelectedPluginEnabled
    {
        get => SelectedItem is PluginOverviewItem { Plugin.IsEnabled: true };
        set
        {
            if (SelectedItem is not PluginOverviewItem { Plugin: { } plugin } || plugin.IsEnabled == value)
                return;

            if (!_pluginManager.SetEnabled(plugin.Manifest.Id, value))
            {
                this.ShowWarningToast(string.Format(LR.M_PluginImportFailed, plugin.Manifest.Id));
                return;
            }

            RefreshPlugins(SelectedItem.Id);
            SettingsView.Current?.RequestRestartApp();
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        RefreshPlugins(SelectedItem?.Id);
        if (Filter == PluginOverviewFilter.Market && !_isMarketLoaded)
            _ = RefreshMarketAsync();
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Filter == PluginOverviewFilter.Market)
            await RefreshMarketAsync();
        else
            RefreshPlugins(SelectedItem?.Id);
    }

    private async Task RefreshMarketAsync()
    {
        if (_isMarketRefreshing)
            return;

        _isMarketRefreshing = true;
        _isMarketLoaded = true;
        OnPropertyChanged(nameof(CanRunMarketAction));
        try
        {
            await _marketService.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(string.Format(LR.M_CatalogRefreshFailed, exception.Message));
        }
        finally
        {
            _isMarketRefreshing = false;
            OnPropertyChanged(nameof(CanRunMarketAction));
            if (Filter == PluginOverviewFilter.Market)
                ApplyFilterAndSelection(SelectedItem?.Id, BuildMarketItems());
        }
    }

    private IReadOnlyList<PluginMarketItem> BuildMarketItems()
    {
        var installed = _pluginManager.Plugins
            .ToDictionary(plugin => plugin.Manifest.Id, plugin => plugin.Manifest.Version, StringComparer.OrdinalIgnoreCase);
        return _marketService.Entries
            .Select(entry => PluginMarketItem.FromEntry(entry, installed, _marketService))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
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

        var packagePath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(packagePath))
            return;

        try
        {
            _pluginManager.StagePackage(packagePath);
            this.ShowSuccessToast(LR.M_PluginImported);
            SettingsView.Current?.RequestRestartApp();
        }
        catch (InvalidDataException)
        {
            this.ShowWarningToast(LR.M_InvalidPluginPackage);
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(string.Format(LR.M_PluginImportFailed, exception.Message));
        }
    }

    private void OpenPluginsFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _externalLauncher.TryOpenPath(_pluginManager.PluginsDirectory);
    }

    private void OpenSelectedFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is PluginOverviewItem item)
            _externalLauncher.TryOpenPath(item.DirectoryPath);
    }

    private async void UninstallSelectedPluginButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is not PluginOverviewItem { Plugin: { } plugin })
            return;

        var confirmed = await ConfirmUninstallAsync();
        if (!confirmed)
            return;

        if (!_pluginManager.UninstallPlugin(plugin.Manifest.Id))
        {
            this.ShowErrorToast(string.Format(LR.M_UninstallFailed, plugin.Manifest.Id));
            return;
        }

        RefreshPlugins(SelectedItem?.Id);
        SettingsView.Current?.RequestRestartApp();
    }

    private async void MarketActionButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem is not PluginMarketItem { Entry: { } entry } || _isMarketRefreshing)
            return;

        try
        {
            var plan = _marketService.ResolveInstallPlan(entry);
            var dependencyNote = plan.HasDependencies
                ? string.Format(LR.M_InstallDependencies, plan.Entries.Count - 1)
                : string.Empty;
            var confirmed = await ConfirmMarketInstallAsync(entry, dependencyNote);
            if (!confirmed)
                return;

            await _marketService.InstallAsync(plan, CancellationToken.None);
            this.ShowSuccessToast(string.Format(LR.M_PluginImported, entry.DisplayName));
            SettingsView.Current?.RequestRestartApp();
        }
        catch (InvalidDataException)
        {
            this.ShowWarningToast(LR.M_InvalidPluginPackage);
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(string.Format(LR.M_PluginImportFailed, exception.Message));
        }
    }

    private async Task<bool> ConfirmMarketInstallAsync(PluginCatalogEntry entry, string dependencyNote)
    {
        var separator = string.IsNullOrEmpty(dependencyNote) ? string.Empty : $"{Environment.NewLine}{dependencyNote}";
        var result = await new FAContentDialog
        {
            Title = LR.M_InstallConfirmTitle,
            Content = string.Format(LR.M_InstallConfirm, entry.DisplayName, separator),
            PrimaryButtonText = LR.C_Install,
            CloseButtonText = SecRandom.Langs.SettingsView.Resources.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        return result == FAContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmUninstallAsync()
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_UninstallConfirmTitle,
            Content = LR.M_UninstallConfirm,
            PrimaryButtonText = LR.C_Uninstall,
            CloseButtonText = SecRandom.Langs.SettingsView.Resources.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        return result == FAContentDialogResult.Primary;
    }

    private void RefreshPlugins(string? preferredPluginId)
    {
        ApplyFilterAndSelection(preferredPluginId, BuildListForCurrentFilter());
    }

    private void ApplyFilterAndSelection(string? preferredPluginId, IReadOnlyList<PluginListItemBase>? items = null)
    {
        var source = items ?? PluginList.ToList();
        IReadOnlyList<PluginListItemBase> filteredList;
        if (Filter == PluginOverviewFilter.Installed)
        {
            filteredList = source.OfType<PluginOverviewItem>()
                .Where(item => MatchesQuery(item))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        else
        {
            filteredList = source.OfType<PluginMarketItem>()
                .Where(item => MatchesQuery(item))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        PluginList.Clear();
        foreach (var item in filteredList)
            PluginList.Add(item);

        OnPropertyChanged(nameof(VisiblePluginCount));
        OnPropertyChanged(nameof(VisiblePluginCountText));
        OnPropertyChanged(nameof(IsPluginListEmpty));

        var preferred = filteredList.FirstOrDefault(x => x.Id == preferredPluginId);
        var current = filteredList.FirstOrDefault(x => x.Id == SelectedItem?.Id);
        SelectedItem = preferred ?? current;
    }

    /// <summary>
    ///     Builds the item list for the currently selected tab from its own source, so switching tabs
    ///     does not filter the other tab's entries out of the shared <see cref="PluginList"/>.
    /// </summary>
    private IReadOnlyList<PluginListItemBase> BuildListForCurrentFilter()
    {
        return Filter == PluginOverviewFilter.Installed
            ? _pluginManager.Plugins
                .Select(plugin => PluginOverviewItem.FromPlugin(plugin, FormatStatus(plugin)))
                .ToArray()
            : BuildMarketItems();
    }

    private bool MatchesQuery(PluginListItemBase item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var query = SearchText.Trim();
        return item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || item.Id.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || item.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string FormatStatus(PluginInfo plugin)
    {
        return plugin.LoadStatus switch
        {
            PluginLoadStatus.Loaded => LR.S_StatusLoaded,
            PluginLoadStatus.Disabled => LR.S_StatusDisabled,
            PluginLoadStatus.Error => LR.S_StatusLoadFailed,
            _ => LR.S_StatusDiscovered
        };
    }

    private string BuildSelectedPluginReadme()
    {
        if (SelectedItem is PluginOverviewItem overview)
        {
            foreach (var fileName in new[] { "README.md", "Readme.md", "readme.md", "README.txt", "readme.txt" })
            {
                var path = Path.Combine(overview.DirectoryPath, fileName);
                if (File.Exists(path))
                    return File.ReadAllText(path, Encoding.UTF8);
            }

            return $"# {overview.Name}{Environment.NewLine}{Environment.NewLine}{overview.Description}";
        }

        if (SelectedItem is PluginMarketItem market)
            return market.ReadmeText;

        return string.Empty;
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

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }
}

public enum PluginOverviewFilter
{
    Installed,
    Market
}

public sealed record PluginFilterChip(PluginOverviewFilter Filter, string Text);

public abstract class PluginListItemBase
{
    public string Id { get; protected init; } = string.Empty;
    public string Name { get; protected init; } = string.Empty;
    public string Version { get; protected init; } = string.Empty;
    public string Author { get; protected init; } = string.Empty;
    public string Description { get; protected init; } = string.Empty;
    public IImage? Icon { get; protected init; }
    public string ListSubtitle => string.IsNullOrWhiteSpace(Author) ? Id : Author;
    public abstract string MetaLine { get; }
    public abstract string StatusText { get; }
}

public sealed class PluginOverviewItem : PluginListItemBase
{
    private PluginOverviewItem()
    {
    }

    public PluginInfo Plugin { get; private init; } = null!;
    public string ApiVersion { get; private init; } = string.Empty;
    public string? ErrorMessage { get; private init; }
    public string DirectoryPath { get; private init; } = string.Empty;

    public override string MetaLine => string.IsNullOrWhiteSpace(Author) ? Version : $"{Version} | {Author}";
    public override string StatusText => _statusText;
    private string _statusText = string.Empty;

    public static PluginOverviewItem FromPlugin(PluginInfo plugin, string statusText)
    {
        var item = new PluginOverviewItem
        {
            Plugin = plugin,
            Id = plugin.Manifest.Id,
            Name = string.IsNullOrWhiteSpace(plugin.Manifest.Name) ? plugin.Manifest.Id : plugin.Manifest.Name,
            Version = plugin.Manifest.Version,
            Author = plugin.Manifest.Author,
            Description = plugin.Manifest.Description,
            ApiVersion = plugin.Manifest.ApiVersion,
            ErrorMessage = plugin.Exception?.Message,
            DirectoryPath = plugin.PluginFolderPath,
            Icon = LoadIcon(plugin)
        };
        item._statusText = statusText;
        return item;
    }

    private static IImage? LoadIcon(PluginInfo plugin)
    {
        try
        {
            var iconName = plugin.Manifest.Icon.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(iconName))
                return null;

            var iconPath = Path.Combine(plugin.PluginFolderPath, iconName);
            return File.Exists(iconPath) ? new Bitmap(iconPath) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public sealed class PluginMarketItem : PluginListItemBase
{
    private PluginMarketItem()
    {
    }

    public PluginCatalogEntry Entry { get; private init; } = null!;
    public bool IsInstalled { get; private init; }
    public bool HasUpdate { get; private init; }
    public bool IsCompatible { get; private init; }
    public bool CanInstall => !IsInstalled && IsCompatible;
    public string ApiVersion { get; private init; } = string.Empty;
    public string DependencyText { get; private init; } = string.Empty;
    public string ReadmeText { get; private init; } = string.Empty;

    public override string MetaLine => string.IsNullOrWhiteSpace(Author) ? Version : $"{Version} | {Author}";

    public override string StatusText => !IsCompatible
        ? LR.C_Incompatible
        : IsInstalled
            ? (HasUpdate ? LR.C_Update : LR.C_Installed)
            : LR.S_StatusAvailable;

    public static PluginMarketItem FromEntry(
        PluginCatalogEntry entry,
        IReadOnlyDictionary<string, string> installedVersions,
        PluginMarketService marketService)
    {
        var installed = installedVersions.TryGetValue(entry.Id, out var version);
        var hasUpdate = installed && !string.IsNullOrWhiteSpace(version) && IsNewer(entry.Version, version);
        return new PluginMarketItem
        {
            Entry = entry,
            Id = entry.Id,
            Name = entry.DisplayName,
            Version = entry.Version,
            Author = entry.Author,
            Description = entry.Description,
            ApiVersion = entry.ApiVersion,
            IsInstalled = installed,
            HasUpdate = hasUpdate,
            IsCompatible = PluginMarketService.IsCompatible(entry, PluginApiVersions.Current.Major.ToString()),
            DependencyText = string.Join(", ", entry.Dependencies.Select(dependency => dependency.Id)),
            ReadmeText = BuildReadme(entry),
            Icon = null
        };
    }

    private static bool IsNewer(string candidate, string current)
    {
        return TryParseVersion(candidate, out var candidateVersion)
               && TryParseVersion(current, out var currentVersion)
               && candidateVersion > currentVersion;
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        var normalized = text.Trim().TrimStart('v', 'V');
        var prereleaseIndex = normalized.IndexOf('-');
        if (prereleaseIndex >= 0)
            normalized = normalized[..prereleaseIndex];
        return System.Version.TryParse(normalized, out version!);
    }

    private static string BuildReadme(PluginCatalogEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {entry.DisplayName}");
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            builder.AppendLine();
            builder.AppendLine(entry.Description);
        }

        if (!string.IsNullOrWhiteSpace(entry.ProjectUrl))
        {
            builder.AppendLine();
            builder.AppendLine($"[{LR.C_ProjectUrl}]({entry.ProjectUrl})");
        }

        return builder.ToString();
    }
}
