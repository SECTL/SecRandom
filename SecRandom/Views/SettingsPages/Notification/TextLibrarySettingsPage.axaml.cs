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

    public TextLibrarySettingsPage()
    {
        _textLibraryService = IAppHost.TryGetService<TextLibraryService>();
        if (_textLibraryService is not null)
            _textLibraryService.Changed += OnLibraryChanged;

        RebuildRows();
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => RebuildRows();
        Unloaded += (_, _) =>
        {
            if (_textLibraryService is not null)
                _textLibraryService.Changed -= OnLibraryChanged;
        };
    }

    public ObservableCollection<TextLibraryRow> Rows { get; } = [];

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

    public bool IsEmpty => TotalCount == 0;

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
