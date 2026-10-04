using SecRandom.Core.Services.Profiles;

namespace SecRandom.Core.Tests;

public sealed class RosterImportRegionTests
{
    private static readonly string[] RollCallKeywords =
        ["学号", "编号", "姓名", "名字", "性别", "分组", "小组", "标签"];

    [Fact]
    public void DetectHeaderRow_SkipsTitleAndBlankRows()
    {
        string?[][] grid =
        [
            ["高一(1)班名单", null, null],
            [null, null, null],
            ["学号", "姓名", "性别"],
            ["1", "张三", "男"]
        ];

        Assert.Equal(3, RosterImportRegionBuilder.DetectHeaderRow(grid, RollCallKeywords));
    }

    [Fact]
    public void DetectHeaderRow_FallsBackToFirstRowWhenNothingMatches()
    {
        string?[][] grid =
        [
            ["1", "张三", "男"],
            ["2", "李四", "女"]
        ];

        // 认不出表头时保留「第一行即表头」的历史行为
        Assert.Equal(1, RosterImportRegionBuilder.DetectHeaderRow(grid, RollCallKeywords));
    }

    [Fact]
    public void DetectHeaderRow_PrefersTheHeaderOverDataRows()
    {
        string?[][] grid =
        [
            ["备注：本表由学籍系统导出", null],
            ["学号", "姓名"],
            ["1001", "王五"]
        ];

        Assert.Equal(2, RosterImportRegionBuilder.DetectHeaderRow(grid, RollCallKeywords));
    }

