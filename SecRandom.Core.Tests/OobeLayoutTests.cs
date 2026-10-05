using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SecRandom.Core.Tests;

/// <summary>
///     首次运行向导（OOBE）的版式守卫：轮播子项数必须等于 ViewModel 的 <c>StepCount</c>，
///     并且该页引用的每个资源键三语齐全。
/// </summary>
/// <remarks>
///     <para>
///         这两条都是"加一页时最容易漏、漏了还不报错"的地方，所以值得钉死：
///         少写一个 <c>Carousel</c> 子项（或忘了把 <c>StepCount</c> 加一）会让后面的页面整体错位，
///         表现为"完成页永远显示不出来、上一步回到别的页"——只有走到最后一步才会发现。
///     </para>
///     <para>
///         少写一句译文不会编译失败：资源管理器按语言回落到中文基资源，
///         用户看到的是"日文界面里夹着一行中文"。集控页与完成页的文案都是这样进来的。
///     </para>
/// </remarks>
public sealed class OobeLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    /// <summary>页面上引用资源的两条写法：XAML 的 <c>x:Static</c> 与 C# 的 <c>LR</c> 别名。</summary>
    private static readonly Regex ReferencedKeyPattern = new(
        @"(?:langs:Resources|LR)\.([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    /// <summary>ViewModel 里那一个决定"轮播有多长"的常量。</summary>
    private static readonly Regex StepCountPattern = new(
        @"StepCount\s*=>\s*(\d+)\s*;",
        RegexOptions.Compiled);

    [Fact]
    public void 轮播子项数与StepCount一致()
    {
        var carousel = XDocument.Load(OobeWindowPath).Root!
            .Descendants(Avalonia + "Carousel")
            .Single();
        var steps = carousel.Elements().Count();

        var match = StepCountPattern.Match(File.ReadAllText(OobeViewModelPath));
        Assert.True(match.Success, "FirstRunOobeViewModel 里没有找到 StepCount 常量");

        var stepCount = int.Parse(match.Groups[1].Value);
        Assert.True(
            steps == stepCount,
            $"Carousel 有 {steps} 个子项，而 StepCount 是 {stepCount}："
            + "加/删一页时必须同时改这两个地方（StepCount - 1 是进度分母与最后一步的序号）");
    }

    [Fact]
    public void 向导引用的资源键三语齐全()
    {
        var zh = Keys(Path.Combine(OobeLangsRoot, "Resources.resx"));
        var en = Keys(Path.Combine(OobeLangsRoot, "Resources.en-US.resx"));
        var ja = Keys(Path.Combine(OobeLangsRoot, "Resources.ja-JP.resx"));

        var failures = new List<string>();

        // 基资源与两个卫星资源必须一一对应：多了会留下一条永远用不到的文案，
        // 少了就是上面说的"静默回落成中文"。
        foreach (var key in zh.Except(en))
            failures.Add($"{key}: 缺少 en-US");
        foreach (var key in zh.Except(ja))
            failures.Add($"{key}: 缺少 ja-JP");
        foreach (var key in en.Except(zh))
            failures.Add($"{key}: en-US 有但基资源没有");
        foreach (var key in ja.Except(zh))
            failures.Add($"{key}: ja-JP 有但基资源没有");

        foreach (var key in ReferencedKeys())
        {
            if (!zh.Contains(key))
                failures.Add($"{key}: 向导引用了它，但基资源里没有");
            if (!en.Contains(key))
                failures.Add($"{key}: 向导引用了它，但 en-US 里没有");
            if (!ja.Contains(key))
                failures.Add($"{key}: 向导引用了它，但 ja-JP 里没有");
        }

        Assert.True(
            failures.Count == 0,
            $"OOBE 资源键问题（{failures.Count} 处）：{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>XAML、窗口代码与 ViewModel 三处引用到的资源键（编译期只保证"基资源里有"）。</summary>
    private static HashSet<string> ReferencedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in new[] { OobeWindowPath, OobeWindowPath + ".cs", OobeViewModelPath })
        {
            foreach (Match match in ReferencedKeyPattern.Matches(File.ReadAllText(path)))
                keys.Add(match.Groups[1].Value);
        }

        Assert.True(keys.Count > 20, $"只解析出 {keys.Count} 个资源键，引用写法可能已经变了");
        return keys;
    }

    private static HashSet<string> Keys(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .Select(element => element.Attribute("name")?.Value ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static string OobeWindowPath => Path.Combine(RepositoryRoot, "SecRandom", "Views", "FirstRunOobeWindow.axaml");

    private static string OobeViewModelPath =>
        Path.Combine(RepositoryRoot, "SecRandom", "ViewModels", "FirstRunOobeViewModel.cs");

    private static string OobeLangsRoot => Path.Combine(RepositoryRoot, "SecRandom", "Langs", "FirstRunOobe");

    private static string RepositoryRoot { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
}
