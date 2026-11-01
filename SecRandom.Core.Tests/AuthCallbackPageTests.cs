using System.Globalization;
using SecRandom.Langs.Auth;
using SecRandom.Services.Auth;

namespace SecRandom.Core.Tests;

/// <summary>
///     授权回调落地页：浏览器里那一页的身份、状态与安全边界。
/// </summary>
/// <remarks>
///     <para>
///         这一页由 <see cref="LoopbackAuthRedirectBroker" /> 在**校验 state、交换 token 之前**写给浏览器，
///         所以它的措辞只能说"回到应用了"，不能说"登录成功"；它也不是 Avalonia 窗口，读不到应用内主题，
///         因此深/浅色必须交给浏览器自己的 <c>prefers-color-scheme</c>。
///     </para>
///     <para>
///         端口虽然只在本机回环，但 <c>error</c> 查询参数仍然会进页面，所以要证明它只回显受控形状；
///         同时证明页面自带 CSP 依赖的内联样式与内联图标，不会发出第二个请求。真正跟监听器交互的用例
///         留在 <c>MobileAuthRedirectTests</c> 的 <see cref="LoopbackAuthRedirectBrokerTests" />，那里已有
///         一个现成的 broker 测试类，不再新建重复类型。
///     </para>
/// </remarks>
public sealed class AuthCallbackPageTests
{
    private static readonly string[] AllStates = ["success", "denied", "failed"];