    [Fact]
    public void Build_UsesSelectedHeaderRowAndSkipsLeadingRows()
    {
        string?[][] grid =
        [
            ["高一(1)班名单", null, null],
            [null, null, null],
            ["学号", "姓名", "性别"],
            ["1", "张三", "男"],
            ["2", "李四", "女"]
        ];
        var region = RosterImportRegion.Default(3);

        var table = RosterImportRegionBuilder.Build(grid, region);

        Assert.Equal(["学号", "姓名", "性别"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("1", table.Rows[0]["学号"]);
        Assert.Equal("张三", table.Rows[0]["姓名"]);
        Assert.Equal("李四", table.Rows[1]["姓名"]);
    }

    [Fact]
    public void Build_WithoutHeaderRowFallsBackToColumnLetters()
    {
        string?[][] grid =
        [
            ["1", "张三", "男"],
            ["2", "李四", "女"]
        ];
        var region = new RosterImportRegion(null, 1, null, 1, null);

        var table = RosterImportRegionBuilder.Build(grid, region);

        Assert.Equal(["A", "B", "C"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("张三", table.Rows[0]["B"]);
    }

    [Fact]
    public void Build_DeduplicatesRepeatedHeaderNames()
    {
        string?[][] grid =
        [
            ["学号", "姓名", "", "姓名"],
            ["1", "张三", "x", "李四"]
        ];
        var region = RosterImportRegion.Default(1);

        var table = RosterImportRegionBuilder.Build(grid, region);

        Assert.Equal(["学号", "姓名", "C", "姓名 (2)"], table.Columns);
        Assert.Equal("张三", table.Rows[0]["姓名"]);
        Assert.Equal("李四", table.Rows[0]["姓名 (2)"]);
    }

    [Fact]
    public void Build_HonoursDataRowAndColumnRange()
    {
        string?[][] grid =
        [
            ["序号", "学号", "姓名"],
            ["1", "1001", "张三"],
            ["2", "1002", "李四"],
            ["合计", null, "2 人"]
        ];
        // 跳过首列序号列，只取第 2 行至第 3 行的数据
        var region = new RosterImportRegion(1, 2, 3, 2, 3);

        var table = RosterImportRegionBuilder.Build(grid, region);

        Assert.Equal(["学号", "姓名"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("1001", table.Rows[0]["学号"]);
        Assert.Equal("李四", table.Rows[1]["姓名"]);
        // 第 4 行的合计行在区域之外，不会被导入
        Assert.DoesNotContain(table.Rows, row => row["学号"] == "合计");
    }

    [Fact]
    public void Build_TrimsTrailingEmptyColumnsAndBlankRows()
    {
        string?[][] grid =
        [
            ["学号", "姓名", null, null],
            ["1", "张三", null, null],
            [null, null, null, null],
            ["2", "李四", null, null]
        ];
        var region = RosterImportRegion.Default(1);

        var table = RosterImportRegionBuilder.Build(grid, region);

        Assert.Equal(["学号", "姓名"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
    }

    [Fact]
    public void Build_ReturnsEmptyTableWhenRangeIsOutOfBounds()
    {
        string?[][] grid = [["学号", "姓名"], ["1", "张三"]];
        var region = new RosterImportRegion(1, 10, null, 1, null);

        var table = RosterImportRegionBuilder.Build(grid, region);

        Assert.Empty(table.Columns);
        Assert.Empty(table.Rows);
        Assert.Empty(RosterImportRegionBuilder.Build([], RosterImportRegion.Default(1)).Rows);
    }

    [Fact]
    public void ResolveLastContentRow_IgnoresTrailingAndInteriorBlankRows()
    {
        string?[][] grid =
        [
            ["学号", "姓名"],
            ["1", "张三"],
            [null, null],
            ["2", "李四"],
            [null, null],
            [null, null]
        ];

        Assert.Equal(4, RosterImportRegionBuilder.ResolveLastContentRow(grid));
        Assert.Equal(0, RosterImportRegionBuilder.ResolveLastContentRow([]));
        Assert.Equal(0, RosterImportRegionBuilder.ResolveLastContentRow([[null, "  "], [null, null]]));
    }

    [Fact]
    public void Build_DefaultLastDataRowStopsAtTheLastContentRow()
    {
        string?[][] grid =
        [
            ["学号", "姓名"],
            ["1", "张三"],
            [null, null],
            [null, null]
        ];

        // LastDataRow 为 null 时不再用网格长度兜底，避免把尾部幽灵空行算进范围
        var table = RosterImportRegionBuilder.Build(grid, new RosterImportRegion(1, 2, null, 1, null));

        var row = Assert.Single(table.Rows);
        Assert.Equal("1", row["学号"]);
    }

    [Fact]
    public void Build_ExcludesTheHeaderRowFromTheDataRange()
    {
        string?[][] grid = [["学号", "姓名"], ["1", "张三"]];

        // 表头行落在最后一行时没有可导入的数据，表头本身不会被当成一条数据
        var headerIsLastRow = RosterImportRegionBuilder.Build(grid, new RosterImportRegion(2, 2, null, 1, null));
        Assert.Empty(headerIsLastRow.Columns);
        Assert.Empty(headerIsLastRow.Rows);

        // 数据起始行不晚于表头行时，同样从表头行的下一行开始
        var overlapping = RosterImportRegionBuilder.Build(grid, new RosterImportRegion(1, 1, null, 1, null));
        var row = Assert.Single(overlapping.Rows);
        Assert.Equal("1", row["学号"]);
    }

    [Fact]
    public void Build_DefaultRegionKeepsLegacyFirstRowHeaderBehaviour()
    {
        string?[][] grid =
        [
            ["学号", "姓名", "标签"],
            ["1", "张三", "a,b"],
            ["2", "李四", null]
        ];

        // 改造前 useHeaderRow:true 的等价输入：第一行当表头，其余行做数据
        var table = RosterImportRegionBuilder.Build(grid, new RosterImportRegion(1, 2, null, 1, null));

        Assert.Equal(["学号", "姓名", "标签"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("a,b", table.Rows[0]["标签"]);
        Assert.Equal(string.Empty, table.Rows[1]["标签"]);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    [InlineData(0, "A")]
    public void ColumnName_MapsExcelColumnLetters(int column, string expected)
    {
        Assert.Equal(expected, RosterImportRegionBuilder.ColumnName(column));
    }

    [Fact]
    public void ScoreRow_MatchesExactHeaderCellsAndIgnoresBlanks()
    {
        Assert.True(RosterImportRegionBuilder.ScoreRow(["学号", "姓名", null], RollCallKeywords) >
                    RosterImportRegionBuilder.ScoreRow(["备注", null, null], RollCallKeywords));
        Assert.Equal(0, RosterImportRegionBuilder.ScoreRow([null, "  "], RollCallKeywords));
    }
}
