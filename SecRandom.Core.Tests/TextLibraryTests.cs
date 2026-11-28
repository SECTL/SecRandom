using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Services.Notification;
using System.Globalization;

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

    private string LibraryPath => Path.Combine(_directory, "library" + SecRandom.Shared.Utils.TextLibraryDatabaseExtension);
    private string LegacyPath => Path.Combine(_directory, "library.json");

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
        Assert.Equal("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(LibraryPath)[..16]));
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
    public void CorruptedDatabase_IsPreserved()
    {
        File.WriteAllText(LibraryPath, "corrupted");
        var service = NewService();
        Assert.NotNull(service.LoadError);
        Assert.Empty(service.Entries);
        Assert.Null(service.PickTemplate());
        Assert.Equal("corrupted", File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void LegacyJson_MigratesEntriesAndUsage()
    {
        const string json = """
                            {
                              "format_version": 1,
                              "texts": [
                                { "id": "legacy", "text": "旧版 {result}", "enabled": true, "use_count": 3 }
                              ],
                              "last_picked_id": "legacy"
                            }
                            """;
        File.WriteAllText(LegacyPath, json);

        var service = NewService();

        Assert.Single(service.Entries);
        Assert.Equal("旧版 {result}", service.Entries[0].Text);
        Assert.Equal(3, service.TotalUseCount);

        Assert.Equal(json, File.ReadAllText(LegacyPath + ".migrated"));
        Assert.Equal("旧版 {result}", NewService().Entries.Single().Text);
    }

    [Fact]
    public void InvalidLegacyJson_IsNeverOverwrittenOrReplacedWithDefaults()
    {
        File.WriteAllText(LegacyPath, "{\"format_version\":1}");
        Assert.NotNull(NewService().LoadError);
        Assert.True(File.Exists(LegacyPath));
        Assert.False(File.Exists(LibraryPath));
    }

    [Fact]
    public void NewerDatabaseVersion_RejectsWrites()
    {
        var service = NewService();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={LibraryPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=2";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => service.ResetCounts());
        Assert.Equal(30, service.Entries.Count);
    }

    [Fact]
    public void Libraries_HaveIndependentUsageAndAppendPreservesCounts()
    {
        var service = NewService();
        var original = service.DefaultLibraryId;
        service.ReplaceAll(["甲 {result}"]);
        service.PickTemplate(original);
        var second = service.CreateLibrary("Second", copyCurrent: true);
        Assert.Equal(0, service.TotalUseCount);
        service.Append(["乙 {result}", "甲 {result}"]);
        Assert.Equal(2, service.Entries.Count);
        service.PickTemplate(second);
        Assert.Equal(1, service.TotalUseCount);
        service.SelectLibrary(original);
        Assert.Equal(1, service.TotalUseCount);
        Assert.Single(service.Entries);
        Assert.False(service.DeleteLibrary(original));
        Assert.True(service.DeleteLibrary(second));
        Assert.Equal("甲 {result}", service.PickTemplate(second));
    }

    [Fact]
    public void EmptyLibrary_DoesNotConsumeDefaultLibrary()
    {
        var service = NewService();
        var empty = service.CreateLibrary("Empty");
        Assert.Null(service.PickTemplate(empty));
        Assert.Equal(0, service.GetEntries(service.DefaultLibraryId).Sum(entry => entry.UseCount));
        Assert.NotNull(service.PickTemplate(null));
        Assert.Equal(1, service.GetEntries(service.DefaultLibraryId).Sum(entry => entry.UseCount));
    }

    [Fact]
    public void ArchiveValidation_RejectsCorruptDatabaseAndKeepsIt()
    {
        File.WriteAllText(LibraryPath, "broken");
        Assert.ThrowsAny<Exception>(() => TextLibraryService.ValidateArchiveCandidate(_directory));
        Assert.Equal("broken", File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void RosterBinding_RoundTripsThroughAttachedObjects()
    {
        var roster = new SecRandom.Shared.Models.Profile.StudentList("class");
        TextLibraryBinding.Set(roster, "library-id");
        var json = System.Text.Json.JsonSerializer.Serialize(roster.AttachedObjects, SecRandom.Core.Abstraction.ConfigServiceBase.JsonOptions);
        roster.AttachedObjects = System.Text.Json.JsonSerializer.Deserialize<Dictionary<Guid, object?>>(json)!;
        Assert.Equal("library-id", TextLibraryBinding.Get(roster));
        roster.Name = "renamed";
        Assert.Equal("library-id", TextLibraryBinding.Get(roster));
        TextLibraryBinding.Set(roster, null);
        Assert.Null(TextLibraryBinding.Get(roster));
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

    // ---- AI 提示词 ----

    [Fact]
    public void Build_WritesEnglishInstructionsInEveryInterfaceLanguage()
    {
        foreach (var culture in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            string prompt = TextLibraryPrompt.Build(CultureInfo.GetCultureInfo(culture));

            Assert.All(prompt, c => Assert.True(c < 128, $"{culture} 的提示词正文出现了非 ASCII 字符：{c}"));
            Assert.DoesNotContain("__OUTPUT_LANGUAGE__", prompt, StringComparison.Ordinal);
            Assert.Contains("SecRandom", prompt, StringComparison.Ordinal);
            Assert.Contains("purpose, style and quantity", prompt, StringComparison.Ordinal);
            Assert.Contains("500", prompt, StringComparison.Ordinal);
            Assert.Contains("200", prompt, StringComparison.Ordinal);
            Assert.Contains("14121", prompt, StringComparison.Ordinal);
            Assert.Contains("{result}", prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Build_SwitchesOnlyTheOutputLanguage()
    {
        string english = TextLibraryPrompt.Build(CultureInfo.GetCultureInfo("en-US"));
        string chinese = TextLibraryPrompt.Build(CultureInfo.GetCultureInfo("zh-CN"));
        string japanese = TextLibraryPrompt.Build(CultureInfo.GetCultureInfo("ja-JP"));

        Assert.Contains("Reply to me in English.", english, StringComparison.Ordinal);
        Assert.Contains("Reply to me in Simplified Chinese.", chinese, StringComparison.Ordinal);
        Assert.Contains("Reply to me in Japanese.", japanese, StringComparison.Ordinal);
        Assert.Equal(english.Replace("English", "Simplified Chinese"), chinese);
        Assert.Equal(english.Replace("English", "Japanese"), japanese);
    }

    [Fact]
    public void Build_AcceptsNullCultureAsEnglish()
    {
        Assert.Equal(
            TextLibraryPrompt.Build(CultureInfo.GetCultureInfo("en-US")),
            TextLibraryPrompt.Build(null));
    }

    [Fact]
    public void Build_TemplateIsPasteableJson()
    {
        string prompt = TextLibraryPrompt.Build(CultureInfo.GetCultureInfo("en-US"));
        int start = prompt.IndexOf('[', StringComparison.Ordinal);
        int end = prompt.IndexOf(']', start) + 1;
        Assert.True(start >= 0 && end > start);

        var result = TextLibraryImporter.ParseJson(prompt[start..end].Trim());

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(2, result.Templates.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ParseJson_AcceptsMarkdownFence()
    {
        var result = TextLibraryImporter.ParseJson("```json\n[\"你好 {result}\"]\n```");
        Assert.True(result.Success);
        Assert.Single(result.Templates);
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
