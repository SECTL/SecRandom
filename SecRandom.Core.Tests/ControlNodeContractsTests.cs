using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     Contract tests for the <c>control-v1</c> frame shapes, the node endpoint policy, and the persisted
///     node state (identity, local switch, applied desired-state revision).
/// </summary>
public sealed class ControlNodeContractsTests
{
    [Fact]
    public void SerializedFrames_UseProtocolFieldNames_AndOmitAbsentOnes()
    {
        var json = ControlProtocolJson.Serialize(new ControlFrame
        {
            Type = ControlFrameTypes.Hello,
            NodeId = "node-1",
            GroupId = "grp-1",
            Capabilities = [ControlCapabilities.StatusRead],
            LocalRemoteAllowed = true
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("hello", root.GetProperty("type").GetString());
        Assert.Equal("node-1", root.GetProperty("node_id").GetString());
        Assert.Equal("grp-1", root.GetProperty("group_id").GetString());
        Assert.True(root.GetProperty("local_remote_allowed").GetBoolean());

        // 字段缺失与 null 等价：没有值的字段不该出现在帧里。
        Assert.False(root.TryGetProperty("command_id", out _));
        Assert.False(root.TryGetProperty("desired_state_revision", out _));
    }

    /// <summary>
    ///     显示名用协议的 <c>display_name</c>；**没有名字时整个字段缺席**。
    /// </summary>
    /// <remarks>
    ///     缺席与空串在服务端是两件事：缺席 = 保持原值，空串 = 清除。因此"本机没有名字可报"
    ///     绝不能序列化成空串，否则会把管理端预置的名字抹掉。
    /// </remarks>
    [Fact]
    public void SerializedFrame_CarriesDisplayName_AndOmitsItWhenThereIsNone()
    {
        var json = ControlProtocolJson.Serialize(new ControlFrame
        {
            Type = ControlFrameTypes.Hello,
            NodeId = "node-1",
            GroupId = "grp-1",
            DisplayName = "301班讲台机"
        });

        using var document = JsonDocument.Parse(json);
        Assert.Equal("301班讲台机", document.RootElement.GetProperty("display_name").GetString());

        var bare = ControlProtocolJson.Serialize(new ControlFrame { Type = ControlFrameTypes.Hello });

        using var bareDocument = JsonDocument.Parse(bare);
        Assert.False(bareDocument.RootElement.TryGetProperty("display_name", out _));
    }

    [Fact]
    public void TryParse_AcceptsUnknownFields_AndRejectsFramesWithoutType()
    {
        var withUnknownField = ControlProtocolJson.TryParse(
            """{"type":"hello.ack","heartbeat_seconds":25,"future_field":{"a":1}}""");

        Assert.NotNull(withUnknownField);
        Assert.Equal(25, withUnknownField!.HeartbeatSeconds);

        Assert.Null(ControlProtocolJson.TryParse("""{"heartbeat_seconds":25}"""));
        Assert.Null(ControlProtocolJson.TryParse("not json at all"));
        Assert.Null(ControlProtocolJson.TryParse(""));
    }

    [Fact]
    public void DesiredStateRevision_IsReadAs64Bit()
    {
        // 服务端按 max(旧值 + 1, 当前毫秒) 生成，数值约 1.7×10¹²：32 位接收会溢出。
        var frame = ControlProtocolJson.TryParse("""{"type":"desired_state","desired_state_revision":1759572000123}""");

        Assert.NotNull(frame);
        Assert.Equal(1_759_572_000_123L, frame!.DesiredStateRevision);
    }

    [Fact]
    public void ReadDesiredState_ReadsOnlyDrawLocked_AndIgnoresEverythingElse()
    {
        var locked = ControlProtocolJson.ReadDesiredState(JsonSerializer.SerializeToElement(new { draw_locked = true }));
        Assert.NotNull(locked);
        Assert.True(locked!.DrawLocked);

        var unlocked = ControlProtocolJson.ReadDesiredState(JsonSerializer.SerializeToElement(new { draw_locked = false }));
        Assert.False(unlocked!.DrawLocked);

        Assert.Null(ControlProtocolJson.ReadDesiredState(JsonSerializer.SerializeToElement(new { other = true })));
        Assert.Null(ControlProtocolJson.ReadDesiredState(JsonSerializer.SerializeToElement(new { draw_locked = "yes" })));
        Assert.Null(ControlProtocolJson.ReadDesiredState(null));
    }

    [Theory]
    [InlineData("wss://secrandom-control.sectl.cn/v1/node/connect", true)]
    [InlineData("ws://127.0.0.1:8791/v1/node/connect", true)]
    [InlineData("ws://localhost:8791/v1/node/connect", true)]
    [InlineData("ws://[::1]:8791/v1/node/connect", true)]
    [InlineData("ws://example.com/v1/node/connect", false)]
    [InlineData("http://secrandom-control.sectl.cn/v1/node/connect", false)]
    [InlineData("wss://user:secret@secrandom-control.sectl.cn/v1/node/connect", false)]
    [InlineData("wss://secrandom-control.sectl.cn/v1/node/connect?token=abc", false)]
    [InlineData("wss://secrandom-control.sectl.cn/v1/node/connect#frag", false)]
    [InlineData("not a uri", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EndpointPolicy_RejectsPlaintextOffLoopback_AndSensitiveUriParts(string? endpoint, bool expected)
    {
        Assert.Equal(expected, ControlEndpointPolicy.TryValidate(endpoint, out var uri, out _));
        if (expected)
            Assert.NotNull(uri);
    }

    [Fact]
    public void NodeStateStore_KeepsAStableIdentity_AndPersistsTheSwitchAndRevision()
    {
        var path = Utils.GetFilePath("config", "control", "node-state.json");
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;

        try
        {
            File.Delete(path);

            var store = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);
            var nodeId = store.Current.NodeId;

            // 128 位随机十六进制：稳定、不可枚举、不含隐私。
            Assert.Equal(32, nodeId.Length);
            Assert.True(Guid.TryParseExact(nodeId, "N", out _));
            Assert.False(store.Current.RemoteControlEnabled);

            store.Update(state => state with
            {
                GroupId = "grp_9f8e7d6c5b4a",
                RemoteControlEnabled = true,
                AppliedDesiredStateRevision = 1_759_572_000_123,
                DrawLocked = true,
                // 名字是用户在设置页填的东西，必须跨重启保留——丢了就每重启一次回落到主机名。
                DisplayName = "301班讲台机"
            });

            // "重启"：新实例必须读回同一个身份与已应用的期望状态。
            var reloaded = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);
            Assert.Equal(nodeId, reloaded.Current.NodeId);
            Assert.Equal("grp_9f8e7d6c5b4a", reloaded.Current.GroupId);
            Assert.True(reloaded.Current.RemoteControlEnabled);
            Assert.Equal(1_759_572_000_123, reloaded.Current.AppliedDesiredStateRevision);
            Assert.True(reloaded.Current.DrawLocked);
            Assert.Equal("301班讲台机", reloaded.Current.DisplayName);
            Assert.True(reloaded.Current.IsConfigured);

            // 全是空白一律当"没填"：否则会带着一串空格去上报，看起来像"这台机器没有名字"。
            store.Update(state => state with { DisplayName = "   " });
            var cleared = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);
            Assert.Null(cleared.Current.DisplayName);
        }
        finally
        {
            if (backup is null)
                File.Delete(path);
            else
                File.WriteAllText(path, backup);
        }
    }

    [Fact]
    public void NodeStateStore_DefaultsTheServerUrlAndNeverTrustsACorruptFile()
    {
        var path = Utils.GetFilePath("config", "control", "node-state.json");
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;

        try
        {
            File.WriteAllText(path, "{ this is not json");

            var store = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);

            // 身份会重新生成，但组 ID 与开关回到保守默认：损坏的文件不该被信任。
            Assert.False(store.Current.IsConfigured);
            Assert.Equal(32, store.Current.NodeId.Length);
            Assert.Equal(ControlNodeClientOptions.DefaultEndpoint, store.Current.ServerUrl);
            Assert.False(store.Current.RemoteControlEnabled);
            Assert.Equal(ControlDesiredState.NeverSetRevision, store.Current.AppliedDesiredStateRevision);
        }
        finally
        {
            if (backup is null)
                File.Delete(path);
            else
                File.WriteAllText(path, backup);
        }
    }
}
