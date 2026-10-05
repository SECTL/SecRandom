using System.Xml.Linq;

namespace SecRandom.Core.Tests;

/// <summary>
///     本地化文案规范：短标签/状态/按钮/标题（非 <c>_D</c> 键）**不带句末标点**，
///     只有 <c>_D</c> 说明句才以句末标点结尾；并且本工作流新增的键三语齐全、不是照抄中文。
/// </summary>
/// <remarks>
///     <para>
///         为什么要有这条守卫：用户实测看到的是「目标不存在。」这种带句号的**状态文案**——
///         句号把一句可扫读的状态变成了半句话，而这类问题只会随每次新增文案慢慢回流。
///         规则本身很简单，靠人眼在几十个文件里守是守不住的。
///     </para>
///     <para>
///         键名约定：<c>_D</c> 结尾 = description（一整句说明，允许句末标点），其余 = 标签/状态/按钮/标题。
///         省略号（<c>…</c> 或 <c>...</c>）不算句末标点：它是"进行中"的省略写法，不是句子结束。
///     </para>
/// </remarks>
public sealed class LocalizationHygieneTests
{
    /// <summary>句末标点：中日文用「。」，英文标签不带结尾 <c>.</c>。</summary>
    private static readonly char[] SentenceFinalPunctuation = ['。', '．', '.', '！', '!', '？', '?'];

    /// <summary>
    ///     本工作流新增的键前缀（手机端集控抽取页 + 设置页账号区）。
    /// </summary>
    private static readonly string[] AddedKeyPrefixes = ["RD_", "MA_"];

    private static readonly string[] AddedExtraKeys = ["N_RemoteDraw", "P_RemoteDraw"];

    /// <summary>
    ///     允许"译文与中文逐字相同"的显式白名单。
    /// </summary>
    /// <remarks>
    ///     只该出现真正同形的专有名词/品牌名；空着说明**当前没有任何键需要例外**，
    ///     这样"文案其实是中文抄过去的"就会被测试直接抓住，而不是被一句"某些词本来就一样"糊过去。
    /// </remarks>
    private static readonly string[] IdenticalTranslationAllowList = [];

