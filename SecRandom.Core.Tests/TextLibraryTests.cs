using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Services.Notification;

namespace SecRandom.Core.Tests;

/// <summary>
///     文案库核心层测试：默认文案内容契约、平均线轮换、服务持久化/只读/损坏重建，以及三种导入来源的校验。
///     全部用例只操作临时目录，绝不触碰真实 data 目录。
/// </summary>
public sealed class TextLibraryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "sr-textlib-" + Guid.NewGuid().ToString("N"));

    public TextLibraryTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 测试收尾清理失败不影响结论
        }
    }

    private string LibraryPath => Path.Combine(_directory, "library.json");

    private TextLibraryService NewService(string? path = null)
        => new(NullLogger<TextLibraryService>.Instance, path ?? LibraryPath);

    // ---- 默认文案内容 ----

    [Fact]
    public void DefaultLibrary_HasExactlyThirtyTemplates()
    {
        Assert.Equal(30, DefaultTextLibrary.Templates.Count);
    }

    [Fact]
    public void DefaultLibrary_TemplatesAreUniqueAndWellFormed()
    {
        var templates = DefaultTextLibrary.Templates;
        Assert.Equal(templates.Count, templates.Distinct(StringComparer.Ordinal).Count());

        foreach (var template in templates)
        {
            Assert.True(FullscreenTextTemplate.HasSinglePlaceholder(template),
                $"模板必须恰好含一个占位符：{template}");
            Assert.InRange(template.Length, 6, 40);
            Assert.DoesNotContain("[result]", template, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DefaultLibrary_SeedsThirtyEntriesOnFirstRun()
    {
        var service = NewService();

        Assert.Equal(30, service.Entries.Count);
        Assert.Equal(0, service.TotalUseCount);
        Assert.Equal(30, service.EligibleCount);
        Assert.True(File.Exists(LibraryPath));
        Assert.Contains("\"format_version\": 1", File.ReadAllText(LibraryPath), StringComparison.Ordinal);
    }

    // ---- 平均线轮换算法 ----

    [Fact]
    public void AverageUseCount_OnlyCountsEnabledEntries()
    {
        var entries = new[]
        {
            Entry("a", useCount: 2),
            Entry("b", useCount: 4),
            Entry("c", useCount: 100, enabled: false)
        };

        Assert.Equal(3d, TextLibraryRotation.AverageUseCount(entries));
    }

    [Fact]
    public void AverageUseCount_ReturnsZeroWhenNothingEnabled()
    {
        var entries = new[] { Entry("a", useCount: 7, enabled: false) };

        Assert.Equal(0d, TextLibraryRotation.AverageUseCount(entries));
        Assert.Empty(TextLibraryRotation.GetEligible(entries, 0));
    }

    [Fact]
    public void GetEligible_KeepsOnlyEnabledEntriesAtOrBelowAverage()
    {
        var low = Entry("low", useCount: 0);
        var middle = Entry("middle", useCount: 2);
        var high = Entry("high", useCount: 9);
        var disabled = Entry("disabled", useCount: 0, enabled: false);
        var entries = new[] { low, middle, high, disabled };

        var average = TextLibraryRotation.AverageUseCount(entries); // (0+2+9)/3 = 3.67
        var eligible = TextLibraryRotation.GetEligible(entries, average);

        Assert.Equal(2, eligible.Count);
        Assert.Contains(low, eligible);
        Assert.Contains(middle, eligible);
        Assert.DoesNotContain(high, eligible);
        Assert.DoesNotContain(disabled, eligible);
    }

    [Fact]
    public void Pick_ExcludesLastPickedEntryWhenOtherCandidatesExist()
    {
        var first = Entry("first");
        var second = Entry("second");
        var third = Entry("third");

        var picked = TextLibraryRotation.Pick([first, second, third], "second", _ => 0);

        Assert.NotNull(picked);
        Assert.Equal("first", picked!.Id);
    }

    [Fact]
    public void Pick_KeepsLastPickedEntryWhenItIsTheOnlyCandidate()
    {
        var only = Entry("only");

        var picked = TextLibraryRotation.Pick([only], "only", _ => 0);

        Assert.Same(only, picked);
    }

    [Fact]
    public void Pick_ReturnsNullForEmptyCandidates()
    {
        Assert.Null(TextLibraryRotation.Pick([], null, _ => 0));
    }

    [Fact]
    public void Pick_UsesInjectedIndexGenerator()
    {
        var candidates = new[] { Entry("a"), Entry("b"), Entry("c") };

        var picked = TextLibraryRotation.Pick(candidates, null, max => max - 1);

        Assert.Equal("c", picked!.Id);
    }

    // ---- 服务：计数与持久化 ----

    [Fact]
    public void PickTemplate_IncrementsCountAndPersistsImmediately()
    {
        var service = NewService();

        var text = service.PickTemplate();

        Assert.NotNull(text);
        Assert.Equal(1, service.TotalUseCount);
        var picked = service.Entries.Single(entry => entry.Text == text);
        Assert.Equal(1, picked.UseCount);
        Assert.NotNull(picked.LastUsedUtc);

        var reloaded = NewService();
        Assert.Equal(1, reloaded.TotalUseCount);
        Assert.Contains(reloaded.Entries, entry => entry.Text == text && entry.UseCount == 1);
    }

    [Fact]
    public void PickTemplate_DoesNotRepeatThePreviousTemplate()
    {
        var service = NewService();

        var first = service.PickTemplate();
        Assert.NotNull(first);

        // 选中的条目计数 +1 后必然高于平均线，下一轮候选自然不含它：
        // "连续两次不同"是这条规则的确定性结果，不靠随机运气。
        var firstEntry = service.Entries.Single(entry => entry.Text == first);
        Assert.True(firstEntry.UseCount > service.AverageUseCount);

        var second = service.PickTemplate();
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void PickTemplate_SkipsDisabledEntries()
    {
        var service = NewService();
        var disabled = service.Entries[0];
        service.SetEnabled(disabled.Id, false);

        Assert.Equal(29, service.EligibleCount);
        for (var i = 0; i < 60; i++)
            Assert.NotEqual(disabled.Text, service.PickTemplate());

        Assert.Equal(0, service.Entries.Single(entry => entry.Id == disabled.Id).UseCount);
    }

    [Fact]
    public void RestoreDefaults_ResetsCountsAndEntries()
    {
        var service = NewService();
        service.ReplaceAll(["一 {result}", "二 {result}"]);
        service.PickTemplate();
        Assert.Equal(2, service.Entries.Count);

        service.RestoreDefaults();

        Assert.Equal(30, service.Entries.Count);
        Assert.Equal(0, service.TotalUseCount);
        Assert.All(service.Entries, entry => Assert.Null(entry.LastUsedUtc));
    }

    [Fact]
    public void ResetCounts_OnlyClearsUsageData()
    {
        var service = NewService();
        var disabled = service.Entries[1];
        service.SetEnabled(disabled.Id, false);
        service.PickTemplate();
        service.PickTemplate();

        service.ResetCounts();

        Assert.Equal(30, service.Entries.Count);
        Assert.Equal(0, service.TotalUseCount);
        Assert.All(service.Entries, entry => Assert.Null(entry.LastUsedUtc));
        Assert.False(service.Entries.Single(entry => entry.Id == disabled.Id).Enabled);
    }

    [Fact]
    public void ReplaceAll_ReplacesLibraryAndClearsCounts()
    {
        var service = NewService();
        service.PickTemplate();

        service.ReplaceAll(["甲 {result}", "乙 {result}", "丙 {result}"]);

        Assert.Equal(3, service.Entries.Count);
        Assert.Equal(0, service.TotalUseCount);

        var reloaded = NewService();
        Assert.Equal(3, reloaded.Entries.Count);
        Assert.Equal(new[] { "甲 {result}", "乙 {result}", "丙 {result}" }, reloaded.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void ReplaceAll_IsIgnoredWhenEveryTemplateIsInvalid()
    {
        var service = NewService();
        var original = service.Entries.Select(entry => entry.Text).ToArray();

        service.ReplaceAll(["", "   ", "{result} {result}"]);

        Assert.Equal(original, service.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void Add_RejectsInvalidAndDuplicateTemplates()
    {
        var service = NewService();
        var existing = service.Entries[0].Text;

        Assert.Null(service.Add(""));
        Assert.Null(service.Add("{result} 和 {result}"));
        Assert.Null(service.Add(existing));
        Assert.Null(service.Add(new string('长', TextLibraryImporter.MaxTextLength) + "{result}"));
        Assert.Equal(30, service.Entries.Count);
    }

    [Fact]
    public void Add_AppendsUsableTemplateAndAppendsMissingPlaceholder()
    {
        var service = NewService();

        var entry = service.Add("自定义文案");

        Assert.NotNull(entry);
        Assert.Equal("自定义文案 {result}", entry!.Text);
        Assert.Equal(31, service.Entries.Count);
        Assert.Equal(31, NewService().Entries.Count);
    }

    [Fact]
    public void Update_KeepsIdAndUseCount()
    {
        var service = NewService();
        var added = service.Add("原始 {result}");
        Assert.NotNull(added);
        service.SetEnabled(added!.Id, false);
        var id = added.Id;

        Assert.True(service.Update(id, "改过 {result}"));

        var updated = service.Entries.Single(entry => entry.Id == id);
        Assert.Equal("改过 {result}", updated.Text);
        Assert.False(updated.Enabled);
        Assert.Equal(0, updated.UseCount);
        Assert.False(service.Update(id, "{result} {result}"));
        Assert.False(service.Update("不存在的 id", "文案 {result}"));
    }

    [Fact]
    public void Remove_DropsEntryAndClearsLastPickedId()
    {
        var service = NewService();
        var removed = service.Entries[0];
        service.SetEnabled(removed.Id, true);

        Assert.True(service.Remove(removed.Id));
        Assert.Equal(29, service.Entries.Count);
        Assert.False(service.Remove(removed.Id));
        Assert.Equal(29, NewService().Entries.Count);
    }

    [Fact]
    public void SetEnabled_TogglesEntryAndRaisesChanged()
    {
        var service = NewService();
        var id = service.Entries[0].Id;
        var changes = 0;
        service.Changed += (_, _) => changes++;

        service.SetEnabled(id, false);

        Assert.Equal(1, changes);
        Assert.False(service.Entries.Single(entry => entry.Id == id).Enabled);
        Assert.False(NewService().Entries.Single(entry => entry.Id == id).Enabled);
    }

    // ---- 服务：损坏与版本 ----

    [Fact]
    public void CorruptedFile_RebuildsDefaultsAndSaves()
    {
        File.WriteAllText(LibraryPath, "{ 这不是 JSON ");

        var service = NewService();

        Assert.Equal(30, service.Entries.Count);
        var rewritten = File.ReadAllText(LibraryPath);
        Assert.Contains("\"texts\"", rewritten, StringComparison.Ordinal);
        Assert.Equal(30, NewService().Entries.Count);
    }

    [Fact]
    public void HigherFormatVersion_IsReadOnlyAndNeverRewritten()
    {
        const string json = """
                            {
                              "format_version": 2,
                              "texts": [
                                { "id": "legacy", "text": "旧版 {result}", "enabled": true, "use_count": 3 }
                              ],
                              "last_picked_id": "legacy"
                            }
                            """;
        File.WriteAllText(LibraryPath, json);

        var service = NewService();

        Assert.Single(service.Entries);
        Assert.Equal("旧版 {result}", service.Entries[0].Text);
        Assert.Equal(3, service.TotalUseCount);

        service.Save();
        service.ResetCounts();
        service.RestoreDefaults();

        Assert.Equal(json, File.ReadAllText(LibraryPath));
        Assert.Equal("旧版 {result}", NewService().Entries.Single().Text);
    }

    [Fact]
    public void UsesAtomicTemporaryFileAndSnakeCaseProperties()
    {
        var service = NewService();
        service.PickTemplate();

        var json = File.ReadAllText(LibraryPath);
        Assert.Contains("\"use_count\"", json, StringComparison.Ordinal);
        Assert.Contains("\"last_picked_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"last_used_utc\"", json, StringComparison.Ordinal);
        // 写入走"临时文件 + 原子替换"，正常结束后不应留下 .tmp
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void EntriesAreImmutableSnapshots()
    {
        var service = NewService();
        var snapshot = service.Entries;

        service.Remove(service.Entries[0].Id);

        Assert.Equal(30, snapshot.Count);
        Assert.Equal(29, service.Entries.Count);
    }

    // ---- 归一化 ----

    [Theory]
    [InlineData("  恭喜   {result}   登场  ", "恭喜 {result} 登场", false)]
    [InlineData("没有占位符", "没有占位符 {result}", true)]
    [InlineData("制表\t符 {result}", "制表 符 {result}", false)]
    public void NormalizeTemplate_CollapsesWhitespaceAndAppendsPlaceholder(
        string raw, string expected, bool appended)
    {
        var normalized = TextLibraryImporter.NormalizeTemplate(raw, out var didAppend);

        Assert.Equal(expected, normalized);
        Assert.Equal(appended, didAppend);
    }

    [Fact]
    public void NormalizeTemplate_ReturnsEmptyForBlankInput()
    {
        Assert.Equal(string.Empty, TextLibraryImporter.NormalizeTemplate(null, out var appended));
        Assert.False(appended);
        Assert.Equal(string.Empty, TextLibraryImporter.NormalizeTemplate("   ", out appended));
        Assert.False(appended);
    }

    // ---- JSON 导入 ----

    [Fact]
    public void ParseJson_AcceptsStringArray()
    {
        var result = TextLibraryImporter.ParseJson("""["甲 {result}", "乙 {result}"]""");

        Assert.True(result.Success);
        Assert.Equal(new[] { "甲 {result}", "乙 {result}" }, result.Templates);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ParseJson_AcceptsObjectShapeWithMixedEntries()
    {
        const string json = """
                            {
                              "format_version": 1,
                              "texts": [
                                { "text": "对象条目 {result}" },
                                "字符串条目 {result}"
                              ]
                            }
                            """;

        var result = TextLibraryImporter.ParseJson(json);

        Assert.True(result.Success);
        Assert.Equal(new[] { "对象条目 {result}", "字符串条目 {result}" }, result.Templates);
    }

    [Fact]
    public void ParseJson_RejectsContentOverCharacterLimit()
    {
        var result = TextLibraryImporter.ParseJson(new string('x', TextLibraryImporter.MaxJsonImportChars + 1));

        Assert.False(result.Success);
        Assert.Contains("14121", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_RejectsMoreThanFiveHundredEntries()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("\"条目 {result}\"", 501)) + "]";

        var result = TextLibraryImporter.ParseJson(json);

        Assert.False(result.Success);
        Assert.Contains("500", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_ReportsPositionForInvalidJson()
    {
        // 缺逗号：解析器抛 JsonException，错误文案里带 1 起的行列号
        var result = TextLibraryImporter.ParseJson("[\"甲 {result}\" \"乙 {result}\"]");

        Assert.False(result.Success);
        Assert.Contains("行", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_RejectsMultiplePlaceholders()
    {
        var result = TextLibraryImporter.ParseJson("""["{result} 与 {result}"]""");

        Assert.False(result.Success);
        Assert.Contains("只能出现一次", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_WarnsAndAppendsMissingPlaceholder()
    {
        var result = TextLibraryImporter.ParseJson("""["缺少占位符"]""");

        Assert.True(result.Success);
        Assert.Equal("缺少占位符 {result}", Assert.Single(result.Templates));
        Assert.Contains("已自动补到末尾", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_RejectsOverLongTemplate()
    {
        var template = new string('长', TextLibraryImporter.MaxTextLength + 1) + "{result}";

        var result = TextLibraryImporter.ParseJson($"[\"{template}\"]");

        Assert.False(result.Success);
        Assert.Contains("200", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_DeduplicatesNormalizedTemplates()
    {
        var result = TextLibraryImporter.ParseJson("""["甲 {result}", "  甲   {result}  "]""");

        Assert.True(result.Success);
        Assert.Equal("甲 {result}", Assert.Single(result.Templates));
        Assert.Contains("重复", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_KeepsValidRowsAndReportsInvalidOnes()
    {
        var result = TextLibraryImporter.ParseJson("""["合法 {result}", "{result} 和 {result}", ""]""");

        Assert.True(result.Success);
        Assert.Equal("合法 {result}", Assert.Single(result.Templates));
        Assert.Equal(2, result.Errors.Count);
        Assert.StartsWith("第 2 条", result.Errors[0], StringComparison.Ordinal);
        Assert.StartsWith("第 3 条", result.Errors[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_FailsWhenNothingIsUsable()
    {
        var result = TextLibraryImporter.ParseJson("[]");

        Assert.False(result.Success);
        Assert.Empty(result.Templates);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void ParseJson_FailsOnBlankContent()
    {
        Assert.False(TextLibraryImporter.ParseJson(null).Success);
        Assert.False(TextLibraryImporter.ParseJson("   ").Success);
    }

    // ---- 清单 / 表格导入 ----

    [Fact]
    public void FromCells_SkipsBlankCells()
    {
        var result = TextLibraryImporter.FromCells(["甲 {result}", null, "   ", "乙 {result}"]);

        Assert.True(result.Success);
        Assert.Equal(new[] { "甲 {result}", "乙 {result}" }, result.Templates);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FromTemplates_ReportsBlankLinesAsErrors()
    {
        var result = TextLibraryImporter.FromTemplates(["甲 {result}", "   "]);

        Assert.True(result.Success);
        Assert.Equal("甲 {result}", Assert.Single(result.Templates));
        Assert.Contains("文案为空", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    private static TextLibraryEntry Entry(string id, int useCount = 0, bool enabled = true)
        => new() { Id = id, Text = $"{id} {{result}}", Enabled = enabled, UseCount = useCount };
}
