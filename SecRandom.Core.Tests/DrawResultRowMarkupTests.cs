namespace SecRandom.Core.Tests;

/// <summary>
///     结果行的版式约束：图片大小可调之后，文字那一列必须相对图片**垂直居中**。
/// </summary>
/// <remarks>
///     <para>
///         为什么值得一条断言：结果行是"横向 <c>StackPanel</c> 里放图片 + 文字列"，
///         横向 <c>StackPanel</c> 会把子项沿交叉轴**拉伸**到整行高，文字列因此从顶部排下来。
///         图片只有 72px 时看不出来（文字本来就比图片高），一旦把图片调大，
///         文字就贴到整行顶部——用户看到的就是"图片大了，名字没垂直居中"。
///         <c>VerticalAlignment="Center"</c> 是让文字列回到自己高度并居中的那一句。
///     </para>
///     <para>
///         ⚠️ 静默回退的代价：这一句被删掉不会编译失败、也不会报错，只会让结果区歪掉，
///         而且只在"图片比文字高"时才看得见。所以这里按真实 XAML 逐字钉住。
///     </para>
/// </remarks>
public sealed class DrawResultRowMarkupTests
{
    /// <summary>每个结果模板里"文字列"那一行的写法（横向 StackPanel 里图片与文字之间）。</summary>
    [Theory]
    [InlineData("SecRandom/Views/MainPages/RollCallResultPresenter.axaml",
        "<StackPanel Spacing=\"6\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\">", 2)]
    [InlineData("SecRandom/Views/MainPages/LotteryResultPresenter.axaml",
        "<StackPanel Spacing=\"6\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\">", 2)]
    [InlineData("SecRandom/Views/MainPages/QuickDrawPage.axaml",
        "<StackPanel Spacing=\"4\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\">", 1)]
    public void 结果行的文字列相对图片垂直居中(string relativePath, string expected, int occurrences)
    {
        var markup = File.ReadAllText(GetRepositoryPath(relativePath));
        var count = markup.Split(expected, StringSplitOptions.None).Length - 1;

        Assert.True(
            count == occurrences,
            $"{relativePath} 里「{expected}」出现 {count} 次（应为 {occurrences} 次）：" +
            "结果行的文字列少了 VerticalAlignment=\"Center\" 时，图片一旦比文字高，文字会贴到整行顶部");
    }

    /// <summary>
    ///     图片尺寸必须来自设置（`ImageSize`），不能在模板里写死——否则滑杆调了也没用。
    /// </summary>
    [Theory]
    [InlineData("SecRandom/Views/MainPages/RollCallResultPresenter.axaml")]
    [InlineData("SecRandom/Views/MainPages/LotteryResultPresenter.axaml")]
    [InlineData("SecRandom/Views/MainPages/QuickDrawPage.axaml")]
    public void 头像模板的尺寸与占位字号跟着设置走(string relativePath)
    {
        var markup = File.ReadAllText(GetRepositoryPath(relativePath));

        Assert.Contains("Width=\"{Binding ImageSize}\" Height=\"{Binding ImageSize}\"", markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"{Binding InitialFontSize}\"", markup, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
