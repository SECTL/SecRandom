using System.Globalization;
using SecRandom.Services.Auth;
using SecRandom.Shared;
using SecRandom.ViewModels.Mobile;
using MobileResources = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Core.Tests;

/// <summary>
///     线上故障的回归面：手机端重登丢失、失败被当成空数据、设置页没有登录入口。
/// </summary>
/// <remarks>
///     <para>
///         <b>根因</b>：移动端启动路径（<c>App.StartMobileHostAsync</c>）从来没有调用
///         <see cref="SectlAuthService.InitializeAsync" />，于是磁盘上的
///         <c>data/config/sectl-auth.json</c> 一直好好的，进程内会话却是空的——
///         重启后表现为"账号退出了"，依赖账号的功能（集控、云备份）全都变成"未登录"。
///         桌面端走的是 <c>StartRuntimeServicesAsync</c>，那一条**有**这一步，所以这是移动端特有的问题。
///     </para>
///     <para>
///         这里能钉住两件事：token 文件本身是能持久化/读回的（存取往返），
///         以及移动端启动路径确实把这一步接上了（源码级断言，防止再被删掉）。
///     </para>
/// </remarks>
public sealed class MobileAuthPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SecRandom", "mobile-auth-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task 令牌写盘后新进程能读回来()
    {
        var path = Path.Combine(_root, "config", "sectl-auth.json");
        var token = new SectlToken("access-1", "refresh-1", "user-1", 3600);

        // 写：模拟一次登录。
        await new SectlTokenStore(path).SaveAsync(token);

        // 新进程：**新实例**从同一个文件读，等于重启后的那一次 InitializeAsync。
        var reloaded = await new SectlTokenStore(path).LoadAsync();

        Assert.NotNull(reloaded);
        Assert.Equal("access-1", reloaded!.AccessToken);
        Assert.Equal("refresh-1", reloaded.RefreshToken);
        Assert.Equal("user-1", reloaded.UserId);
    }

    [Fact]
    public void 令牌路径在数据根下的config目录里而不是缓存目录()
    {
        // 手机端的数据根由 Utils.ConfigureMobileDataRoot() 指定（应用私有数据目录），
        // 令牌就落在它的 config/ 下；放到系统缓存目录会被系统清掉，那正是"重启就退出"的另一种成因。
        var path = Utils.GetFilePath("config", "sectl-auth.json").Replace('\\', '/');

        Assert.EndsWith("/config/sectl-auth.json", path, StringComparison.Ordinal);
        Assert.DoesNotContain("/cache/", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/temp/", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 移动端启动必须把磁盘上的登录态装回来()
    {
        var source = File.ReadAllText(GetRepositoryPath("SecRandom/App.axaml.cs"));

        var start = source.IndexOf("private async Task StartMobileHostAsync", StringComparison.Ordinal);
        Assert.True(start > 0, "找不到移动端启动方法");

        var next = source.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        var body = source[start..(next < 0 ? source.Length : next)];

        Assert.Contains("SectlAuthService", body, StringComparison.Ordinal);
        Assert.Contains("auth.InitializeAsync", body, StringComparison.Ordinal);

        // 必须在 Host 启动之前：之后再装，首屏的页面已经按"未登录"渲染过一次了。
        var authIndex = body.IndexOf("auth.InitializeAsync", StringComparison.Ordinal);
        var hostIndex = body.IndexOf("host.StartAsync", StringComparison.Ordinal);
        Assert.True(hostIndex > authIndex, "登录态必须在 Host 启动前从磁盘装回来");
    }

    // ---------------------------------------------------------------- 设置页账号区

    [Theory]
    [InlineData("黎泽懿", "u1", "黎泽懿")]
    [InlineData(null, "u1", "u1")]
    [InlineData("  ", "u1", "u1")]
    [InlineData(null, null, "")]
    public void 账号显示名回落而不是留空(string? name, string? userId, string expected)
    {
        var user = new SectlUser(userId, name, null, null);

        Assert.Equal(expected, MobileAccountSectionViewModel.ResolveDisplayName(user, userId));
    }

    [Fact]
    public void 账号显示名优先用资料里的昵称()
    {
        var user = new SectlUser("u1", "Aionflux", "a@example.com", null);

        Assert.Equal("Aionflux", MobileAccountSectionViewModel.ResolveDisplayName(user, "u1"));
        Assert.Equal("u1", MobileAccountSectionViewModel.ResolveAccountId(user, "u1"));
    }

    [Fact]
    public void 移动端设置页顶部有账号区并且实时跟随登录状态()
    {
        var markup = File.ReadAllText(
            GetRepositoryPath("SecRandom/Views/Mobile/Settings/MobileSettingsCatalogPage.axaml"));

        // 登录 / 退出 / 头像 / 昵称（缺昵称时用账号 ID 回落）
        Assert.Contains("Account.SignInCommand", markup, StringComparison.Ordinal);
        Assert.Contains("Account.SignOutCommand", markup, StringComparison.Ordinal);
        Assert.Contains("Account.Avatar", markup, StringComparison.Ordinal);
        Assert.Contains("Account.DisplayName", markup, StringComparison.Ordinal);
        Assert.Contains("MA_SignIn", markup, StringComparison.Ordinal);

        // 账号区必须在设置项列表**之前**（也就是页面顶部）：手机端没有标题栏账号入口。
        var accountIndex = markup.IndexOf("Account.SignInCommand", StringComparison.Ordinal);
        var listIndex = markup.IndexOf("ItemsSource=\"{Binding Items}\"", StringComparison.Ordinal);
        Assert.True(accountIndex > 0 && listIndex > accountIndex, "账号区必须排在设置项列表前面");

        // 实时跟随：订阅 StateChanged 而不是构造时读一次。
        var viewModel = File.ReadAllText(
            GetRepositoryPath("SecRandom/ViewModels/Mobile/MobileAccountSectionViewModel.cs"));
        Assert.Contains("StateChanged", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void 账号区在移动分支里注册了()
    {
        var app = File.ReadAllText(GetRepositoryPath("SecRandom/App.axaml.cs"));

        Assert.Contains("MobileAccountSectionViewModel", app, StringComparison.Ordinal);
    }

    [Fact]
    public void 未登录文案只留最短的未登录()
    {
        // 用户明确要求：未登录只显示"未登录"，不要后面那串"登录后可以……"。
        Assert.Equal("未登录", MobileResources.MA_SignedOutHint);
        Assert.Equal(
            "Not signed in",
            MobileResources.ResourceManager.GetString("MA_SignedOutHint", new CultureInfo("en-US")));
        Assert.Equal(
            "未ログイン",
            MobileResources.ResourceManager.GetString("MA_SignedOutHint", new CultureInfo("ja-JP")));

        // 短标签不带句末标点（三语一致）。
        foreach (var language in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            var text = MobileResources.ResourceManager.GetString("MA_SignedOutHint", new CultureInfo(language))!;
            Assert.False(
                text.EndsWith('。') || text.EndsWith('．') || text.EndsWith('.')
                || text.EndsWith('!') || text.EndsWith('?'),
                $"{language} 的未登录文案带了句末标点：{text}");
        }
    }

    [Fact]
    public void 账号头像占位在两种主题下都用主题色而不是写死的颜色()
    {
        var markup = File.ReadAllText(
            GetRepositoryPath("SecRandom/Views/Mobile/Settings/MobileSettingsCatalogPage.axaml"));

        // 三种状态共用一个圆：外圈只有一处背景/描边定义，且都是主题资源（浅色与深色各有取值）。
        Assert.Contains("Background=\"{DynamicResource ControlFillColorSecondaryBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{DynamicResource CardStrokeColorDefaultBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"True\"", markup, StringComparison.Ordinal);

        // 未登录不再是"一个不透明强调色的空圆"，而是中性底 + 人形轮廓：
        // 强调色在深色主题里是一个高饱和圆点，既不像占位，也和已登录首字圆的底色不是同一套 token。
        Assert.DoesNotContain("AccentFillColorDefaultBrush", markup, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"{sr:Fi PersonFilled}\"", markup, StringComparison.Ordinal);

        // 不写死颜色：浅/深主题由 DynamicResource 决定。
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", markup);
    }

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
