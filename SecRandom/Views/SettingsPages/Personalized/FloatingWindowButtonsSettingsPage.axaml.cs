using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Services.FloatingWindow;
using SecRandom.ViewModels;
using SecRandom.Views;
using LSP = SecRandom.Langs.SettingsPages.Personalized.FloatingWindowButtons.Resources;

namespace SecRandom.Views.SettingsPages.Personalized;

/// <summary>
///     管理悬浮窗上的自定义按钮：顶部工具栏 + 只读表格展示，
///     新增/编辑在右侧抽屉里完成。页面不出现在侧栏（由浮窗管理页的设置卡跳转进来）。
/// </summary>
[PageInfo("settings.personalized.floatingWindowButtons", FluentIcons.AppsListDetailFilled, "settings.personalized",
    isHide: true)]
public partial class FloatingWindowButtonsSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly FloatingWindowIconService? _iconService;
    private FloatingWindowButtonRow? _selectedRow;

    public FloatingWindowButtonsSettingsPage()
    {
        Settings = ViewModel.Config.FloatingWindowSettings;
        _iconService = IAppHost.TryGetService<FloatingWindowIconService>();

        foreach (var config in Settings.CustomButtons)
            AddRow(config);

        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();

    public ObservableCollection<FloatingWindowButtonRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public string CountText => string.Format(CultureInfo.CurrentCulture, Text("M_CountFormat"), Rows.Count,
        FloatingWindowButtonCatalog.MaxButtons);

    public bool CanEdit => _selectedRow is not null;
    public bool CanDelete => _selectedRow is not null;

    public bool CanMoveUp => _selectedRow is not null && Rows.IndexOf(_selectedRow) > 0;

    public bool CanMoveDown => _selectedRow is not null && Rows.IndexOf(_selectedRow) is var index && index >= 0 &&
                               index < Rows.Count - 1;

    private PropertyChangedEventHandler? _propertyChangedHandler;

    // UserControl 继承自 AvaloniaObject，已经有一个同名 PropertyChanged 事件；
    // 这里显式实现 INotifyPropertyChanged，避免隐藏框架事件。
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChangedHandler += value;
        remove => _propertyChangedHandler -= value;
    }

    private FloatingWindowSettingsConfig Settings { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private static string Text(string key) => FloatingWindowButtonUi.Text(key);

    private static string Format(string key, params object?[] args) => FloatingWindowButtonUi.Format(key, args);

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _iconService?.Prune(Rows.Select(row => row.Config.IconImage));
    }

    private void AddRow(FloatingWindowButtonConfig config)
    {
        var preview = _iconService?.TryLoadBitmap(config.IconImage);
        Rows.Add(new FloatingWindowButtonRow(config, preview));
    }

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    private void Persist()
    {
        try
        {
            Settings.CustomButtons = Rows.Select(row => row.Config).ToList();
            ConfigHandler.Save();
        }
        catch (Exception e)
        {
            this.ShowErrorToast(Text("M_SaveFailed"), e);
        }

        NotifyListState();
    }

    private void RowsGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedRow = RowsGrid.SelectedItem as FloatingWindowButtonRow;
        NotifyListState();
    }

    private void RowsGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is FloatingWindowButtonRow row)
            OpenEditor(row);
    }

    private void AddButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Rows.Count >= FloatingWindowButtonCatalog.MaxButtons)
        {
            this.ShowWarningToast(Format("M_AddLimit", FloatingWindowButtonCatalog.MaxButtons));
            return;
        }

        var label = string.Format(CultureInfo.CurrentCulture, Text("M_DefaultLabel"), Rows.Count + 1);
        if (label.Length > FloatingWindowButtonCatalog.MaxLabelLength)
            label = label[..FloatingWindowButtonCatalog.MaxLabelLength];

        var config = new FloatingWindowButtonConfig
        {
            Label = label,
            IconGlyph = FloatingWindowButtonCatalog.GetSuggestedIcon(FloatingWindowButtonActionKind.MainPage),
            ActionKind = FloatingWindowButtonActionKind.MainPage,
            ActionTarget = FloatingWindowButtonCatalog.GetDefaultTarget(FloatingWindowButtonActionKind.MainPage)
        };

        OpenEditor(config, isNew: true);
    }

    private void EditButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedRow is { } row)
            OpenEditor(row);
    }

    private void EditRow_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FloatingWindowButtonRow row)
            OpenEditor(row);
    }

    private void DeleteSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedRow is { } row)
            _ = DeleteRowAsync(row);
    }

    private void DeleteRow_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FloatingWindowButtonRow row)
            _ = DeleteRowAsync(row);
    }

    private async Task DeleteRowAsync(FloatingWindowButtonRow row)
    {
        if (!await ConfirmAsync(Text("M_DeleteTitle"), Format("M_DeleteContent", row.Label), Text("C_Delete")))
            return;

        var iconFile = row.Config.IconImage;
        Rows.Remove(row);
        _selectedRow = RowsGrid.SelectedItem as FloatingWindowButtonRow;
        Persist();

        if (!IsIconInUse(iconFile))
            _iconService?.TryDelete(iconFile);

        this.ShowSuccessToast(Text("M_DeleteDone"));
    }

    private void MoveUp_OnClick(object? sender, RoutedEventArgs e)
    {
        MoveSelected(-1);
    }

    private void MoveDown_OnClick(object? sender, RoutedEventArgs e)
    {
        MoveSelected(1);
    }

    private void MoveSelected(int offset)
    {
        if (_selectedRow is not { } row)
            return;

        var index = Rows.IndexOf(row);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Rows.Count)
            return;

        Rows.Move(index, target);
        Persist();
        RowsGrid.SelectedItem = row;
        _selectedRow = row;
        NotifyListState();
    }

    private async void ClearAll_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Rows.Count == 0)
            return;

        if (!await ConfirmAsync(Text("M_ClearTitle"), Text("M_ClearContent"), Text("C_Clear")))
            return;

        Rows.Clear();
        _selectedRow = null;
        Persist();
        _iconService?.Prune([]);
        this.ShowSuccessToast(Text("M_ClearDone"));
    }

    private void OpenEditor(FloatingWindowButtonRow row)
    {
        OpenEditor(CloneConfig(row.Config), isNew: false);
    }

    private void OpenEditor(FloatingWindowButtonConfig config, bool isNew)
    {
        var editor = new FloatingWindowButtonEditorView(config, isNew, OnEditorSaved, IsIconInUse);
        SettingsView.Current?.OpenDrawer(editor);
    }

    private void OnEditorSaved(FloatingWindowButtonConfig saved)
    {
        var previousIcon = Rows.FirstOrDefault(row => row.Config.Id == saved.Id)?.Config.IconImage;

        var index = Settings.CustomButtons.FindIndex(config => config.Id == saved.Id);
        if (index >= 0)
            Settings.CustomButtons[index] = saved;
        else
            Settings.CustomButtons.Add(saved);

        // 用保存后的配置重建表格行；Persist 会把这个列表写回配置并通知浮窗刷新。
        Rows.Clear();
        foreach (var config in Settings.CustomButtons)
            AddRow(config);

        _selectedRow = Rows.FirstOrDefault(row => row.Config.Id == saved.Id);
        RowsGrid.SelectedItem = _selectedRow;
        Persist();

        if (!string.IsNullOrWhiteSpace(previousIcon) && previousIcon != saved.IconImage && !IsIconInUse(previousIcon))
            _iconService?.TryDelete(previousIcon);
    }

    private bool IsIconInUse(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        return Rows.Any(row => string.Equals(row.Config.IconImage, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static FloatingWindowButtonConfig CloneConfig(FloatingWindowButtonConfig source)
    {
        return new FloatingWindowButtonConfig
        {
            Id = source.Id,
            Enabled = source.Enabled,
            Label = source.Label,
            IconGlyph = source.IconGlyph,
            IconImage = source.IconImage,
            ActionKind = source.ActionKind,
            ActionTarget = source.ActionTarget
        };
    }

    private async Task<bool> ConfirmAsync(string title, string content, string primaryText)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return false;

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = Text("C_Cancel"),
            DefaultButton = FAContentDialogButton.Close
        };

        return await dialog.ShowAsync(topLevel) == FAContentDialogResult.Primary;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        _propertyChangedHandler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>只读表格里的一行：把配置投影成表格列需要的展示值。</summary>
public sealed class FloatingWindowButtonRow
{
    public FloatingWindowButtonRow(FloatingWindowButtonConfig config, Bitmap? iconPreview)
    {
        Config = config;
        IconPreview = iconPreview;
    }

    public FloatingWindowButtonConfig Config { get; }

    public string Label => Config.Label;

    public string IconGlyph => Config.IconGlyph;

    public bool Enabled => Config.Enabled;

    public bool HasCustomImage => IconPreview is not null;

    public bool ShowGlyphIcon => IconPreview is null;

    public Bitmap? IconPreview { get; }

    public string ActionLabel => FloatingWindowButtonUi.ActionLabel(Config.ActionKind);

    public string TargetLabel => FloatingWindowButtonUi.TargetLabel(Config.ActionKind, Config.ActionTarget);
}
