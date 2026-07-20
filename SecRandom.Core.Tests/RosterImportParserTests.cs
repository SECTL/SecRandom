using SecRandom.Core.Services.Profiles;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class RosterImportParserTests
{
    [Fact]
    public void FindBestColumn_PrefersExactMatchOverContains()
    {
        var columns = new[] { "学生学号", "学号" };

        Assert.Equal("学号", RosterImportParser.FindBestColumn(columns, ["学号"]));
    }

    [Fact]
    public void FindBestColumn_EarlierKeywordWinsAndMissReturnsNull()
    {
        var columns = new[] { "姓名", "编号" };

        // 两个列分别精确命中两个关键字时，关键字索引更靠前的得分更高
        Assert.Equal("姓名", RosterImportParser.FindBestColumn(columns, ["姓名", "编号"]));
        Assert.Equal("编号", RosterImportParser.FindBestColumn(columns, ["编号", "姓名"]));
        Assert.Null(RosterImportParser.FindBestColumn(columns, ["不存在"]));
        // 大小写不敏感
        Assert.Equal("Name", RosterImportParser.FindBestColumn(["Name"], ["name"]));
    }

    [Fact]
    public void ParseStudents_FiltersDoubleBlankRowsAndDetectsDuplicateNames()
    {
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["学号"] = "01", ["姓名"] = "张三", ["标签"] = "a,b；c  a" },
            new() { ["学号"] = "02", ["姓名"] = "张三" },
            new() { ["学号"] = " ", ["姓名"] = "" },
            new() { ["学号"] = "", ["姓名"] = "  " },
            new() { ["学号"] = "03", ["姓名"] = "李四" }
        };
        var mapping = new StudentRosterColumnMapping("学号", "姓名", null, null, "标签");

        var result = RosterImportParser.ParseStudents(rows, mapping);

        Assert.Equal(3, result.Items.Count);
        var duplicate = Assert.Single(result.DuplicatedNames);
        Assert.Equal("张三", duplicate);
        // 双空白行被 IsCandidate 剔除
        Assert.DoesNotContain(result.Items, student => string.IsNullOrWhiteSpace(student.Id) && string.IsNullOrWhiteSpace(student.Name));
        // 标签去重并归一为空格分隔
        Assert.Equal("a b c", result.Items[0].Tags);
        Assert.True(result.Items.All(student => student.Exists));
        // 解析阶段不分配 RecordId，由目录管理服务在落库时补齐
        Assert.All(result.Items, student => Assert.Equal(Guid.Empty, student.RecordId));
    }

    [Fact]
    public void ParseStudents_UnmappedColumnsYieldEmptyValues()
    {
        var rows = new List<Dictionary<string, string>> { new() { ["姓名"] = "王五" } };
        var mapping = new StudentRosterColumnMapping(null, "姓名", null, null, null);

        var result = RosterImportParser.ParseStudents(rows, mapping);

        var student = Assert.Single(result.Items);
        Assert.Equal(string.Empty, student.Id);
        Assert.Equal("王五", student.Name);
        Assert.Empty(result.DuplicatedNames);
    }

    [Fact]
    public void RenameDuplicatedStudents_NumbersFromSecondOccurrence()
    {
        var students = new List<Student>
        {
            new() { Name = "张三" },
            new() { Name = "张三" },
            new() { Name = "张三" },
            new() { Name = "李四" }
        };

        RosterImportParser.RenameDuplicatedStudents(students);

        Assert.Equal("张三", students[0].Name);
        Assert.Equal("张三 (2)", students[1].Name);
        Assert.Equal("张三 (3)", students[2].Name);
        Assert.Equal("李四", students[3].Name);
    }

    [Fact]
    public void ParsePrizes_AppliesNumericFallbacksAndFiltersBlankRows()
    {
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["名称"] = "书", ["数量"] = "abc", ["权重"] = "" },
            new() { ["名称"] = "笔", ["数量"] = "-5", ["权重"] = "2.5" },
            new() { ["名称"] = "书", ["数量"] = "3", ["权重"] = "1" },
            new() { ["名称"] = "", ["数量"] = "1", ["权重"] = "1" }
        };
        var mapping = new PrizeRosterColumnMapping(null, "名称", "权重", "数量", null);

        var result = RosterImportParser.ParsePrizes(rows, mapping);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(1, result.Items[0].Count);   // 非数字回退为 1
        Assert.Equal(1, result.Items[0].Weight);  // 空白回退为 1
        Assert.Equal(0, result.Items[1].Count);   // 负数钳到 0
        Assert.Equal(2.5, result.Items[1].Weight);
        var duplicate = Assert.Single(result.DuplicatedNames);
        Assert.Equal("书", duplicate);

        RosterImportParser.RenameDuplicatedPrizes(result.Items);
        Assert.Equal("书 (2)", result.Items[2].Name);
    }

    [Fact]
    public void SplitKeywords_TrimsAndDropsEmptyEntries()
    {
        Assert.Equal(["学号", "编号", "id"], RosterImportParser.SplitKeywords("学号| 编号 ||id"));
    }
}
