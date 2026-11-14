using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Services.FloatingWindow;
using SecRandom.Views;
using LSP = SecRandom.Langs.SettingsPages.Personalized.FloatingWindowButtons.Resources;

namespace SecRandom.Views.SettingsPages.Personalized;

/// <summary>
///     右侧抽屉里的自定义按钮编辑器：表单式编辑名称、图标、动作和目标，
///     保存后把结果交回列表页，不在表格里做内联编辑。
/// </summary>
public partial class FloatingWindowButtonEditorView : UserControl, IDrawerCloseAware
{
    private readonly FloatingWindowButtonConfig _config;
    private readonly FloatingWindowIconService? _iconService;
    private readonly Func<string?, bool> _isIconInUse;
    private readonly Action<FloatingWindowButtonConfig> _onSave;
    private readonly string? _initialIconImage;
    private bool _saved;

    public FloatingWindowButtonEditorView()
        : this(new FloatingWindowButtonConfig { Label = "Button" }, true, _ => { }, _ => false)
    {
    }

    public FloatingWindowButtonEditorView(FloatingWindowButtonConfig config, bool isNew,
        Action<FloatingWindowButtonConfig> onSave, Func<string?, bool> isIconInUse)
    {
        _config = config;
        _onSave = onSave;
        _isIconInUse = isIconInUse;
        _initialIconImage = config.IconImage;
        _iconService = IAppHost.TryGetService<FloatingWindowIconService>();

        InitializeComponent();

        TitleText.Text = FloatingWindowButtonUi.Text(isNew ? "M_AddTitle" : "M_EditTitle");
        EnabledCheck.IsChecked = config.Enabled;
        LabelBox.Text = config.Label;

        var actionOptions = FloatingWindowButtonUi.BuildActionOptions();
        ActionBox.ItemsSource = actionOptions;
        ActionBox.SelectedItem = actionOptions.FirstOrDefault(option => option.Kind == config.ActionKind)
                                 ?? actionOptions[0];
        ActionBox.SelectionChanged += ActionBox_OnSelectionChanged;

        TargetBox.SelectionChanged += TargetBox_OnSelectionChanged;

        RefreshIconPreview();
        RefreshTargetEditor();
    }

    private FloatingWindowButtonActionKind SelectedKind =>
        ActionBox.SelectedItem is ButtonActionOption option
            ? option.Kind
            : _config.ActionKind;

    private void RefreshIconPreview()
    {
        var bitmap = _iconService?.TryLoadBitmap(_config.IconImage);
        IconImage.Source = bitmap;
        IconImage.IsVisible = bitmap is not null;
        IconGlyph.Glyph = _config.IconGlyph;
        IconGlyph.IsVisible = bitmap is null;
    }

