using System.IO.Compression;
using System.Text;
using MiniExcelLibs;
using SecRandom.Core.Services.Profiles;

namespace SecRandom.Core.Tests;

/// <summary>
/// 走真实 xlsx 的导入链路：MiniExcel 原始读取 → 区域解析 → 名单解析。
/// 用例刻意用行号不连续的工作表，钉住「行序与 Excel 行号一致」这个前提，
/// 界面上的表头行选择依赖它。
/// </summary>
public sealed class RosterImportWorkbookTests
{
    private static readonly string[] RollCallKeywords = ["学号", "编号", "姓名", "性别", "分组", "标签"];

    [Fact]
    public void RegionPipeline_ReadsHeaderFromRealWorkbookRowNumber()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SecRandom-roster-{Guid.NewGuid():N}.xlsx");
        try
        {
            WriteWorkbook(path);

            Assert.Equal(["学生名单", "奖品表"], MiniExcel.GetSheetNames(path));

            var grid = ReadGrid(path, "学生名单");

            // 第 1 行标题、第 2 行在文件里根本不存在、第 3 行才是表头
            Assert.Equal(6, grid.Count);
            Assert.Equal("高一(1)班名单", grid[0][0]);
            Assert.All(grid[1], cell => Assert.True(string.IsNullOrWhiteSpace(cell)));
            Assert.Equal("学号", grid[2][0]);

            var headerRow = RosterImportRegionBuilder.DetectHeaderRow(grid, RollCallKeywords);
            Assert.Equal(3, headerRow);

            // 第 3 行表头 + 第 4 行起的数据；第 4 行本身也是空行
            var table = RosterImportRegionBuilder.Build(grid, RosterImportRegion.Default(headerRow));
            Assert.Equal(["学号", "姓名", "性别"], table.Columns);
            Assert.Equal(2, table.Rows.Count);

            var parsed = RosterImportParser.ParseStudents(
                table.Rows,
                new StudentRosterColumnMapping("学号", "姓名", "性别", null, null));
            Assert.Equal(2, parsed.Items.Count);
            Assert.Equal("1001", parsed.Items[0].Id);
            Assert.Equal("张三", parsed.Items[0].Name);
            Assert.Equal("女", parsed.Items[1].Gender);
            Assert.Empty(parsed.DuplicatedNames);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void RegionPipeline_ReadsSecondWorksheetByName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SecRandom-roster-{Guid.NewGuid():N}.xlsx");
        try
        {
            WriteWorkbook(path);

            // 默认按第一个工作表读，表头在第 3 行，第一列是整表的标题
            var firstSheet = RosterImportRegionBuilder.Build(ReadGrid(path, null), RosterImportRegion.Default(1));
            Assert.Equal("高一(1)班名单", firstSheet.Columns[0]);

            // 指定第二个工作表后，表头就在第 1 行
            var grid = ReadGrid(path, "奖品表");
            Assert.Equal("奖号", grid[0][0]);

            var table = RosterImportRegionBuilder.Build(grid, RosterImportRegion.Default(1));
            Assert.Equal(["奖号", "奖品"], table.Columns);
            var prize = Assert.Single(RosterImportParser.ParsePrizes(
                table.Rows,
                new PrizeRosterColumnMapping("奖号", "奖品", null, null, null)).Items);
            Assert.Equal("笔记本", prize.Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// 表格尾部被按过回车或被格式刷过的空行会留下空的行元素，MiniExcel 会一路补到那一行。
    /// 行数上限必须按最后一个有内容的行算，否则界面上会显示一个虚高的范围。
    /// </summary>
    [Fact]
    public void RegionPipeline_IgnoresTrailingBlankRowsLeftByFormatting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SecRandom-roster-{Guid.NewGuid():N}.xlsx");
        try
        {
            WriteWorkbook(path, withTrailingBlankRow: true);

            var grid = ReadGrid(path, "学生名单");

            // 网格长度被空行元素撑到 500，但最后一个有内容的行仍是第 6 行
            Assert.Equal(500, grid.Count);
            Assert.Equal(6, RosterImportRegionBuilder.ResolveLastContentRow(grid));

            var table = RosterImportRegionBuilder.Build(grid, RosterImportRegion.Default(3));
            Assert.Equal(["学号", "姓名", "性别"], table.Columns);
            Assert.Equal(2, table.Rows.Count);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>手写最小 xlsx：只有第 1、3、5、6 行存在，第 2、4 行没有 row 元素。</summary>
    private static void WriteWorkbook(string path, bool withTrailingBlankRow = false)
    {
        if (File.Exists(path))
            File.Delete(path);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
<Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""");
        Add(zip, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
        Add(zip, "xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<sheets>
<sheet name="学生名单" sheetId="1" r:id="rId1"/>
<sheet name="奖品表" sheetId="2" r:id="rId2"/>
</sheets>
</workbook>
""");
        Add(zip, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
</Relationships>
""");
        Add(zip, "xl/worksheets/sheet1.xml", $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<dimension ref="{(withTrailingBlankRow ? "A1:C500" : "A1:C6")}"/>
<sheetData>
{Row(1, "高一(1)班名单")}
{Row(3, "学号", "姓名", "性别")}
{Row(5, "1001", "张三", "男")}
{Row(6, "1002", "李四", "女")}
{(withTrailingBlankRow ? "<row r=\"500\"/>" : string.Empty)}
</sheetData>
</worksheet>
""");
        Add(zip, "xl/worksheets/sheet2.xml", $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<dimension ref="A1:B2"/>
<sheetData>
{Row(1, "奖号", "奖品")}
{Row(2, "A1", "笔记本")}
</sheetData>
</worksheet>
""");
    }

    private static string Row(int rowNumber, params string[] cells)
    {
        var builder = new StringBuilder($"<row r=\"{rowNumber}\">");
        for (var index = 0; index < cells.Length; index++)
            builder.Append($"<c r=\"{(char)('A' + index)}{rowNumber}\" t=\"inlineStr\"><is><t>{cells[index]}</t></is></c>");

        return builder.Append("</row>").ToString();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static IReadOnlyList<IReadOnlyList<string?>> ReadGrid(string path, string? sheetName)
    {
        return MiniExcel.Query(path, useHeaderRow: false, sheetName: sheetName)
            .Cast<IDictionary<string, object?>>()
            .Select(ToCells)
            .ToList();
    }

    private static string?[] ToCells(IDictionary<string, object?> row)
    {
        var cells = new string?[row.Count];
        foreach (var pair in row)
        {
            var index = RosterImportRegionBuilder.ColumnIndex(pair.Key);
            if (index < 1 || index > cells.Length)
                continue;

            cells[index - 1] = pair.Value?.ToString() ?? string.Empty;
        }

        return cells;
    }
}
