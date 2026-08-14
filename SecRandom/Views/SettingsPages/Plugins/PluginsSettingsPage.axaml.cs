using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HotAvalonia;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.PluginSdk;
using SecRandom.Services.Desktop;
using LR = SecRandom.Langs.SettingsPages.Plugins.Overview.Resources;

namespace SecRandom.Views.SettingsPages.Plugins;

[PageInfo("settings.plugin", FluentIcons.AppsListFilled, useFullWidth: true, hidePageTitle: true)]
public partial class PluginsSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly IPluginManager _pluginManager = IAppHost.GetService<IPluginManager>();
    private readonly IExternalLauncher _externalLauncher = IAppHost.GetService<IExternalLauncher>();
    private readonly ObservableCollection<PluginOverviewItem> _pluginList = [];
    private PluginOverviewItem? _selectedItem;
    private string _searchText = string.Empty;
    private PluginOverviewFilter _filter = PluginOverviewFilter.Installed;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public PluginsSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        ReadmeViewer.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Markdown")
                Dispatcher.UIThread.Post(RebuildCodeBlocks);
        };
    }

    public ObservableCollection<PluginOverviewItem> PluginList { get; } = [];

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

    public string EmptyHint => LR.C_OverviewEmptyHint;

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

            ApplyFilterAndSelection(SelectedItem?.Id);
            OnPropertyChanged(nameof(SelectedFilterChip));
            OnPropertyChanged(nameof(EmptyTitle));
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

            OnPropertyChanged(nameof(HasSelectedPlugin));
            OnPropertyChanged(nameof(HasNoSelectedPlugin));
            OnPropertyChanged(nameof(SelectedPluginTitle));
            OnPropertyChanged(nameof(SelectedPluginMetaLine));
            OnPropertyChanged(nameof(SelectedPluginStatus));
            OnPropertyChanged(nameof(SelectedPluginError));
            OnPropertyChanged(nameof(HasSelectedPluginError));
            OnPropertyChanged(nameof(SelectedPluginReadme));
            OnPropertyChanged(nameof(SelectedPluginIcon));
            OnPropertyChanged(nameof(IsSelectedPluginEnabled));
            OnPropertyChanged(nameof(CanToggleSelectedPlugin));
            OnPropertyChanged(nameof(CanOpenSelectedFolder));
        }
    }

    public bool HasSelectedPlugin => SelectedItem != null;
    public bool HasNoSelectedPlugin => SelectedItem == null;
    public bool CanToggleSelectedPlugin => SelectedItem != null;
    public bool CanOpenSelectedFolder => SelectedItem != null;
    public bool HasSelectedPluginError => !string.IsNullOrWhiteSpace(SelectedItem?.ErrorMessage);

    public IImage? SelectedPluginIcon => SelectedItem?.Icon;

    public string SelectedPluginTitle => SelectedItem?.Name ?? LR.C_NoPluginSelected;
    public string SelectedPluginMetaLine => SelectedItem == null
        ? string.Empty
        : $"{SelectedItem.Version} | {SelectedItem.Author}";
    public string SelectedPluginStatus => SelectedItem?.StatusText ?? "-";
    public string SelectedPluginError => SelectedItem?.ErrorMessage ?? string.Empty;
    public string SelectedPluginReadme => BuildSelectedPluginReadme();

    public bool IsSelectedPluginEnabled
    {
        get => SelectedItem?.Plugin.IsEnabled == true;
        set
        {
            if (SelectedItem?.Plugin is not { } plugin || plugin.IsEnabled == value)
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
        Dispatcher.UIThread.Post(RebuildCodeBlocks);
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshPlugins(SelectedItem?.Id);
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
        if (SelectedItem is not null)
            _externalLauncher.TryOpenPath(SelectedItem.DirectoryPath);
    }

    [AvaloniaHotReload]
    private void RebuildCodeBlocks()
    {
        var viewer = ReadmeViewer;
        if (!viewer.IsLoaded)
            return;

        foreach (var border in viewer.GetVisualDescendants()
                     .OfType<Border>()
                     .Where(b => b.Classes.Contains("CodeBlock"))
                     .ToList())
        {
            if (border.Child is Grid)
                continue;

            if (border.Child is not Panel codePad)
                continue;

            var editor = codePad.Children.OfType<TextEditor>().FirstOrDefault();
            if (editor is null)
                continue;

            var lang = codePad.Children.OfType<Label>().FirstOrDefault()?.Content?.ToString() ?? string.Empty;
            codePad.Children.Remove(editor);
            border.Child = BuildCodeBlockLayout(editor, lang);
        }
    }

    private static Control BuildCodeBlockLayout(TextEditor editor, string lang)
    {
        editor.Margin = new Thickness(0, 0, 0, 0);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };

        var langLabel = new Label
        {
            Content = lang,
            VerticalAlignment = VerticalAlignment.Center
        };
        langLabel.Classes.Add("LangInfo");
        header.Children.Add(langLabel);

        var copyButton = new Button { Content = new TextBlock() };
        copyButton.Classes.Add("CopyButton");
        copyButton.Click += (_, _) =>
        {
            var top = TopLevel.GetTopLevel(editor);
            top?.Clipboard?.SetTextAsync(editor.Text);
        };
        Grid.SetColumn(copyButton, 1);
        header.Children.Add(copyButton);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(header);
        Grid.SetRow(editor, 1);
        layout.Children.Add(editor);
        return layout;
    }

    private void RefreshPlugins(string? preferredPluginId)
    {
        var query = SearchText.Trim();
        var items = _pluginManager.Plugins
            .Where(plugin => string.IsNullOrWhiteSpace(query)
                             || plugin.Manifest.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                             || plugin.Manifest.Id.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                             || plugin.Manifest.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(plugin => plugin.Manifest.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(plugin => PluginOverviewItem.FromPlugin(plugin, FormatStatus(plugin)))
            .ToList();

        ApplyFilterAndSelection(preferredPluginId, items);
    }

    private void ApplyFilterAndSelection(string? preferredPluginId, IReadOnlyList<PluginOverviewItem>? items = null)
    {
        var source = items ?? PluginList.ToList();
        var filteredList = Filter == PluginOverviewFilter.Installed
            ? source.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
            : [];

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
        if (SelectedItem == null)
            return string.Empty;

        foreach (var fileName in new[] { "README.md", "Readme.md", "readme.md", "README.txt", "readme.txt" })
        {
            var path = Path.Combine(SelectedItem.DirectoryPath, fileName);
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8);
        }

        return $"# {SelectedItem.Name}{Environment.NewLine}{Environment.NewLine}{SelectedItem.Description}";
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

public sealed class PluginOverviewItem
{
    private PluginOverviewItem()
    {
    }

    public PluginInfo Plugin { get; private init; } = null!;
    public string Id { get; private init; } = string.Empty;
    public string Name { get; private init; } = string.Empty;
    public string Version { get; private init; } = string.Empty;
    public string Author { get; private init; } = string.Empty;
    public string Description { get; private init; } = string.Empty;
    public string ApiVersion { get; private init; } = string.Empty;
    public string StatusText { get; private init; } = string.Empty;
    public string? ErrorMessage { get; private init; }
    public string DirectoryPath { get; private init; } = string.Empty;
    public IImage? Icon { get; private init; }
    public string ListSubtitle => string.IsNullOrWhiteSpace(Author) ? Id : Author;

    public static PluginOverviewItem FromPlugin(PluginInfo plugin, string statusText)
    {
        return new PluginOverviewItem
        {
            Plugin = plugin,
            Id = plugin.Manifest.Id,
            Name = string.IsNullOrWhiteSpace(plugin.Manifest.Name) ? plugin.Manifest.Id : plugin.Manifest.Name,
            Version = plugin.Manifest.Version,
            Author = plugin.Manifest.Author,
            Description = plugin.Manifest.Description,
            ApiVersion = plugin.Manifest.ApiVersion,
            StatusText = statusText,
            ErrorMessage = plugin.Exception?.Message,
            DirectoryPath = plugin.PluginFolderPath,
            Icon = LoadIcon(plugin)
        };
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
