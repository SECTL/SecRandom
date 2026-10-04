using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SecRandom.Core.Services.Profiles;
using SecRandom.Views.SettingsPages.ListManagement;

namespace SecRandom.Mobile.Tests;

/// <summary>
/// 名单导入的「导入区域」选择器由两个桌面名单抽屉和 OOBE 共用，这里借用本仓库唯一的 Avalonia Headless
/// 测试宿主，验证它的 XAML 能加载、区域状态能驱动 <see cref="RosterImportRegionSelector.RegionChanged"/>。
/// 断言只用与语言无关的行号，避免依赖运行环境的 UI 区域性。
/// </summary>
public sealed class RosterImportRegionSelectorTests
{
    private static readonly string[] Keywords = ["学号", "编号", "姓名", "性别", "分组", "标签"];

    private static IReadOnlyList<IReadOnlyList<string?>> Grid =>
    [
        ["高一(1)班名单", null, null],
        [null, null, null],
        ["学号", "姓名", "性别"],
        ["1", "张三", "男"],
        ["2", "李四", "女"]
    ];

    [AvaloniaFact]
    public void LoadDetectsHeaderRowAndExposesDefaultRegion()
    {
        var selector = new RosterImportRegionSelector();

        selector.Load(["学生名单", "奖品表"], "学生名单", Grid, Keywords);

        Assert.True(selector.HasGrid);
        Assert.True(selector.HasSheetSelection);
        Assert.Equal("学生名单", selector.SelectedSheet);
        Assert.Equal(3, selector.SelectedHeaderRowOption?.Row);
        Assert.Equal(3, selector.CurrentRegion.HeaderRow);
        Assert.Equal(4, selector.CurrentRegion.FirstDataRow);
        Assert.Equal(5, selector.CurrentRegion.LastDataRow);
        Assert.Equal(5m, selector.RowCountMaximum);
        // 整张 5 行的表都在原始预览里，含标题行与空行
        Assert.Equal(5, selector.RawRows.Count);
        Assert.Equal(3, selector.SelectedRawRow?.RowNumber);
    }

    [AvaloniaFact]
    public void SelectingRawRowAndHeaderRowOptionDriveTheRegion()
    {
        var selector = new RosterImportRegionSelector();
        selector.Load([], null, Grid, Keywords);
        var changes = 0;
        selector.RegionChanged += (_, _) => changes++;

        // 点原始预览里的第 1 行即把表头行改到第 1 行
        selector.SelectedRawRow = selector.RawRows[0];

        Assert.Equal(1, selector.CurrentRegion.HeaderRow);
        Assert.Equal(2, selector.CurrentRegion.FirstDataRow);
        Assert.Equal(1, changes);

        // 下拉里的「无表头」项 Row 为 null
        var none = Assert.Single(selector.HeaderRowOptions, option => option.Row is null);
        selector.SelectedHeaderRowOption = none;

        Assert.Null(selector.CurrentRegion.HeaderRow);
        Assert.Equal(1, selector.CurrentRegion.FirstDataRow);
        Assert.Equal(2, changes);
        Assert.Null(selector.SelectedRawRow);
    }

    [AvaloniaFact]
    public void SheetSelectionRaisesSheetChangedAndRangeIsClamped()
    {
        var selector = new RosterImportRegionSelector();
        selector.Load(["学生名单", "奖品表"], "学生名单", Grid, Keywords);
        string? requested = null;
        selector.SheetSelectionChanged += (_, name) => requested = name;

        selector.SelectedSheet = "奖品表";
        Assert.Equal("奖品表", requested);

        selector.DataLastRow = 4;
        selector.DataFirstRow = 5;
        // 起始行不能越过结束行
        Assert.Equal(5, selector.CurrentRegion.FirstDataRow);
        Assert.Equal(5, selector.CurrentRegion.LastDataRow);

        // 超出表格行数与非数字输入都被钳回合法范围
        selector.DataLastRow = 99;
        Assert.Equal(5, selector.CurrentRegion.LastDataRow);
        selector.DataFirstRow = null;
        selector.DataLastRow = null;
        Assert.Equal(5, selector.CurrentRegion.FirstDataRow);
        Assert.Equal(5, selector.CurrentRegion.LastDataRow);
    }

    [AvaloniaFact]
    public void SingleSheetWorkbookHidesSheetSelectionAndClearResets()
    {
        var selector = new RosterImportRegionSelector();
        selector.Load([], null, Grid, Keywords);
        Assert.False(selector.HasSheetSelection);

        selector.Clear();

        Assert.False(selector.HasGrid);
        Assert.Empty(selector.RawRows);
        Assert.Empty(selector.HeaderRowOptions);
        Assert.Empty(selector.SheetOptions);
    }

