using SecRandom.Core.Services.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     设备显示名的取值规则：**用户填的优先，留空回落到主机名**。
/// </summary>
/// <remarks>
///     这两个规则各自都有一个容易做错的边界：留空时**不能发空串**（协议里空串是"清除名字"，
///     会把管理端预置的名字抹掉），截断时**不能把代理对切成半个字符**（控制台里会平白少一个字符）。
/// </remarks>
public sealed class ControlNodeDisplayNameTests
{
    private const string HostName = "sr-test-host";

    [Theory]
    [InlineData("301班讲台机", "301班讲台机")]
    [InlineData("  301班讲台机  ", "301班讲台机")]
    [InlineData("", HostName)]
    [InlineData("   ", HostName)]
    [InlineData("\t\r\n", HostName)]
    [InlineData(null, HostName)]
    public void 填了用填的_留空回落主机名(string? configured, string expected) =>
        Assert.Equal(expected, ControlNodeDisplayName.Resolve(configured, HostName));

    [Fact]
    public void 两个来源都为空时不填字段_而不是发空串()
    {
        // 空串在协议里是"清除名字"；"本机什么名字都取不到"根本不是用户要求清除。
        Assert.Null(ControlNodeDisplayName.Resolve(null, null));
        Assert.Null(ControlNodeDisplayName.Resolve("  ", "  "));
        Assert.Null(ControlNodeDisplayName.Resolve("", "\t"));
    }

    [Fact]
    public void 超长名字被截断到协议上限()
    {
        var resolved = ControlNodeDisplayName.Resolve(new string('机', 200), null);

        Assert.NotNull(resolved);
        Assert.Equal(ControlNodeDisplayName.MaxLength, resolved.Length);
    }

    [Fact]
    public void 主机名超长时同样截断()
    {
        var resolved = ControlNodeDisplayName.Resolve(null, new string('h', 100));

        Assert.NotNull(resolved);
        Assert.Equal(ControlNodeDisplayName.MaxLength, resolved.Length);
    }

    [Fact]
    public void 截断不会把代理对切成半个字符()
    {
        // 一个 emoji 占两个 UTF-16 码元，上限是偶数时正好会被切开。
        var configured = string.Concat(Enumerable.Repeat("\U0001F600", 80));

        var resolved = ControlNodeDisplayName.Resolve(configured, null);

        Assert.NotNull(resolved);
        Assert.True(resolved.Length <= ControlNodeDisplayName.MaxLength);
        Assert.False(char.IsHighSurrogate(resolved[^1]), "末尾不该留下孤立的高位代理");
    }
}
