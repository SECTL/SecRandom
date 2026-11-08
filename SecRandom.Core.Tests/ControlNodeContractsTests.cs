using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Core.Tests;

/// <summary>
///     v4.1 的帧形状、节点通道地址策略与本机状态的契约测试。
/// </summary>
public sealed class ControlNodeContractsTests
{
    [Fact]
    public void 序列化帧使用协议字段名且省略缺省字段()
    {
        var json = ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeRequest,
            Id = "hello-1",
            Topic = ControlV4Protocol.Topics.Hello,
            Payload = ControlV4Json.ToElement(new ControlV4Hello
            {
                DeviceId = "dev-1",
                Secret = "secret-1",
                DeviceName = "301班讲台机",
                Version = "1.0.0",
                Capabilities = [ControlCapabilities.Broadcast]
            })
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("req", root.GetProperty("type").GetString());
        Assert.Equal("hello-1", root.GetProperty("id").GetString());
        Assert.Equal("hello", root.GetProperty("topic").GetString());

        var payload = root.GetProperty("payload");
        Assert.Equal(4, payload.GetProperty("protocol").GetInt32());
        Assert.Equal("secrandom", payload.GetProperty("platform").GetString());
        Assert.Equal("dev-1", payload.GetProperty("device_id").GetString());
        Assert.Equal("secret-1", payload.GetProperty("secret").GetString());
        Assert.Equal("301班讲台机", payload.GetProperty("device_name").GetString());
        Assert.False(payload.TryGetProperty("class_id", out _));
    }

    [Fact]
    public void 应答帧在失败时携带错误码_成功时不携带()
    {
        var ok = ControlV4Json.ToElement(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeResponse,
            Id = "hello-1",
            Ok = true
        });
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.False(ok.TryGetProperty("code", out _));

