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

        var search = new TextBox
        {
            PlaceholderText = FloatingWindowButtonUi.Text("M_IconSearchPlaceholder"),
            Width = 430
        };

        var count = new TextBlock { Classes = { "caption" } };
        var list = new DataGrid
        {
            Height = 320,
            Width = 430,
            AutoGenerateColumns = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        list.Columns.Add(new DataGridTemplateColumn
        {
            Header = FloatingWindowButtonUi.Text("C_Icon"),
            Width = new DataGridLength(64),
            CellTemplate = new FuncDataTemplate<IconChoice>((choice, _) => choice is null
                ? null
                : new SecRandom.Core.Controls.FluentIcon(choice.Glyph, 20)
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                })
        });
        list.Columns.Add(new DataGridTextColumn
        {
            Header = FloatingWindowButtonUi.Text("C_Label"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new Avalonia.Data.Binding(nameof(IconChoice.Name))
        });

        var panel = new StackPanel
        {
            Spacing = 8,
            Children = { search, count, list }
        };

        var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
        {
            Title = FloatingWindowButtonUi.Text("M_IconPickerTitle"),
            Content = panel,
            PrimaryButtonText = FloatingWindowButtonUi.Text("C_UseIcon"),
            CloseButtonText = FloatingWindowButtonUi.Text("C_Cancel"),
            DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        void ApplyFilter()
        {
            var matches = FloatingWindowButtonUi.FilterIcons(search.Text);
            list.ItemsSource = matches;
            count.Text = FloatingWindowButtonUi.Format("M_IconCountFormat", matches.Count);
        }

        list.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = list.SelectedItem is IconChoice;
        search.TextChanged += (_, _) => ApplyFilter();
        ApplyFilter();

        var result = await dialog.ShowAsync(topLevel);
        if (result != FluentAvalonia.UI.Controls.FAContentDialogResult.Primary ||
            list.SelectedItem is not IconChoice choice)
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