    private void ActionBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ActionBox.SelectedItem is not ButtonActionOption option)
            return;

        if (option.Kind == _config.ActionKind)
            return;

        _config.ActionKind = option.Kind;
        _config.ActionTarget = FloatingWindowButtonCatalog.GetDefaultTarget(option.Kind);
        RefreshTargetEditor();
    }

    private void TargetBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TargetBox.SelectedItem is ButtonTargetOption option)
            _config.ActionTarget = option.Value;
    }

    private void RefreshTargetEditor()
    {
        var kind = SelectedKind;
        TargetSection.IsVisible = kind is FloatingWindowButtonActionKind.MainPage
            or FloatingWindowButtonActionKind.SettingsPage
            or FloatingWindowButtonActionKind.Uri
            or FloatingWindowButtonActionKind.Path;

        var isPageTarget = kind is FloatingWindowButtonActionKind.MainPage
            or FloatingWindowButtonActionKind.SettingsPage;

        TargetBox.IsVisible = isPageTarget;
        TargetTextBox.IsVisible = !isPageTarget && TargetSection.IsVisible;
        PathBrowsePanel.IsVisible = kind == FloatingWindowButtonActionKind.Path;

        if (isPageTarget)
        {
            var options = FloatingWindowButtonUi.BuildTargetOptions(kind, _config.ActionTarget);
            TargetBox.ItemsSource = options;
            var selected = options.FirstOrDefault(option => option.Value == _config.ActionTarget) ?? options.FirstOrDefault();
            TargetBox.SelectedItem = selected;
            _config.ActionTarget = selected?.Value ?? string.Empty;
        }
        else
        {
            TargetBox.ItemsSource = null;
            TargetTextBox.Text = _config.ActionTarget;
        }
    }

    private void PickBuiltInIcon_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = PickBuiltInIconAsync();
    }

    private async Task PickBuiltInIconAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var choice = await FloatingWindowIconPicker.PickBuiltInAsync(topLevel);
        if (choice is null)
            return;

        _config.IconGlyph = choice.Glyph;
        ReplaceIconFile(string.Empty);
        RefreshIconPreview();
    }

    private async void ImportImage_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_iconService is null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = FloatingWindowButtonUi.Text("M_ImportImage"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(FloatingWindowButtonUi.Text("M_ImageType"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.ico"]
                }
            ]
        });

        if (files.Count == 0)
            return;

        await using var stream = await files[0].OpenReadAsync();
        ApplyImportResult(await _iconService.ImportImageAsync(stream));
    }

    private async void ImportApplicationIcon_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_iconService is null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        string? path = null;
        if (OperatingSystem.IsMacOS())
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = FloatingWindowButtonUi.Text("M_ImportApplicationIcon"),
                AllowMultiple = false
            });
            if (folders.Count > 0)
                path = folders[0].TryGetLocalPath();
        }
        else
        {
            var patterns = OperatingSystem.IsWindows()
                ? new[] { "*.exe", "*.lnk", "*.ico", "*.dll" }
                : new[] { "*.desktop" };
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = FloatingWindowButtonUi.Text("M_ImportApplicationIcon"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(FloatingWindowButtonUi.Text("M_ApplicationType")) { Patterns = patterns }
                ]
            });
            if (files.Count > 0)
                path = files[0].TryGetLocalPath();
        }

        if (string.IsNullOrWhiteSpace(path))
            return;

        ApplyImportResult(await _iconService.ImportApplicationIconAsync(path));
    }

    private void ApplyImportResult(FloatingWindowIconImportResult result)
    {
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.FileName))
        {
            this.ShowWarningToast(FloatingWindowButtonUi.DescribeImportError(result.Error));
            return;
        }

        ReplaceIconFile(result.FileName);
        RefreshIconPreview();
        this.ShowSuccessToast(FloatingWindowButtonUi.Text("M_IconUpdated"));
    }

    private void ReplaceIconFile(string fileName)
    {
        var previous = _config.IconImage;
        _config.IconImage = fileName;

        if (string.IsNullOrWhiteSpace(previous) || previous == fileName || previous == _initialIconImage)
            return;

        _iconService?.TryDelete(previous);
    }

    private void ClearCustomIcon_OnClick(object? sender, RoutedEventArgs e)
    {
        ReplaceIconFile(string.Empty);
        if (string.IsNullOrWhiteSpace(_config.IconGlyph))
            _config.IconGlyph = FloatingWindowButtonCatalog.GetSuggestedIcon(_config.ActionKind);
        RefreshIconPreview();
    }

    private async void BrowseFile_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = FloatingWindowButtonUi.Text("M_BrowseFileTitle"),
            AllowMultiple = false
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { Length: > 0 } path)
            TargetTextBox.Text = path;
    }

    private async void BrowseFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = FloatingWindowButtonUi.Text("M_BrowseFolderTitle"),
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path)
            TargetTextBox.Text = path;
    }

    private void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        var kind = SelectedKind;
        _config.Enabled = EnabledCheck.IsChecked == true;
        _config.Label = (LabelBox.Text ?? string.Empty).Trim();
        _config.ActionKind = kind;

        if (kind is FloatingWindowButtonActionKind.MainPage or FloatingWindowButtonActionKind.SettingsPage)
            _config.ActionTarget = (TargetBox.SelectedItem as ButtonTargetOption)?.Value ?? string.Empty;
        else if (kind is FloatingWindowButtonActionKind.Uri or FloatingWindowButtonActionKind.Path)
            _config.ActionTarget = (TargetTextBox.Text ?? string.Empty).Trim();
        else
            _config.ActionTarget = string.Empty;

        var error = FloatingWindowButtonUi.DescribeError(_config);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            return;
        }

        _saved = true;
        _onSave(_config);
        SettingsView.Current?.CloseDrawer();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.CloseDrawer();
    }

    async Task IDrawerCloseAware.OnDrawerClosedAsync()
    {
        if (_saved)
            return;

        var current = _config.IconImage;
        if (string.IsNullOrWhiteSpace(current) || current == _initialIconImage)
            return;

        if (!_isIconInUse(current))
            _iconService?.TryDelete(current);
    }
}