    [Theory]
    [InlineData(AuthCallbackPageState.Success, "success")]
    [InlineData(AuthCallbackPageState.Denied, "denied")]
    [InlineData(AuthCallbackPageState.Failed, "failed")]
    public void 每一种状态都写出对应的状态标记(AuthCallbackPageState state, string token)
    {
        var html = Render(state);

        // 只认 <main> 上那一处：样式表里三态选择器都在，按整页计数必然不止一个。
        Assert.Contains($"<main data-state=\"{token}\">", html, StringComparison.Ordinal);
        foreach (var other in AllStates)
        {
            if (other == token) continue;
            Assert.DoesNotContain($"<main data-state=\"{other}\">", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void 页面跟随浏览器主题而不是应用主题()
    {
        var html = Render(AuthCallbackPageState.Success);

        // 声明两种配色 + 一条 prefers-color-scheme 媒体查询：这是页面唯一读到的"主题"。
        Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\">", html, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-color-scheme:dark)", html, StringComparison.Ordinal);
        Assert.Contains("--bg:#fff", html, StringComparison.Ordinal);
        Assert.Contains("--bg:#1a1b1f", html, StringComparison.Ordinal);
    }

    [Fact]
    public void 页面是自包含的且不发出第二个请求()
    {
        var html = Render(AuthCallbackPageState.Success);

        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"viewport\"", html, StringComparison.Ordinal);
        // 图标内联：回环监听随时可能已经被释放，页面不能依赖再取一次资源。
        Assert.Contains("src=\"data:image/png;base64,", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html, StringComparison.Ordinal);
        // 没有进场动画：页面被截图或停在后台标签页时也必须立刻是可见的。
        Assert.DoesNotContain("animation", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@keyframes", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 页面同时给出应用图标与版本号()
    {
        var html = Render(AuthCallbackPageState.Success, version: "v9.9.9");

        Assert.Contains("class=\"ring\"", html, StringComparison.Ordinal);
        Assert.Contains("SecRandom", html, StringComparison.Ordinal);
        Assert.Contains("v9.9.9", html, StringComparison.Ordinal);
    }

    [Fact]
    public void 文案说回到应用而不说登录成功()
    {
        // 应答发生在 state 校验与换 token 之前，说"登录成功"是页面验证不了的谎。
        var html = Render(AuthCallbackPageState.Success);

        Assert.Contains(Resources.C_AuthCallback_Title_Success, html, StringComparison.Ordinal);
        Assert.Contains(Resources.C_AuthCallback_Lede_Success, html, StringComparison.Ordinal);
        Assert.DoesNotContain("登录成功", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization successful", html, StringComparison.Ordinal);
        Assert.DoesNotContain("signed in", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 三种状态的页面互不相同()
    {
        var pages = new[] { AuthCallbackPageState.Success, AuthCallbackPageState.Denied, AuthCallbackPageState.Failed }
            .Select(state => Render(state))
            .ToArray();

        Assert.Equal(3, pages.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void 只有失败状态才回显错误码()
    {
        var success = Render(AuthCallbackPageState.Success, "access_denied");
        var denied = Render(AuthCallbackPageState.Denied, "access_denied");
        var failed = Render(AuthCallbackPageState.Failed, "temporarily_unavailable");

        Assert.DoesNotContain("class=\"code\"", success, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"code\"", denied, StringComparison.Ordinal);
        Assert.Contains("temporarily_unavailable", failed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("access denied")]
    [InlineData("access_denied\n<script>")]
    [InlineData("ACCESS_DENIED")]
    [InlineData("")]
    public void 形状不对的错误码一律不回显(string errorCode)
    {
        var html = Render(AuthCallbackPageState.Failed, errorCode);

        Assert.DoesNotContain("class=\"code\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(1)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void 超长错误码被截断规则挡住()
    {
        var html = Render(AuthCallbackPageState.Failed, new string('a', 65));

        Assert.DoesNotContain("class=\"code\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void 文案按HTML规则转义()
    {
        var html = LoopbackAuthCallbackPage.Render(AuthCallbackPageState.Success, null, "v3.1.0",
            new AuthCallbackPageText("<b>标题</b>", "a & b \" c"));

        Assert.DoesNotContain("<b>标题</b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;标题&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.Contains("a &amp; b &quot; c", html, StringComparison.Ordinal);
    }

    [Fact]
    public void 页面语言跟随界面语言()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ja-JP");
            Assert.Contains("<html lang=\"ja-JP\">", Render(AuthCallbackPageState.Success), StringComparison.Ordinal);

            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.Contains("<html lang=\"en-US\">", Render(AuthCallbackPageState.Success), StringComparison.Ordinal);

            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
            Assert.Contains("<html lang=\"zh-Hans\">", Render(AuthCallbackPageState.Success), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void 三语文案都已就位且互不相同()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            string[] cultures = ["zh-CN", "en-US", "ja-JP"];
            var titles = new List<string>();
            var ledes = new List<string>();

            foreach (var name in cultures)
            {
                CultureInfo.CurrentUICulture = new CultureInfo(name);

                // 资源缺键时 Designer 会回退成键名，这里就能抓到。
                Assert.DoesNotContain("C_AuthCallback_", Resources.C_AuthCallback_Title_Success, StringComparison.Ordinal);
                Assert.DoesNotContain("C_AuthCallback_", Resources.C_AuthCallback_Lede_Success, StringComparison.Ordinal);
                Assert.DoesNotContain("C_AuthCallback_", Resources.C_AuthCallback_Title_Denied, StringComparison.Ordinal);
                Assert.DoesNotContain("C_AuthCallback_", Resources.C_AuthCallback_Lede_Denied, StringComparison.Ordinal);
                Assert.DoesNotContain("C_AuthCallback_", Resources.C_AuthCallback_Title_Failed, StringComparison.Ordinal);
                Assert.DoesNotContain("C_AuthCallback_", Resources.C_AuthCallback_Lede_Failed, StringComparison.Ordinal);

                titles.Add(Resources.C_AuthCallback_Title_Success);
                ledes.Add(Resources.C_AuthCallback_Lede_Success);
            }

            Assert.Equal(cultures.Length, titles.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(cultures.Length, ledes.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static string Render(AuthCallbackPageState state, string? errorCode = null, string version = "v3.1.0") =>
        LoopbackAuthCallbackPage.Render(state, errorCode, version);
}
