using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Controls;

namespace SecRandom.Views.SettingsPages.Personalized;

/// <summary>
///     内置图标选择对话框：自定义按钮和收纳手柄共用同一套搜索、预览和选中逻辑，
///     这样两处的图标列表（精选优先、搜索上限）不会各写一份。
/// </summary>
internal static class FloatingWindowIconPicker
{
    /// <summary>弹出图标选择器；用户取消或没有选中时返回 <c>null</c>。</summary>
    public static async Task<IconChoice?> PickBuiltInAsync(TopLevel topLevel)
    {
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
                : new FluentIcon(choice.Glyph, 20)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                })
        });
        list.Columns.Add(new DataGridTextColumn
        {
            Header = FloatingWindowButtonUi.Text("C_Label"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(IconChoice.Name))
        });

        var panel = new StackPanel
        {
            Spacing = 8,
            Children = { search, count, list }
        };

        var dialog = new FAContentDialog
        {
            Title = FloatingWindowButtonUi.Text("M_IconPickerTitle"),
            Content = panel,
            PrimaryButtonText = FloatingWindowButtonUi.Text("C_UseIcon"),
            CloseButtonText = FloatingWindowButtonUi.Text("C_Cancel"),
            DefaultButton = FAContentDialogButton.Primary,
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
        return result == FAContentDialogResult.Primary && list.SelectedItem is IconChoice choice
            ? choice
            : null;
    }
}