    [Fact]
    public void 非说明键的文案不以句末标点结尾()
    {
        var failures = new List<string>();

        foreach (var file in ResxFiles())
        {
            var culture = CultureOf(file);
            var forbiddenEnders = ForbiddenEnders(culture);

            foreach (var (key, value) in Entries(file))
            {
                if (IsDescriptionKey(key) || IsEllipsis(value))
                    continue;

                var trimmed = value.TrimEnd();
                if (trimmed.Length > 0 && forbiddenEnders.Contains(trimmed[^1]))
                    failures.Add($"[{culture}] {RelativePath(file)} :: {key} = \"{value}\"");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"以下非 _D 键的文案以句末标点结尾（改前：{failures.Count} 处）：{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void 说明键仍然允许以句末标点结尾()
    {
        // 反向确认规则没有被误用成"所有文案都不许有句号"：_D 说明句本来就该以句末标点收尾。
        var described = ResxFiles()
            .SelectMany(file => Entries(file))
            .Count(entry => IsDescriptionKey(entry.Key)
                            && entry.Value.TrimEnd() is { Length: > 0 } text
                            && SentenceFinalPunctuation.Contains(text[^1]));

        Assert.True(described > 0, "没有任何 _D 说明句以句末标点结尾：规则可能被用错了");
    }

    [Fact]
    public void 移动端新增键三语齐全且不是照抄中文()
    {
        var zh = Entries(Path.Combine(LangsRoot, "Mobile", "Resources.resx")).ToDictionary();
        var en = Entries(Path.Combine(LangsRoot, "Mobile", "Resources.en-US.resx")).ToDictionary();
        var ja = Entries(Path.Combine(LangsRoot, "Mobile", "Resources.ja-JP.resx")).ToDictionary();
        var designer = File.ReadAllText(Path.Combine(LangsRoot, "Mobile", "Resources.Designer.cs"));

        var added = zh.Keys
            .Where(key => AddedKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))
                          || AddedExtraKeys.Contains(key, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(added.Length > 20, $"新增键只有 {added.Length} 个，键名前缀可能写错了");

        var failures = new List<string>();
        foreach (var key in added)
        {
            if (!en.ContainsKey(key))
                failures.Add($"{key}: 缺少 en-US");
            if (!ja.ContainsKey(key))
                failures.Add($"{key}: 缺少 ja-JP");
            if (!designer.Contains($"public static string {key} =>", StringComparison.Ordinal))
                failures.Add($"{key}: Resources.Designer.cs 里没有对应属性");

            if (en.TryGetValue(key, out var english) && string.IsNullOrWhiteSpace(english))
                failures.Add($"{key}: en-US 是空的");
            if (ja.TryGetValue(key, out var japanese) && string.IsNullOrWhiteSpace(japanese))
                failures.Add($"{key}: ja-JP 是空的");

            // 防"假翻译"：英文/日文与中文逐字相同，多半是没翻。
            if (IdenticalTranslationAllowList.Contains(key, StringComparer.Ordinal))
                continue;

            if (en.TryGetValue(key, out var identical) && identical == zh[key])
                failures.Add($"{key}: en-US 与中文逐字相同（{identical}）");
            if (ja.TryGetValue(key, out var same) && same == zh[key])
                failures.Add($"{key}: ja-JP 与中文逐字相同（{same}）");
        }

        Assert.True(
            failures.Count == 0,
            $"新增键的三语/生成器问题（{failures.Count} 处）：{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    private static bool IsDescriptionKey(string key) => key.EndsWith("_D", StringComparison.Ordinal);

    /// <summary>
    ///     每种语言各自的句末标点。
    /// </summary>
    /// <remarks>
    ///     规则按语言执行：中文/日文用「。」（外加西文感叹/问号），英文标签不带结尾 <c>.</c>。
    ///     注意只看**结尾**——日文句子中间的「。」是正常用法，不能一律禁止，否则会把正常译文整片判红。
    /// </remarks>
    private static char[] ForbiddenEnders(string culture) => culture switch
    {
        "en-US" => ['.', '!', '?'],
        _ => ['。', '．', '.', '！', '!', '？', '?']
    };

    private static bool IsEllipsis(string value)
    {
        var trimmed = value.TrimEnd();
        return trimmed.EndsWith("…", StringComparison.Ordinal) || trimmed.EndsWith("...", StringComparison.Ordinal);
    }

    /// <summary>语言标签：文件名带 <c>.en-US.</c>/<c>.ja-JP.</c>，其余文件的中文基资源视作 zh-CN。</summary>
    private static string CultureOf(string path) =>
        Path.GetFileName(path).Contains(".en-US.", StringComparison.OrdinalIgnoreCase) ? "en-US"
        : Path.GetFileName(path).Contains(".ja-JP.", StringComparison.OrdinalIgnoreCase) ? "ja-JP"
        : "zh-CN";

    private static IEnumerable<(string Key, string Value)> Entries(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .Select(element => (
                Key: element.Attribute("name")?.Value ?? string.Empty,
                Value: element.Element("value")?.Value ?? string.Empty))
            .Where(entry => entry.Key.Length > 0);

    private static IEnumerable<string> ResxFiles() =>
        Directory.EnumerateFiles(LangsRoot, "*.resx", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

    private static string RelativePath(string path) => Path.GetRelativePath(RepositoryRoot, path);

    private static string LangsRoot => Path.Combine(RepositoryRoot, "SecRandom", "Langs");

    private static string RepositoryRoot { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
}

/// <summary>把 <c>(Key, Value)</c> 序列转成字典，便于三语对照。</summary>
internal static class LocalizationEntryExtensions
{
    public static Dictionary<string, string> ToDictionary(this IEnumerable<(string Key, string Value)> entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
}
