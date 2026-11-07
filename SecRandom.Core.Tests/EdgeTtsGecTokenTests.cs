using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Edge_tts_sharp;

namespace SecRandom.Core.Tests;

/// <summary>
///     Edge 合成接口的 <c>Sec-MS-GEC</c> 令牌：形状、参考向量与合成地址。
/// </summary>
/// <remarks>
///     <para>
///         这些向量由上游 <c>rany2/edge-tts</c> 的 <c>drm.py</c> 直接算出，是"令牌是否仍然被服务端接受"
///         的离线判据。令牌的摘要部分是上游**改过**的 SHA-256（轮常量、首块状态、schedule 种子都不同），
///         所以任何"顺手换成 <see cref="SHA256" />"的重构都会让这些用例失败——那正是要拦住的回归。
///     </para>
///     <para>
///         令牌算法住在 <c>vendors/EdgeTtsSharp</c> 子模块（SECTL 自己的仓库）里，App 只消费
///         <see cref="EdgeTts.CreateSynthesisUri" />；这份测试是 App 侧对子模块行为的回归闸门，
///         子模块升级后若令牌形状、配对版本或参考向量变了，这里会先失败。
///     </para>
/// </remarks>
public sealed class EdgeTtsGecTokenTests
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    [Theory]
    [InlineData(0, "52BC7A17B50DFEC9-28C4394775C8751CD2F6859C030C06DB1489ACAB3B3A7651831FC26895177ED6")]
    [InlineData(1_000_000_000,
        "5E9153719FADA922-14D2A75113EC5862E17303287DCDB197E3A671DF9FCC53C100D6B27928384B98")]
    [InlineData(1_234_567_890,
        "BA405D1577B8E0CE-61F01E88539977A1A1E969572D82EECFA45FB1E5DEBD122D6ACBAF0054F959E5")]
    [InlineData(1_793_500_000,
        "E24FC7300983DF16-F024DC59F0D22A361B69B861A1BD1BEB35D2DC93BDC4A8314FE5FB4EBBB3F8CB")]
    [InlineData(1_793_500_400,
        "A7745486F4B5CD07-B6D4B97C0461DAD4FFE47360CE77B4C42938943CE08DC8F017EF744F2BFE4B29")]
    [InlineData(1_793_500_500,
        "08D45558CC9E0986-43E39ECC5A6744DAE03167A84536E5741BFC13EFBAAADB4DA1B0E783F82DB2FA")]
    [InlineData(2_548_986_000,
        "02C5335086FE74B6-A3191DD713A978B84957D2F5B3E6D29E957450C5A0EE75EFE175DBB4CC77F7F1")]
    public void 参考时刻生成与上游一致的令牌(long unixSeconds, string expected)
    {
        var token = EdgeTtsGecToken.Create(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, token);
    }

    [Fact]
    public void 令牌是十六位密钥段加六十四位摘要段()
    {
        var token = EdgeTtsGecToken.Create(DateTimeOffset.UtcNow);

        var parts = token.Split('-');
        Assert.Equal(2, parts.Length);
        Assert.Equal(16, parts[0].Length);
        Assert.Equal(64, parts[1].Length);
        Assert.All(token, character => Assert.True(char.IsAsciiHexDigit(character) || character == '-'));
        Assert.Equal(token.ToUpperInvariant(), token);
    }

    [Fact]
    public void 同一五分钟窗口内令牌保持不变()
    {
        // 1_793_500_200 起算的窗口覆盖到 1_793_500_499，整段共用同一个令牌。
        var windowStart = DateTimeOffset.FromUnixTimeSeconds(1_793_500_200);
        var expected = EdgeTtsGecToken.Create(windowStart);

        Assert.Equal(expected, EdgeTtsGecToken.Create(windowStart.AddSeconds(299)));
        Assert.NotEqual(expected, EdgeTtsGecToken.Create(windowStart.AddSeconds(300)));
        Assert.NotEqual(expected, EdgeTtsGecToken.Create(windowStart.AddSeconds(-1)));
    }

    [Fact]
    public void 摘要不是标准SHA256()
    {
        // 上游故意改过压缩函数；如果这里相等，说明有人把实现换成了 SHA256.HashData，服务端会 403。
        var moment = DateTimeOffset.FromUnixTimeSeconds(1_793_500_000);
        var ticks = moment.UtcDateTime.ToFileTimeUtc();
        ticks -= ticks % 3_000_000_000;
        var payload = Encoding.ASCII.GetBytes(
            ticks.ToString(CultureInfo.InvariantCulture) + TrustedClientToken);
        var standard = Convert.ToHexString(SHA256.HashData(payload));

        var token = EdgeTtsGecToken.Create(moment);

        Assert.NotEqual(standard, token.Split('-')[1]);
    }

    [Fact]
    public void 版本号与v2令牌配对()
    {
        // "2-" 前缀不是装饰：配 1-、或只给 v1 令牌，握手都会被 403 拒绝。
        Assert.Equal("2-143.0.3650.75", EdgeTtsGecToken.SecMsGecVersion);
        Assert.Equal($"2-{EdgeTtsGecToken.ChromiumVersion}", EdgeTtsGecToken.SecMsGecVersion);
    }

    [Fact]
    public void 合成地址携带当前窗口的v2令牌与版本号()
    {
        var parameters = ParseQuery(new Uri(EdgeTts.CreateSynthesisUri()).Query);

        Assert.Equal(TrustedClientToken, parameters["TrustedClientToken"]);
        Assert.Equal(EdgeTtsGecToken.SecMsGecVersion, parameters["Sec-MS-GEC-Version"]);
        Assert.Matches("^[0-9A-F]{16}-[0-9A-F]{64}$", parameters["Sec-MS-GEC"]);
        Assert.Equal(EdgeTtsGecToken.Create(DateTimeOffset.UtcNow), parameters["Sec-MS-GEC"]);
    }

    [Fact]
    public void 连接接缝只追加连接标识而不改令牌()
    {
        var synthesisUri = new Uri(EdgeTts.CreateSynthesisUri());
        var expected = ParseQuery(synthesisUri.Query);

        var wss = new Wss(synthesisUri.ToString());
        try
        {
            var actual = ParseQuery(RequestUri(wss).Query);

            Assert.Equal(expected["TrustedClientToken"], actual["TrustedClientToken"]);
            Assert.Equal(expected["Sec-MS-GEC"], actual["Sec-MS-GEC"]);
            Assert.Equal(expected["Sec-MS-GEC-Version"], actual["Sec-MS-GEC-Version"]);
            Assert.Matches("^[0-9a-f]{32}$", actual["ConnectionId"]);
        }
        finally
        {
            wss.Dispose();
        }
    }

    // Wss 把连接地址留在私有字段里，且构造过程不触网，这里只读出来核对。
    private static Uri RequestUri(Wss wss) =>
        (Uri)typeof(Wss)
            .GetField("_uri", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(wss)!;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0)
                continue;
            parameters[pair[..separator]] = pair[(separator + 1)..];
        }

        return parameters;
    }
}