    [AvaloniaFact]
    public void NoHeaderRowOptionComesFirstButIsNotTheDefault()
    {
        var selector = new RosterImportRegionSelector();

        selector.Load([], null, Grid, Keywords);

        // 「无表头」排在最前面，便于点击
        Assert.Null(selector.HeaderRowOptions[0].Row);
        // 但默认选中的仍是自动识别出的表头行
        Assert.Equal(3, selector.SelectedHeaderRowOption?.Row);
        Assert.Equal(3, selector.CurrentRegion.HeaderRow);
    }

    [AvaloniaFact]
    public void TrailingBlankRowsDoNotRaiseRowCountOrFillThePreview()
    {
        var selector = new RosterImportRegionSelector();
        // MiniExcel 会把尾部只有格式的空行补成整行：网格长度 300，内容只到第 2 行
        var grid = new List<IReadOnlyList<string?>>();
        grid.Add(["学号", "姓名"]);
        grid.Add(["1", "张三"]);
        for (var row = 0; row < 298; row++)
            grid.Add([null, null]);
        Assert.Equal(300, grid.Count);

        selector.Load([], null, grid, Keywords);

        Assert.Equal(2m, selector.RowCountMaximum);
        Assert.Equal(2, selector.CurrentRegion.LastDataRow);
        Assert.Equal(2, selector.RawRows.Count);
        Assert.Equal(2, RosterImportRegionBuilder.ResolveLastContentRow(grid));
    }

    [AvaloniaFact]
    public void RawRowPreviewKeepsFiveRowsAndSlidesWithTheHeaderRow()
    {
        var selector = new RosterImportRegionSelector();
        var grid = new List<IReadOnlyList<string?>>();
        grid.Add(["学号", "姓名", "性别", "小组"]);
        for (var row = 1; row <= 30; row++)
            grid.Add([row.ToString(), $"学生{row:00}", "男", "第三小组"]);

        selector.Load([], null, grid, Keywords);

        // 列表里始终只有 5 行，不是装载 20 行再滚动
        Assert.Equal(5, selector.RawRows.Count);
        Assert.Equal([1, 2, 3, 4, 5], selector.RawRows.Select(row => row.RowNumber));

        // 往下选表头行，窗口跟着滑下去
        selector.SelectedHeaderRowOption = selector.HeaderRowOptions.Single(option => option.Row == 12);
        Assert.Equal(5, selector.RawRows.Count);
        Assert.Equal([10, 11, 12, 13, 14], selector.RawRows.Select(row => row.RowNumber));
        Assert.Equal(12, selector.SelectedRawRow?.RowNumber);

        // 滑到表格最后一行时窗口贴底，不会越出内容范围（网格共 31 行：1 行表头 + 30 行数据）
        var lastRow = RosterImportRegionBuilder.ResolveLastContentRow(grid);
        Assert.Equal(31, lastRow);
        selector.SelectedHeaderRowOption = selector.HeaderRowOptions.Single(option => option.Row == lastRow);
        Assert.Equal([27, 28, 29, 30, 31], selector.RawRows.Select(row => row.RowNumber));

        // 选「无表头」回到表格开头
        selector.SelectedHeaderRowOption = selector.HeaderRowOptions.Single(option => option.Row is null);
        Assert.Equal([1, 2, 3, 4, 5], selector.RawRows.Select(row => row.RowNumber));
        Assert.Null(selector.SelectedRawRow);
    }

    [AvaloniaFact]
    public void RawRowPreviewFitsAllFiveRowsWithoutScrolling()
    {
        var selector = new RosterImportRegionSelector();
        var grid = new List<IReadOnlyList<string?>>();
        grid.Add(["学号", "姓名", "性别", "小组"]);
        for (var row = 1; row <= 30; row++)
            grid.Add([row.ToString(), $"学生{row:00}", "男", "第三小组"]);

        selector.Load([], null, grid, Keywords);
        Assert.Equal(5, selector.RawRows.Count);

        var window = new Window { Width = 400, Height = 900, Content = selector };
        window.Show();
        window.Measure(new Size(400, 900));
        window.Arrange(new Rect(0, 0, 400, 900));

        var listBox = selector.GetVisualDescendants().OfType<ListBox>().Single();
        var itemHeight = listBox.ContainerFromIndex(0)!.Bounds.Height;

        // 5 行全都放得下，不需要滚动；放不下第 6 行
        Assert.True(listBox.Bounds.Height >= itemHeight * 5,
            $"预览高度 {listBox.Bounds.Height} 放不下 5 行（行高 {itemHeight}）");
        Assert.True(listBox.Bounds.Height < itemHeight * 6,
            $"预览高度 {listBox.Bounds.Height} 会空出第 6 行的位置（行高 {itemHeight}）");
    }
}
