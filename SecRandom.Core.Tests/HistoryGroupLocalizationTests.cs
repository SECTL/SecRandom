using System.Xml.Linq;

namespace SecRandom.Core.Tests;

/// <summary>
///     历史记录组功能新增键的**定向**三语守卫：中/英/日三个资源文件里都要有、都不能为空，
///     英文与日文都不许照抄中文，日文也不许照抄英文。
/// </summary>
/// <remarks>
///     <para>
///         为什么要单独一个文件：<see cref="LocalizationHygieneTests" /> 的
///         <c>移动端新增键三语齐全且不是照抄中文</c> 只扫 <c>Langs/Mobile/**</c>，
///         其余文件夹的新增键只被「句末标点」那一条规则覆盖——也就是说
///         「三语齐全 / 不是假翻译」这两个最容易翻车的地方，历史上**没有任何自动化把关**。
///         这里按文件夹白名单只查本功能自己的键，不去碰 <see cref="LocalizationHygieneTests" /> 的扫描根：
///         一旦把它扩到全仓库，会把一堆与本 PR 无关的历史遗留键整片炸红。
///     </para>
///     <para>
///         白名单是有意的：只有列在下表里的键受此约束。新增键时请**同步加进表里**，
///         否则就又回到「靠人眼看几十个文件」的老路上。
///     </para>
///     <para>
///         路径定位照抄 <see cref="LocalizationHygieneTests" />：由测试程序集输出目录
///         <c>bin/&lt;Configuration&gt;/net10.0</c> 上溯四级得到仓库根，再拼
///         <c>SecRandom/Langs</c>；不自己发明路径，避免两份实现漂移。
///     </para>
/// </remarks>
public sealed class HistoryGroupLocalizationTests
{
    /// <summary>
    ///     本功能新增的键，按所属页面资源文件夹分组（相对 <c>SecRandom/Langs</c>）。
    /// </summary>
    private static readonly (string[] Folder, string[] Keys)[] CoveredFolders =
    [
        (["SettingsPages", "HistoryManagement"],
        [
            "S_HistoryGroups",
            "S_HistoryGroups_Enabled",
            "S_HistoryGroups_Enabled_D",
            "S_HistoryGroups_Manage",
            "S_HistoryGroups_Manage_D",
            "S_HistoryGroups_Existing",
            "S_HistoryGroups_Add",
            "S_HistoryGroups_Rename",
            "S_HistoryGroups_Delete",
            "S_HistoryGroups_ConfirmDelete",
            "S_HistoryGroups_DeleteHint",
            "S_HistoryGroups_Save",
            "S_HistoryGroups_Cancel",
            "S_HistoryGroups_Empty",
            "S_HistoryGroups_RecordCount",
            "S_HistoryGroups_Error_NameEmpty",
            "S_HistoryGroups_Error_NameTooLong",
            "S_HistoryGroups_Error_NameControlChars",
            "S_HistoryGroups_Error_NameReserved",
            "S_HistoryGroups_Error_AlreadyExists",
            "S_HistoryGroups_Error_NotFound",
            "S_HistoryGroups_Error_NameUnchanged",
            "S_HistoryGroups_Error_ProfileUnavailable",
            "S_HistoryGroups_Error_SaveFailed"
        ]),
        (["SettingsPages", "Picking"],
        [
            // 三个抽取页的「默认记录组」行：点名/抽奖是选择器的初始值，闪抽是唯一的选择器。
            "S_RollCallHistoryGroup",
            "S_RollCallHistoryGroup_D",
            "S_LotteryHistoryGroup",
            "S_LotteryHistoryGroup_D",
            "S_QuickDrawHistoryGroup",
            "S_QuickDrawHistoryGroup_D",
            "O_HistoryGroupDefault",
            "S_HistoryGroup_Disabled"
        ]),
        (["SettingsPages", "More"],
        [
            "C_RollCallHistoryGroupSelector",
            "C_LotteryHistoryGroupSelector"
        ]),
        (["MainPages", "RollCall"],
        [
            "O_HistoryGroup",
            "O_HistoryGroupDefault"
        ]),
        (["MainPages", "Lottery"],
        [
            "O_HistoryGroup",
            "O_HistoryGroupDefault"
        ]),
        (["MainPages", "QuickDraw"],
        [
            // 闪抽没有自己的选择器，只在结果区下方显示一行「已锁定记录组」提示；
            // 配置的组被删掉/改名后再多一句「原记录组已不存在」。
            "M_HistoryGroupLocked",
            "M_HistoryGroupMissing"
        ])
    ];

