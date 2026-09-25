using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Services.Linkage;

namespace SecRandom.Core.Tests;

/// <summary>
/// ClassIsland IPC 门槛与等待策略：通知服务必须通过插件版本门槛（旧插件不满足通知契约），
/// 退避期内不能继续空等，否则内置回退通知与抽取前的 QuickDraw 窗口会被拖到十秒之后。
/// </summary>
public sealed class ClassIslandIpcConnectionTests
{
    [Theory]
    [InlineData("Yes", "1.2.0.0", true)]
    [InlineData("Yes", "1.3.0.0", true)]
    [InlineData("Yes", "1.1.0.0", false)]
    [InlineData("Yes", null, false)]
    [InlineData("No", "1.2.0.0", false)]
    [InlineData("yes", "1.2.0.0", false)]
    public void IsNotificationServiceUsable_GatesPluginVersion(string? isAlive, string? version, bool expected)
    {
        var pluginVersion = version is null ? null : Version.Parse(version);

        Assert.Equal(expected, ClassIslandIpcConnection.IsNotificationServiceUsable(isAlive, pluginVersion));
    }

    [Fact]
    public async Task GetLessonsServiceAsync_ReturnsImmediatelyDuringBackoff()
    {
        var connection = CreateConnectionInBackoff();

        var elapsed = await MeasureAsync(() => connection.GetLessonsServiceAsync(TestContext.Current.CancellationToken));

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"退避期内不应继续等待：{elapsed}");
    }

    [Fact]
    public async Task GetNotificationServiceAsync_ReturnsImmediatelyDuringBackoff()
    {
        var connection = CreateConnectionInBackoff();

        var elapsed = await MeasureAsync(() => connection.GetNotificationServiceAsync(TestContext.Current.CancellationToken));

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"退避期内不应继续等待：{elapsed}");
    }

    private static ClassIslandIpcConnection CreateConnectionInBackoff()
    {
        var connection = new ClassIslandIpcConnection(NullLogger<ClassIslandIpcConnection>.Instance);
        var field = typeof(ClassIslandIpcConnection).GetField(
                        "_nextConnectAttempt", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("ClassIslandIpcConnection._nextConnectAttempt 字段已改名。");
        field.SetValue(connection, DateTimeOffset.UtcNow.AddMinutes(5));
        return connection;
    }

    private static async Task<TimeSpan> MeasureAsync(Func<Task> action)
    {
        var start = DateTime.UtcNow;
        await action();
        return DateTime.UtcNow - start;
    }
}
