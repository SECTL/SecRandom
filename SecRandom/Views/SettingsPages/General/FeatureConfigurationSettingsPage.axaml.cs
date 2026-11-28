using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FeatureConfiguration;

namespace SecRandom.Views.SettingsPages.General;

public partial class FeatureConfigurationSettingsPage : UserControl
{
    private static readonly string[] CategoryKeys = ["Overview", "Core", "Draw", "Window", "Sound", "Extension", "Data", "System"];
    private readonly MainConfigHandler _config;
    private readonly SimpleModeSettingsConfig _settings;
    private HashSet<string> _saved = [];
    private HashSet<string> _editing = [];
    private string _profileId = StartupFeatureCatalog.FullProfile;
    private string _profileName = string.Empty;
    private bool _updatingProfileSelector;

    public FeatureConfigurationSettingsPage()
    {
        _config = IAppHost.GetService<MainConfigHandler>();
        _settings = _config.Data.General.SimpleMode;
        InitializeComponent();
        DataContext = this;

        PageTitle = FeatureConfigurationText.Get("Title");
        ProfileDescription = FeatureConfigurationText.Get("EntryDescription");
        SaveAsButton.Content = FeatureConfigurationText.Get("SaveAs");
        SaveButton.Content = FeatureConfigurationText.Get("Save");
        RenameButton.Content = FeatureConfigurationText.Get("Rename");
        DeleteButton.Content = FeatureConfigurationText.Get("Delete");
        DiscardButton.Content = FeatureConfigurationText.Get("Discard");
        ActivateButton.Content = FeatureConfigurationText.Get("Activate");

        for (var index = 0; index < CategoryKeys.Length; index++)
            CategoryTabs.Items.Add(new TabItem { Header = FeatureConfigurationText.Get(CategoryKeys[index]), Tag = index });

        LoadProfile(StartupFeatureCatalog.ResolveSelectedProfileId(_settings));
        CategoryTabs.SelectedIndex = 0;
        RenderPage();
    }

    public string PageTitle { get; }
    public string ProfileTitle => string.Format(FeatureConfigurationText.Get("Current"), _profileName);
    public string ProfileDescription { get; }

    private static string T(string key) => FeatureConfigurationText.Get(key);
    private bool IsDirty => !_saved.SetEquals(_editing);
    private bool IsBuiltin => _profileId is StartupFeatureCatalog.FullProfile or StartupFeatureCatalog.SimpleProfile;
    private string ActiveProfileId => StartupFeatureCatalog.ResolveSelectedProfileId(_settings);

    private string ProfileName(string id) => id switch
    {
        StartupFeatureCatalog.FullProfile => T("Full"),
        StartupFeatureCatalog.SimpleProfile => T("Simple"),
        _ => _settings.Profiles?.FirstOrDefault(profile => profile.Id == id)?.Name ?? T("Full")
    };

    private void LoadProfile(string id, bool refreshSelector = true)
    {
        _profileId = id;
        _profileName = ProfileName(id);
        _saved = new HashSet<string>(ResolveProfile(id), StringComparer.Ordinal);
        _editing = new HashSet<string>(_saved, StringComparer.Ordinal);
        if (refreshSelector)
            RefreshProfileSelector();
        RenderPage();
    }

    private IReadOnlySet<string> ResolveProfile(string id)
    {
        if (id == StartupFeatureCatalog.FullProfile)
            return StartupFeatureCatalog.ResolveLegacyMode(false);
        if (id == StartupFeatureCatalog.SimpleProfile)
            return StartupFeatureCatalog.ResolveLegacyMode(true);

        var profile = _settings.Profiles?.FirstOrDefault(item => item.Id == id);
        return profile is null
            ? StartupFeatureCatalog.ResolveLegacyMode(false)
            : StartupFeatureCatalog.Graph.Resolve(profile.EnabledFeatures ?? []);
    }

    private void RefreshProfileSelector()
    {
        _updatingProfileSelector = true;
        try
        {
            var items = new List<ComboBoxItem>
            {
                new() { Content = T("Full"), Tag = StartupFeatureCatalog.FullProfile },
                new() { Content = T("Simple"), Tag = StartupFeatureCatalog.SimpleProfile }
            };
            foreach (var profile in _settings.Profiles ?? [])
                items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });

            ProfileSelector.SelectedItem = null;
            ProfileSelector.ItemsSource = items;
            ProfileSelector.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, _profileId));
        }
        finally
        {
            _updatingProfileSelector = false;
        }
    }

    private async void ProfileSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfileSelector || ProfileSelector.SelectedItem is not ComboBoxItem { Tag: string id } || id == _profileId)
            return;
        if (!await ConfirmDiscardAsync())
        {
            SetProfileSelectorSelection(_profileId);
            return;
        }
        LoadProfile(id, refreshSelector: false);
    }

    private void SetProfileSelectorSelection(string id)
    {
        _updatingProfileSelector = true;
        try
        {
            ProfileSelector.SelectedItem = ProfileSelector.ItemsSource?.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, id));
        }
        finally
        {
            _updatingProfileSelector = false;
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty)
            return true;
        var result = await new FAContentDialog
        {
            Title = T("CloseTitle"),
            Content = T("CloseMessage"),
            PrimaryButtonText = T("Save"),
            SecondaryButtonText = T("DontSave"),
            CloseButtonText = T("Cancel")
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result == FAContentDialogResult.Primary)
            return await SaveAsync();
        if (result != FAContentDialogResult.Secondary)
            return false;
        _editing = new HashSet<string>(_saved, StringComparer.Ordinal);
        RenderPage();
        return true;
    }

    public Task<bool> ConfirmCloseAsync() => ConfirmDiscardAsync();

    private void CategoryTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RenderPage();
    }

    private void RenderPage()
    {
        if (PageContent is null)
            return;
        ProfileExpander.Header = ProfileTitle;
        StatusText.Text = T(IsDirty ? "Unsaved" : "Saved");
        CountText.Text = string.Format(T("Count"), _editing.Count, StartupFeatureCatalog.Graph.Features.Count);
        ActivateButton.IsVisible = _profileId != ActiveProfileId;
        SaveAsButton.IsVisible = true;
        SaveButton.IsVisible = !IsBuiltin;
        SaveButton.IsEnabled = IsDirty;
        RenameButton.IsEnabled = !IsBuiltin;
        DeleteButton.IsEnabled = !IsBuiltin;
        DiscardButton.IsEnabled = IsDirty;

        PageContent.Children.Clear();
        var index = CategoryTabs.SelectedIndex;
        if (index <= 0)
        {
            PageContent.Children.Add(Heading(T("Overview")));
            PageContent.Children.Add(new TextBlock { Text = T("Intro"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            PageContent.Children.Add(new TextBlock { Text = string.Format(T("Included"), string.Join(", ", StartupFeatureCatalog.Graph.Features.Where(feature => _editing.Contains(feature.Id)).Select(feature => T(feature.NameKey)))), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            PageContent.Children.Add(Heading(T("Information")));
            foreach (var feature in StartupFeatureCatalog.Graph.Features)
                PageContent.Children.Add(CreateFeatureRow(feature));
            return;
        }

        var category = CategoryKeys[index];
        PageContent.Children.Add(Heading(T(category)));
        foreach (var feature in StartupFeatureCatalog.Graph.Features.Where(feature => feature.CategoryKey == category))
            PageContent.Children.Add(CreateFeatureRow(feature));
    }

    private Control CreateFeatureRow(FeatureDefinition feature)
    {
        var card = new FASettingsExpander
        {
            Header = T(feature.NameKey),
            Description = T(feature.DescriptionKey),
            IconSource = new FluentIconSource(FluentIcons.AppsFilled),
            IsExpanded = false
        };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (feature.Required)
        {
            footer.Children.Add(new TextBlock
            {
                Text = T("Required"),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Avalonia.Media.Brushes.Gray
            });
        }
        else
        {
            var toggle = new ToggleSwitch
            {
                Tag = feature.Id,
                IsChecked = _editing.Contains(feature.Id),
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Click += async (_, _) => await ToggleFeatureAsync(feature.Id, toggle);
            footer.Children.Add(toggle);
        }
        var dependents = StartupFeatureCatalog.Graph.Features.Where(item => item.Dependencies.Contains(feature.Id)).Select(item => item.Id);
        var info = new Button
        {
            Content = new FluentIcon(FluentIcons.InfoFilled, 16),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = new Flyout { Content = new TextBlock { Text = $"{T("Dependencies")}: {DependencyNames(feature.Dependencies)}\n{T("Dependents")}: {DependencyNames(dependents)}", TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 320 } }
        };
        ToolTip.SetTip(info, T("Information"));
        footer.Children.Add(info);
        card.Footer = footer;
        return card;
    }

    private async Task ToggleFeatureAsync(string id, ToggleSwitch toggle)
    {
        var enabling = toggle.IsChecked == true;
        var next = enabling ? StartupFeatureCatalog.Graph.Enable(_editing, id) : StartupFeatureCatalog.Graph.Disable(_editing, id);
        var affected = enabling ? next.Except(_editing).Where(item => item != id) : _editing.Except(next).Where(item => item != id);
        var names = affected.Select(FeatureName).ToArray();
        if (names.Length > 0)
        {
            var result = await new FAContentDialog
            {
                Title = T("Information"),
                Content = string.Format(T(enabling ? "EnableDependencies" : "DisableDependents"), string.Join(", ", names)),
                PrimaryButtonText = T("Confirm"),
                CloseButtonText = T("Cancel")
            }.ShowAsync(TopLevel.GetTopLevel(this));
            if (result != FAContentDialogResult.Primary)
            {
                toggle.IsChecked = !enabling;
                return;
            }
        }
        _editing = new HashSet<string>(next, StringComparer.Ordinal);
        RenderPage();
    }

    private string FeatureName(string id) => StartupFeatureCatalog.Graph.Features.FirstOrDefault(feature => feature.Id == id) is { } feature ? T(feature.NameKey) : id;
    private string DependencyNames(IEnumerable<string> ids)
    {
        var names = ids.Select(FeatureName).ToArray();
        return names.Length == 0 ? T("None") : string.Join(", ", names);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) };

    private void Discard_OnClick(object? sender, RoutedEventArgs e)
    {
        _editing = new HashSet<string>(_saved, StringComparer.Ordinal);
        RenderPage();
    }

    private async void Save_OnClick(object? sender, RoutedEventArgs e) => await SaveAsync();
    private async void SaveAs_OnClick(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: true);

    private async Task<bool> SaveAsync(bool saveAs = false)
    {
        var source = (_settings.Profiles ?? []).FirstOrDefault(p => p.Id == _profileId);
        if (IsBuiltin || saveAs)
        {
            var name = await PromptNameAsync();
            if (name is null)
                return false;
            _profileId = $"user.{Guid.NewGuid():N}";
            _profileName = name;
        }
        var profiles = (_settings.Profiles ?? []).ToList();
        var existing = profiles.FirstOrDefault(p => p.Id == _profileId);
        var known = StartupFeatureCatalog.Graph.Features.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = source?.EnabledFeatures?.Where(id => !known.Contains(id)) ?? [];
        var profile = new FeatureProfile { Id = _profileId, Name = _profileName, EnabledFeatures = _editing.Concat(unknown).ToList() };
        if (existing is not null)
            profiles.Remove(existing);
        profiles.Add(profile);
        _settings.Profiles = profiles;
        _config.Save();
        if (_settings.SelectedProfileId == _profileId)
            SettingsView.Current?.RequestRestartApp();
        _saved = new HashSet<string>(_editing, StringComparer.Ordinal);
        RefreshProfileSelector();
        RenderPage();
        return true;
    }

    private async Task<string?> PromptNameAsync(string? currentName = null)
    {
        var input = new TextBox { MaxLength = 64, Text = currentName ?? string.Empty };
        var result = await new FAContentDialog { Title = T("SaveName"), Content = input, PrimaryButtonText = T("Save"), CloseButtonText = T("Cancel") }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return null;
        var name = input.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, T("Full"), StringComparison.CurrentCultureIgnoreCase)
            && !string.Equals(name, T("Simple"), StringComparison.CurrentCultureIgnoreCase)
            && !(_settings.Profiles ?? []).Any(p => (currentName is null || p.Id != _profileId) && string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            return name;
        await new FAContentDialog { Title = T("SaveName"), Content = T("NameInvalid"), CloseButtonText = T("Confirm") }.ShowAsync(TopLevel.GetTopLevel(this));
        return null;
    }

    private async void Rename_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IsBuiltin)
            return;
        var name = await PromptNameAsync(_profileName);
        if (name is null)
            return;
        var profile = (_settings.Profiles ?? []).FirstOrDefault(item => item.Id == _profileId);
        if (profile is null)
            return;
        profile.Name = _profileName = name;
        _config.Save();
        RefreshProfileSelector();
        RenderPage();
    }

    private async void Delete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IsBuiltin)
            return;
        var result = await new FAContentDialog { Title = T("Delete"), Content = T("DeleteMessage"), PrimaryButtonText = T("Delete"), CloseButtonText = T("Cancel") }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return;
        var wasSelected = _settings.SelectedProfileId == _profileId;
        _settings.Profiles = (_settings.Profiles ?? []).Where(item => item.Id != _profileId).ToList();
        if (wasSelected)
        {
            _settings.SelectedProfileId = StartupFeatureCatalog.FullProfile;
            SettingsView.Current?.RequestRestartApp();
        }
        _config.Save();
        LoadProfile(StartupFeatureCatalog.FullProfile);
    }

    private async void Activate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync())
            return;
        var result = await new FAContentDialog { Title = T("Activate"), Content = T("ApplyConfirmation"), PrimaryButtonText = T("Confirm"), CloseButtonText = T("Cancel") }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return;
        _settings.SelectedProfileId = _profileId;
        _settings.Enabled = _profileId == StartupFeatureCatalog.SimpleProfile;
        _config.Save();
        SettingsView.Current?.RequestRestartApp();
        RenderPage();
    }
}