    /// <summary>
    ///     历史记录设置页那批键。文件夹名以 <c>hist-ui</c> 的实际落点为准，所以运行期两个候选都试。
    /// </summary>
    private static readonly string[] PendingKeys =
    [
        "S_HistoryGroup",
        "S_HistoryGroup_Default",
        "S_HistoryGroup_All",
        "S_HistoryGroup_Move",
        "S_HistoryGroup_MoveHint",
        "S_HistoryGroup_MoveDone",
        "S_HistoryGroup_New",
        "S_Filter_HistoryGroup"
    ];

    private static readonly string[] PendingFolderAliases = ["History", "HistoryManagement"];

    /// <summary>
    ///     允许「译文与中文逐字相同」的键：只该放真正同形的汉字词，并在这里写清为什么。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>S_HistoryGroups_Save</c>：日文的「保存」与中文写法完全相同，是正常译文而不是没翻；
    ///         日文里的保存按钮本来就用这个汉字词（不是片假名音译）。这类同形词是极少数，
    ///         所以宁可显式列出，也不要为了它放宽整条规则——一放宽，「中文复制过去」就又查不出来了。
    ///     </para>
    /// </remarks>
    private static readonly string[] IdenticalTranslationAllowList = ["S_HistoryGroups_Save"];

    [Fact]
    public void 新增键在中英日三个资源文件里都存在且非空()
    {
        var failures = new List<string>();

        foreach (var (folder, keys) in CoveredFolders)
        {
            var resources = LoadFolder(folder);
            foreach (var key in keys)
            {
                foreach (var culture in Cultures)
                {
                    if (!resources.TryGetValue(culture, out var entries))
                    {
                        failures.Add($"{FolderLabel(folder)}: 缺少 Resources.{culture}.resx");
                        continue;
                    }

                    // 防「键漏了一半」：种中文基、忘 en-US/ja-JP（或反过来只改译文）都会在这里被抓住。
                    if (!entries.TryGetValue(key, out var value))
                        failures.Add($"{FolderLabel(folder)}: {culture} 缺少 {key}");
                    else if (string.IsNullOrWhiteSpace(value))
                        failures.Add($"{FolderLabel(folder)}: {key} 的 {culture} 是空的");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"历史记录组新增键的三语缺失（{failures.Count} 处）：{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void 新增键的英文与日文都没有照抄中文()
    {
        var failures = new List<string>();

        foreach (var (folder, keys) in CoveredFolders)
        {
            var resources = LoadFolder(folder);
            foreach (var key in keys)
            {
                if (!TryGetAllCultures(resources, key, out var values))
                    continue; // 缺失已由前一个用例报出，这里不重复刷屏

                var (chinese, english, japanese) = values;

                // 同形汉字词（少而确定）走白名单，其余任何逐字相同都按「没翻」处理。
                if (IdenticalTranslationAllowList.Contains(key, StringComparer.Ordinal))
                    continue;

                // 防「假翻译」：与中文逐字相同，多半是压根没翻，只是把中文复制过去交差。
                if (string.Equals(english, chinese, StringComparison.Ordinal))
                    failures.Add($"{FolderLabel(folder)}: {key} 的 en-US 与中文逐字相同（{chinese}）");
                if (string.Equals(japanese, chinese, StringComparison.Ordinal))
                    failures.Add($"{FolderLabel(folder)}: {key} 的 ja-JP 与中文逐字相同（{chinese}）");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"历史记录组新增键疑似未翻译（{failures.Count} 处）：{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void 新增键的日文与英文互不相同()
    {
        var failures = new List<string>();

        foreach (var (folder, keys) in CoveredFolders)
        {
            var resources = LoadFolder(folder);
            foreach (var key in keys)
            {
                if (!TryGetAllCultures(resources, key, out var values))
                    continue;

                var (_, english, japanese) = values;

                // 防「日文其实是英文」：中日两栏都填了同一串英文，看上去三语齐全，实际只有两语。
                if (string.Equals(english, japanese, StringComparison.Ordinal))
                    failures.Add($"{FolderLabel(folder)}: {key} 的 ja-JP 与 en-US 相同（{english}）");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"历史记录组新增键的日文疑似照抄英文（{failures.Count} 处）：{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void 历史记录设置页新增键三语齐全且互不相同()
    {
        var keys = PendingKeys;

        // 只有「该键真的落在某个候选文件夹里」才算已落盘。只看文件夹存在会把既有的
        // HistoryManagement 误当成落点：那个页面本来就有自己的资源文件，只是不含这批键。
        var landed = PendingFolderAliases
            .Select(alias => (Folder: new[] { "SettingsPages", alias }, Resources: LoadFolder(["SettingsPages", alias])))
            .Where(candidate => candidate.Resources.TryGetValue("resx", out var baseEntries)
                                && keys.Any(key => baseEntries.ContainsKey(key)))
            .ToList();

        // 软失败：设置页那批键由 hist-ui 并行落盘，尚未落地时不应该把本文件整片刷红。
        // 落盘（哪怕是部分落盘）后本用例自动开始把关，无需再改代码。
        if (landed.Count == 0)
        {
            Assert.Skip(
                "等待 hist-ui 落盘："
                + string.Join(" / ", PendingFolderAliases.Select(alias => "Langs/SettingsPages/" + alias))
                + " 下都还没有这批键（"
                + string.Join("、", keys)
                + "）。");
            return;
        }

        var failures = new List<string>();

        foreach (var (folder, resources) in landed)
        {
            foreach (var key in keys)
            {
                if (!TryGetAllCultures(resources, key, out var values))
                {
                    foreach (var culture in Cultures)
                    {
                        if (!resources.TryGetValue(culture, out var entries))
                            failures.Add($"{FolderLabel(folder)}: 缺少 Resources.{culture}.resx");
                        else if (!entries.ContainsKey(key))
                            failures.Add($"{FolderLabel(folder)}: {culture} 缺少 {key}");
                    }

                    continue;
                }

                var (chinese, english, japanese) = values;
                if (string.IsNullOrWhiteSpace(chinese))
                    failures.Add($"{FolderLabel(folder)}: {key} 的中文是空的");
                if (string.IsNullOrWhiteSpace(english))
                    failures.Add($"{FolderLabel(folder)}: {key} 的 en-US 是空的");
                if (string.IsNullOrWhiteSpace(japanese))
                    failures.Add($"{FolderLabel(folder)}: {key} 的 ja-JP 是空的");

                if (!IdenticalTranslationAllowList.Contains(key, StringComparer.Ordinal))
                {
                    if (string.Equals(english, chinese, StringComparison.Ordinal))
                        failures.Add($"{FolderLabel(folder)}: {key} 的 en-US 与中文逐字相同（{chinese}）");
                    if (string.Equals(japanese, chinese, StringComparison.Ordinal))
                        failures.Add($"{FolderLabel(folder)}: {key} 的 ja-JP 与中文逐字相同（{chinese}）");
                }

                if (string.Equals(english, japanese, StringComparison.Ordinal))
                    failures.Add($"{FolderLabel(folder)}: {key} 的 ja-JP 与 en-US 相同（{english}）");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"历史记录设置页新增键的三语问题（{failures.Count} 处）：{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    private static readonly string[] Cultures = ["resx", "en-US", "ja-JP"];

    private static Dictionary<string, Dictionary<string, string>> LoadFolder(string[] folder)
    {
        var directory = Path.Combine([LangsRoot, .. folder]);
        var resources = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var culture in Cultures)
        {
            var path = Path.Combine(directory, culture == "resx" ? "Resources.resx" : $"Resources.{culture}.resx");
            if (!File.Exists(path))
                continue;

            resources[culture] = Entries(path)
                .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        }

        return resources;
    }

    private static bool TryGetAllCultures(
        Dictionary<string, Dictionary<string, string>> resources,
        string key,
        out (string Chinese, string English, string Japanese) values)
    {
        values = default;

        if (!resources.TryGetValue("resx", out var chinese) || !chinese.TryGetValue(key, out var zh))
            return false;
        if (!resources.TryGetValue("en-US", out var english) || !english.TryGetValue(key, out var en))
            return false;
        if (!resources.TryGetValue("ja-JP", out var japanese) || !japanese.TryGetValue(key, out var ja))
            return false;

        values = (zh, en, ja);
        return true;
    }

    private static string FolderLabel(string[] folder) => "Langs/" + string.Join('/', folder);

    /// <summary>与 <see cref="LocalizationHygieneTests" /> 同源的资源读取方式（键 → 值，忽略空白项）。</summary>
    private static IEnumerable<(string Key, string Value)> Entries(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .Select(element => (
                Key: element.Attribute("name")?.Value ?? string.Empty,
                Value: element.Element("value")?.Value ?? string.Empty))
            .Where(entry => entry.Key.Length > 0);

    /// <summary>
    ///     与 <see cref="LocalizationHygieneTests" /> 一致的语言根目录：由输出目录上溯四级到仓库根。
    /// </summary>
    private static string LangsRoot => Path.Combine(RepositoryRoot, "SecRandom", "Langs");

    private static string RepositoryRoot { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
}