        var failed = ControlV4Json.ToElement(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeResponse,
            Id = "hello-1",
            Ok = false,
            Code = ControlV4Protocol.ErrorCodes.BadSecret
        });
        Assert.False(failed.GetProperty("ok").GetBoolean());
        Assert.Equal("bad_secret", failed.GetProperty("code").GetString());
    }

    [Fact]
    public void TryParse_接受未知字段_拒绝缺type或非JSON()
    {
        var frame = ControlV4Json.TryParse(
            """{"type":"res","id":"hello-1","ok":true,"payload":{"heartbeat_seconds":25,"future_field":{"a":1}}}""");

        Assert.NotNull(frame);
        Assert.Equal("hello-1", frame!.Id);
        Assert.True(frame.Ok);

        // 未知字段不参与解析，但不会让整帧失败：服务端加字段时老客户端要继续能用。
        var ack = ControlV4HelloAck.Parse(frame.Payload);
        Assert.Equal(25, ack!.HeartbeatSeconds);

        Assert.Null(ControlV4Json.TryParse("""{"id":"hello-1"}"""));
        Assert.Null(ControlV4Json.TryParse("not json at all"));
        Assert.Null(ControlV4Json.TryParse(""));
    }

    [Fact]
    public void HelloAck_缺少draw_locked时按锁定处理_缺省心跳用25秒()
    {
        // fail-closed：服务端没说"允许抽"，就不允许抽。少了一个字段就放开是本项目的红线。
        var ack = ControlV4HelloAck.Parse(JsonSerializer.SerializeToElement(new { desired_state_revision = 9L }));
        Assert.NotNull(ack);
        Assert.True(ack!.DrawLocked);
        Assert.Equal(25, ack!.HeartbeatSeconds);
        Assert.Equal(75, ack!.OfflineAfterSeconds);
        Assert.Equal(9L, ack!.DesiredStateRevision);

        var unlocked = ControlV4HelloAck.Parse(JsonSerializer.SerializeToElement(new
        {
            draw_locked = false,
            heartbeat_seconds = 10,
            offline_after_seconds = 30,
            device = new { id = "dev-1", name = "讲台机" }
        }));
        Assert.False(unlocked!.DrawLocked);
        Assert.Equal(10, unlocked!.HeartbeatSeconds);
        Assert.Equal(30, unlocked!.OfflineAfterSeconds);
        Assert.Equal("dev-1", unlocked!.DeviceId);
        Assert.Equal("讲台机", unlocked!.DeviceName);

        // 没有 payload（老服务端/异常响应）→ 返回 fail-closed 的默认值：锁定 + 兜底心跳，
// 绝不能因为「没解析出字段」就当成允许抽取。
        var missing = ControlV4HelloAck.Parse(null);
        Assert.True(missing.DrawLocked);
        Assert.Equal(25, missing.HeartbeatSeconds);
        Assert.Equal(75, missing.OfflineAfterSeconds);
    }

    [Fact]
    public void DesiredState_读取64位revision和缺失的draw_locked()
    {
        // 服务端按 max(旧值 + 1, 当前毫秒) 生成，数值约 1.7×10¹²：32 位接收会溢出。
        var parsed = ControlV4DesiredState.TryParse(JsonSerializer.SerializeToElement(new
        {
            revision = 1_759_572_000_123L,
            class_id = "class-1",
            draw_locked = true
        }));

        Assert.NotNull(parsed);
        Assert.Equal(1_759_572_000_123L, parsed!.Revision);
        Assert.Equal("class-1", parsed!.ClassId);
        Assert.True(parsed!.DrawLocked);

        var lockedByDefault = ControlV4DesiredState.TryParse(
            JsonSerializer.SerializeToElement(new { revision = 5L }));
        Assert.Null(lockedByDefault!.DrawLocked);

        Assert.Null(ControlV4DesiredState.TryParse(
            JsonSerializer.SerializeToElement(new { class_id = "class-1" })));
    }

    [Fact]
    public void Command_读取毫秒expires_at与命令名()
    {
        var parsed = ControlV4Command.TryParse(JsonSerializer.SerializeToElement(new
        {
            command_id = "cmd-1",
            cmd = "draw.trigger",
            args = new { roster_kind = "students" },
            issuer = "user-1",
            expires_at = 1_759_572_000_123L
        }), out var command);

        Assert.True(parsed);
        Assert.Equal("cmd-1", command!.CommandId);
        Assert.Equal("draw.trigger", command!.Cmd);
        Assert.Equal(1_759_572_000_123L, command!.ExpiresAt);
        Assert.Equal("user-1", command!.Issuer);

        Assert.False(ControlV4Command.TryParse(
            JsonSerializer.SerializeToElement(new { cmd = "draw.trigger" }), out _));
        Assert.False(ControlV4Command.TryParse(
            JsonSerializer.SerializeToElement(new { command_id = "cmd-1" }), out _));
    }

    [Theory]
    [InlineData("wss://sectl.cn/control/ws", true)]
    [InlineData("ws://127.0.0.1:8791/control/ws", true)]
    [InlineData("ws://localhost:8791/control/ws", true)]
    [InlineData("ws://[::1]:8791/control/ws", true)]
    [InlineData("ws://example.com/control/ws", false)]
    [InlineData("http://sectl.cn/control/ws", false)]
    [InlineData("wss://user:secret@sectl.cn/control/ws", false)]
    [InlineData("wss://sectl.cn/control/ws?token=abc", false)]
    [InlineData("wss://sectl.cn/control/ws#frag", false)]
    [InlineData("not a uri", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 通道地址策略拒绝明文非回环与敏感URI片段(string? endpoint, bool expected)
    {
        Assert.Equal(expected, ControlEndpointPolicy.TryValidate(endpoint, out var uri, out _));
        if (expected)
            Assert.NotNull(uri);
    }

    [Fact]
    public void 本机状态跨重启保留设备身份_地点与班级()
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
                SiteId = "SC-K7Q2M9",
                SiteName = "第一实验学校",
                ClassId = "class-1",
                RemoteControlEnabled = true,
                AppliedDesiredStateRevision = 1_759_572_000_123,
                DrawLocked = true,
                DisplayName = "301班讲台机"
            });

            var reloaded = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);
            Assert.Equal(nodeId, reloaded.Current.NodeId);
            Assert.Equal("SC-K7Q2M9", reloaded.Current.SiteId);
            Assert.Equal("第一实验学校", reloaded.Current.SiteName);
            Assert.Equal("class-1", reloaded.Current.ClassId);
            Assert.True(reloaded.Current.RemoteControlEnabled);
            Assert.Equal(1_759_572_000_123, reloaded.Current.AppliedDesiredStateRevision);
            Assert.True(reloaded.Current.DrawLocked);
            Assert.Equal("301班讲台机", reloaded.Current.DisplayName);

            // 全是空白一律当"没填"：否则会带着一串空格去上报，看起来像"这台机器没有名字"。
            store.Update(state => state with { DisplayName = "   ", SiteName = "  " });
            var cleared = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);
            Assert.Null(cleared.Current.DisplayName);
            Assert.Null(cleared.Current.SiteName);
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
    public void 旧版节点通道地址会被迁移到新默认并落盘()
    {
        var path = Utils.GetFilePath("config", "control", "node-state.json");
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """
                {
                  "node_id": "0123456789abcdef0123456789abcdef",
                  "server_url": "wss://secrandom-control.sectl.cn/v1/node/connect"
                }
                """);

            var store = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);

            // 旧域名在新集控上没有服务：读回来就换成新出厂默认。
            Assert.Equal(ControlNodeClientOptions.DefaultEndpoint, store.Current.ServerUrl);

            // 迁移必须落盘：否则每次启动都会把死地址再读回来一次。
            var reloaded = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);
            Assert.Equal(ControlNodeClientOptions.DefaultEndpoint, reloaded.Current.ServerUrl);
            Assert.DoesNotContain(
                "secrandom-control",
                File.ReadAllText(path),
                StringComparison.OrdinalIgnoreCase);
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
    public void 本机状态默认地址且绝不信任损坏文件()
    {
        var path = Utils.GetFilePath("config", "control", "node-state.json");
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;

        try
        {
            File.WriteAllText(path, "{ this is not json");

            var store = new FileControlNodeStateStore(NullLogger<FileControlNodeStateStore>.Instance);

            // 身份会重新生成，但接入地点、开关与班级回到保守默认：损坏的文件不该被信任。
            Assert.Null(store.Current.SiteId);
            Assert.Null(store.Current.ClassId);
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