using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Services.Notification;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.Notification.Texts.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

/// <summary>
/// Edits the fullscreen result text library: statistics, per-entry usage/enablement, editing and bulk import.
/// </summary>
[PageInfo("settings.notification.texts", FluentIcons.TextFontFilled, "settings.notification", isHide: true)]
public partial class TextLibrarySettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly TextLibraryService? _textLibraryService;
    private string _searchText = string.Empty;
    private bool _isRebuilding;
    private TextLibraryOption? _selectedLibrary;

    public TextLibrarySettingsPage()
    {
        _textLibraryService = IAppHost.TryGetService<TextLibraryService>();
        if (_textLibraryService is not null)
            _textLibraryService.Changed += OnLibraryChanged;

        RebuildRows();
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_textLibraryService is not null)
            {
                _textLibraryService.Changed -= OnLibraryChanged;
                _textLibraryService.Changed += OnLibraryChanged;
            }
            RebuildRows();
        };
        Unloaded += (_, _) =>
        {
            if (_textLibraryService is not null)
                _textLibraryService.Changed -= OnLibraryChanged;
        };
    }

    public ObservableCollection<TextLibraryRow> Rows { get; } = [];
    public ObservableCollection<TextLibraryOption> Libraries { get; } = [];
    public string NewLibraryText => Text("C_NewLibrary");
    public string CopyLibraryText => Text("C_CopyLibrary");
    public string RenameLibraryText => Text("C_RenameLibrary");
    public string DeleteLibraryText => Text("C_DeleteLibrary");
    public string LinkRostersText => Text("C_LinkRosters");
    public bool CanDeleteLibrary => _selectedLibrary is { IsDefault: false };
    public TextLibraryOption? SelectedLibrary
    {
        get => _selectedLibrary;
        set
        {
            if (value is null || _selectedLibrary?.Id == value.Id) return;
            _selectedLibrary = value;
            _textLibraryService?.SelectLibrary(value.Id);
            OnPropertyChanged(nameof(SelectedLibrary));
            OnPropertyChanged(nameof(CanDeleteLibrary));
            RebuildRows();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;
            _searchText = value ?? string.Empty;
            OnPropertyChanged(nameof(SearchText));
            RebuildRows();
        }
    }

    public int TotalCount { get; private set; }

    public int EnabledCount { get; private set; }

    public int TotalUseCount { get; private set; }

    public int EligibleCount { get; private set; }

    public string AverageUseCountText { get; private set; } = "0.0";

    /// <summary>One-line summary shown in the toolbar: total, enabled, used, average and rotatable counts.</summary>
    public string StatisticsText { get; private set; } = string.Empty;

    public bool IsUnavailable => _textLibraryService?.LoadError is not null;
    public string UnavailableText => Text("M_LibraryUnavailable");
    public bool IsEmpty => TotalCount == 0 && !IsUnavailable;

    public bool HasRows => TotalCount > 0;

    public bool HasVisibleRows => Rows.Count > 0;

    public bool HasNoMatches => HasRows && Rows.Count == 0;

    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    private static string Text(string key) => LR.ResourceManager.GetString(key, LR.Culture) ?? key;

    private void OnLibraryChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RebuildRows, DispatcherPriority.Background);

    private void RebuildRows()
    {
        if (_isRebuilding)
            return;
        _isRebuilding = true;
        try
        {
            if (_textLibraryService is not null)
            {
                var selected = _textLibraryService.SelectedLibraryId;
                Libraries.Clear();
                foreach (var library in _textLibraryService.Libraries)
                    Libraries.Add(new TextLibraryOption(library.Id, library.IsDefault && library.Name == "Default" ? Text("C_DefaultLibrary") : library.Name, library.IsDefault));
                _selectedLibrary = Libraries.FirstOrDefault(item => item.Id == selected);
                OnPropertyChanged(nameof(SelectedLibrary));
                OnPropertyChanged(nameof(CanDeleteLibrary));
            }
            var entries = _textLibraryService?.Entries ?? [];
            TotalCount = entries.Count;
            EnabledCount = entries.Count(entry => entry.Enabled);
            TotalUseCount = _textLibraryService?.TotalUseCount ?? 0;
            EligibleCount = _textLibraryService?.EligibleCount ?? 0;
            AverageUseCountText = (_textLibraryService?.AverageUseCount ?? 0)
                .ToString("0.0", CultureInfo.CurrentCulture);
            StatisticsText = string.Format(CultureInfo.CurrentCulture, Text("M_StatisticsFormat"),
                TotalCount, EnabledCount, TotalUseCount, AverageUseCountText, EligibleCount);

            var query = _searchText.Trim();
            Rows.Clear();
            foreach (var entry in entries
                         .Where(entry => query.Length == 0 ||
                                         entry.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                         .OrderByDescending(entry => entry.UseCount)
                         .ThenBy(entry => entry.Text, StringComparer.CurrentCulture))
            {
                var share = TotalUseCount == 0 ? 0 : entry.UseCount * 100.0 / TotalUseCount;
                Rows.Add(new TextLibraryRow(entry, string.Format(CultureInfo.CurrentCulture, "{0:0.0}%", share),
                    SetEntryEnabled));
            }

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(EnabledCount));
            OnPropertyChanged(nameof(TotalUseCount));
            OnPropertyChanged(nameof(EligibleCount));
            OnPropertyChanged(nameof(AverageUseCountText));
            OnPropertyChanged(nameof(StatisticsText));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsUnavailable));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasVisibleRows));
            OnPropertyChanged(nameof(HasNoMatches));
            OnPropertyChanged(nameof(IsSearching));
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private async Task EditLibraryAsync(bool copy, bool rename)
    {
        if (_textLibraryService is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var input = new TextBox { Text = rename ? _textLibraryService.Libraries.FirstOrDefault(l => l.Id == _selectedLibrary?.Id)?.Name : string.Empty, MaxLength = 100, PlaceholderText = Text("M_LibraryName") };
        var result = await new FAContentDialog
        {
            Title = Text(rename ? "C_RenameLibrary" : copy ? "C_CopyLibrary" : "C_NewLibrary"),
            Content = input, PrimaryButtonText = Text("C_Save"), CloseButtonText = Text("C_Cancel")
        }.ShowAsync(top);
        if (result != FAContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text)) return;
        try
        {
            if (rename && _selectedLibrary is not null) _textLibraryService.RenameLibrary(_selectedLibrary.Id, input.Text);
            else _textLibraryService.CreateLibrary(input.Text, copy);
            RebuildRows();
        }
        catch (Exception exception) { this.ShowErrorToast(Text("M_OperationFailed"), exception); }
    }

    private async void NewLibrary_OnClick(object? sender, RoutedEventArgs e) => await EditLibraryAsync(false, false);
    private async void CopyLibrary_OnClick(object? sender, RoutedEventArgs e) => await EditLibraryAsync(true, false);
    private async void RenameLibrary_OnClick(object? sender, RoutedEventArgs e) => await EditLibraryAsync(false, true);
    private async void LinkRosters_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_textLibraryService is null || _selectedLibrary is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var catalog = IAppHost.TryGetService<IProfileCatalogManager>();
        if (catalog is null) return;
        var libraryId = _selectedLibrary.Id;
        var isDefault = _selectedLibrary.IsDefault;
        var items = new System.Collections.Generic.List<(string Name, bool Student, CheckBox Check)>();
        var content = new StackPanel { Spacing = 6 };
        foreach (var (name, student) in catalog.GetStudentListNames().Select(name => (name, true)).Concat(catalog.GetPrizeListNames().Select(name => (name, false))))
        {
            var roster = student ? (SecRandom.Shared.Interfaces.IAttachableSettingsObject?)catalog.LoadStudentList(name) : catalog.LoadPrizeList(name);
            if (roster is null) continue;
            var binding = TextLibraryBinding.Get(roster);
            var usesDefault = string.IsNullOrEmpty(binding) || binding == _textLibraryService.DefaultLibraryId;
            var boundName = usesDefault ? Text("C_DefaultLibrary") : _textLibraryService.Libraries.FirstOrDefault(l => l.Id == binding)?.Name ?? Text("M_MissingLibrary");
            var check = new CheckBox { Content = $"{Text(student ? "C_StudentRoster" : "C_PrizeRoster")}: {name} ({boundName})", IsChecked = isDefault ? usesDefault : binding == libraryId, IsEnabled = !isDefault || !usesDefault };
            content.Children.Add(check);
            items.Add((name, student, check));
        }
        var result = await new FAContentDialog
        {
            Title = LinkRostersText,
            Content = new ScrollViewer { Content = content, MaxHeight = 360 },
            PrimaryButtonText = Text("C_Save"), CloseButtonText = Text("C_Cancel")
        }.ShowAsync(top);
        if (result != FAContentDialogResult.Primary) return;
        try
        {
            foreach (var (name, student, check) in items)
            {
                var roster = student ? (SecRandom.Shared.Interfaces.IAttachableSettingsObject?)catalog.LoadStudentList(name) : catalog.LoadPrizeList(name);
                if (roster is null) continue;
                var binding = TextLibraryBinding.Get(roster);
                if (check.IsChecked == true)
                {
                    if (isDefault ? string.IsNullOrEmpty(binding) || binding == libraryId : binding == libraryId) continue;
                    TextLibraryBinding.Set(roster, isDefault ? null : libraryId);
                }
                else if (!isDefault && binding == libraryId)
                    TextLibraryBinding.Set(roster, null);
                else continue;
                var saved = student ? catalog.SaveStudentList((SecRandom.Shared.Models.Profile.StudentList)roster) : catalog.SavePrizeList((SecRandom.Shared.Models.Profile.PrizeList)roster);
                if (!saved) throw new InvalidOperationException(name);
            }
        }
        catch (Exception exception) { this.ShowErrorToast(Text("M_OperationFailed"), exception); }
    }
    private async void DeleteLibrary_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_textLibraryService is null || _selectedLibrary is not { IsDefault: false } selected || TopLevel.GetTopLevel(this) is not { } top) return;
        var affected = new System.Collections.Generic.List<string>();
        var catalog = IAppHost.TryGetService<IProfileCatalogManager>();
        if (catalog is not null)
        {
            affected.AddRange(catalog.GetStudentListNames().Where(name => TextLibraryBinding.Get(catalog.LoadStudentList(name)) == selected.Id));
            affected.AddRange(catalog.GetPrizeListNames().Where(name => TextLibraryBinding.Get(catalog.LoadPrizeList(name)) == selected.Id));
        }
        var content = string.Format(Text("M_DeleteLibrary"), selected.Name);
        if (affected.Count > 0) content += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, affected);
        var result = await new FAContentDialog { Title = DeleteLibraryText, Content = content, PrimaryButtonText = Text("C_Delete"), CloseButtonText = Text("C_Cancel"), DefaultButton = FAContentDialogButton.Close }.ShowAsync(top);
        if (result == FAContentDialogResult.Primary) { _textLibraryService.DeleteLibrary(selected.Id); RebuildRows(); }
    }

    private void SetEntryEnabled(string id, bool enabled)
    {
        if (_textLibraryService is null)
            return;
        _textLibraryService.SetEnabled(id, enabled);
    }

    private async void OpenImportDrawer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_textLibraryService is null)
            return;
        SettingsView.Current?.OpenDrawer(new TextLibraryImportDrawer(_textLibraryService));
        await Task.CompletedTask;
    }

    private async void OpenExportDrawer_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_textLibraryService is null)
            return;
        SettingsView.Current?.OpenDrawer(new TextLibraryExportDrawer(_textLibraryService));
        await Task.CompletedTask;
    }

    private void Refresh_OnClick(object? sender, RoutedEventArgs e)
    {
        _textLibraryService?.Reload();
        RebuildRows();
    }

    private async void RestoreDefaults_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_textLibraryService is null)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var result = await new FAContentDialog
        {
            Title = Text("M_RestoreDefaultsTitle"),
            Content = Text("M_RestoreDefaultsContent"),
            PrimaryButtonText = Text("C_RestoreDefaults"),
            CloseButtonText = Text("C_Cancel"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary)
            return;

        try
        {
            _textLibraryService.RestoreDefaults();
            this.ShowSuccessToast(Text("M_RestoreDefaultsDone"));
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(Text("M_OperationFailed"), exception);
        }
    }

    private async void ResetCounts_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_textLibraryService is null)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var result = await new FAContentDialog
        {
            Title = Text("M_ResetCountsTitle"),
            Content = Text("M_ResetCountsContent"),
            PrimaryButtonText = Text("C_ResetCounts"),
            CloseButtonText = Text("C_Cancel"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary)
            return;

        try
        {
            _textLibraryService.ResetCounts();
            this.ShowSuccessToast(Text("M_ResetCountsDone"));
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(Text("M_OperationFailed"), exception);
        }
    }

    private async void EditEntry_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_textLibraryService is null || sender is not Button { DataContext: TextLibraryRow row })
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var textBox = new TextBox
        {
            Text = row.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            MaxLength = TextLibraryImporter.MaxTextLength,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        var hint = new TextBlock
        {
            Text = string.Format(Text("M_EditHint"), TextLibraryImporter.MaxTextLength),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        };
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        error.Classes.Add("accent");
        error.Foreground = Brushes.OrangeRed;

        var dialog = new FAContentDialog
        {
            Title = Text("M_EditTitle"),
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { textBox, hint, error }
            },
            PrimaryButtonText = Text("C_Save"),
            CloseButtonText = Text("C_Cancel"),
            DefaultButton = FAContentDialogButton.Primary
        };

        void Validate()
        {
            var normalized = TextLibraryImporter.NormalizeTemplate(textBox.Text, out _);
            var message = string.Empty;
            if (normalized.Length == 0)
                message = Text("M_EditEmpty");
            else if (normalized.Length > TextLibraryImporter.MaxTextLength)
                message = string.Format(Text("M_EditTooLong"), TextLibraryImporter.MaxTextLength);
            error.Text = message;
            error.IsVisible = message.Length > 0;
            dialog.IsPrimaryButtonEnabled = message.Length == 0;
        }

        textBox.TextChanged += (_, _) => Validate();
        Validate();

        var result = await dialog.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary)
            return;

        try
        {
            var normalized = TextLibraryImporter.NormalizeTemplate(textBox.Text, out var appendedPlaceholder);
            if (!_textLibraryService.Update(row.Id, normalized))
                this.ShowWarningToast(string.Format(Text("M_EditMissing"), row.Text));
            else if (appendedPlaceholder)
                this.ShowToast(Text("M_EditPlaceholderAppended"));
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(Text("M_OperationFailed"), exception);
        }
    }

    private async void DeleteEntry_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_textLibraryService is null || sender is not Button { DataContext: TextLibraryRow row })
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var result = await new FAContentDialog
        {
            Title = Text("M_DeleteTitle"),
            Content = string.Format(Text("M_DeleteContent"), Shorten(row.Text)),
            PrimaryButtonText = Text("C_Delete"),
            CloseButtonText = Text("C_Cancel"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary)
            return;

        try
        {
            if (_textLibraryService.Remove(row.Id))
                this.ShowToast(Text("M_DeleteDone"));
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(Text("M_OperationFailed"), exception);
        }
    }

    private static string Shorten(string value) =>
        value.Length <= 40 ? value : string.Concat(value.AsSpan(0, 40), "…");

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnPropertyChanged(string propertyName) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record TextLibraryOption(string Id, string Name, bool IsDefault);

/// <summary>
/// One grid row of the text library. The enable switch writes straight through to the library so the
/// persisted rotation filter changes together with the grid.
/// </summary>
public sealed class TextLibraryRow : INotifyPropertyChanged
{
    private readonly Action<string, bool> _setEnabled;
    private bool _enabled;

    public TextLibraryRow(TextLibraryEntry entry, string shareText, Action<string, bool> setEnabled)
    {
        Id = entry.Id;
        Text = entry.Text;
        UseCount = entry.UseCount;
        ShareText = shareText;
        _enabled = entry.Enabled;
        _setEnabled = setEnabled;
    }

    public string Id { get; }

    public string Text { get; }

    public int UseCount { get; }

    public string ShareText { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            _setEnabled(Id, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
