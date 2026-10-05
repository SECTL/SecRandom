using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Voice;

namespace SecRandom.Core.Tests;

/// <summary>
///     <c>media.play</c> 里"本机做不到的选项"与"临时音量"的可测逻辑（§4.5.7）。
/// </summary>
/// <remarks>
///     handler 本身要通知服务与 Avalonia 界面，单测造不出来，所以这两件纯逻辑被抽到
///     <see cref="ControlMediaPlayPlatformSupport" /> 与 <see cref="TemporaryVoiceVolume" /> 里测；
///     再加一条服务层断言，钉住"临时改 VolumeSize 对本次播报有效"这个前提。
/// </remarks>
public sealed class ControlMediaPlayOptionTests
{
    // ------------------------------------------------------------ 系统音量：明确拒绝

    [Fact]
    public void 系统音量_没有平台实现时带该字段的命令必须被明确拒绝()
    {
        var request = Request(systemVolumePercent: 40);

        Assert.Equal(
            "system_volume_percent",
            ControlMediaPlayPlatformSupport.FindUnsupportedField(request, systemVolumeSupported: false));
    }

    [Fact]
    public void 系统音量_平台实现落地后同一条载荷不再被拒()
    {
        // 把"将来补上 Windows CoreAudio 之后这道拒绝必须消失"写进测试，
        // 免得实现落地了还留着一道恒拒的闸门。
        var request = Request(systemVolumePercent: 40);

        Assert.Null(ControlMediaPlayPlatformSupport.FindUnsupportedField(request, systemVolumeSupported: true));
    }

    [Fact]
    public void 系统音量_不带该字段时无论平台支不支持都不拒()
    {
        var request = Request();

        Assert.Null(ControlMediaPlayPlatformSupport.FindUnsupportedField(request, systemVolumeSupported: false));
        Assert.Null(ControlMediaPlayPlatformSupport.FindUnsupportedField(request, systemVolumeSupported: true));
    }

    [Fact]
    public void 系统音量_本机今天确实没有实现()
    {
        // 这个常量就是"不假装成功"的开关：把它改成 true 之前，先得有真能改端点音量的实现，
        // 否则控制台会以为音量调了而教室里根本没变。
        Assert.False(ControlMediaPlayPlatformSupport.SystemVolumeSupported);
    }

    // ------------------------------------------------------------ 临时播报音量

    [Fact]
    public void 临时音量_给了就改内存并在释放后恢复原值()
    {
        var settings = new VoiceSettingsConfig { VolumeSize = 80 };

        using (TemporaryVoiceVolume.Apply(settings, 30))
        {
            Assert.Equal(30, settings.VolumeSize);
        }

        Assert.Equal(80, settings.VolumeSize);
    }

    [Fact]
    public void 临时音量_没给就一个字都不动()
    {
        // 老载荷（不带这个字段）必须逐字同行为：不碰值，也不发 PropertyChanged——
        // 后者会让开着的语音设置页面把配置写盘一次。
        var settings = new VoiceSettingsConfig { VolumeSize = 80 };
        var changed = 0;
        settings.PropertyChanged += (_, _) => changed++;

        using (TemporaryVoiceVolume.Apply(settings, null))
        {
            Assert.Equal(80, settings.VolumeSize);
            Assert.Equal(0, changed);
        }

        Assert.Equal(80, settings.VolumeSize);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void 临时音量_重复释放不会把音量恢复成上一轮的临时值()
    {
        var settings = new VoiceSettingsConfig { VolumeSize = 80 };
        var scope = TemporaryVoiceVolume.Apply(settings, 30);

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(80, settings.VolumeSize);
    }

    [Fact]
    public void 临时音量_越界输入不会写进活着的配置对象()
    {
        var settings = new VoiceSettingsConfig { VolumeSize = 80 };

        using (TemporaryVoiceVolume.Apply(settings, 500))
        {
            Assert.Equal(100, settings.VolumeSize);
        }

        Assert.Equal(80, settings.VolumeSize);
    }

    // ------------------------------------------------------------ 服务层：临时音量真能生效

    [Fact]
    public async Task 播报取的是调用时的配置音量_所以临时改VolumeSize对本次播报有效()
    {
        // 集控的 voice_volume_percent 靠"播报前临时改 VolumeSize、播完恢复"实现。
        // 这条断言钉住它赖以成立的前提：SpeakAsync 取的是**调用那一刻**的 VolumeSize，
        // 不是构造时的快照。哪天有人把设置缓存到字段里，这里会先红。
        var config = new MainConfigModel();
        config.VoiceSettings.VoiceEnable = true;
        config.VoiceSettings.VoiceEngine = TestEngine;
        config.VoiceSettings.VolumeSize = 80;

        var provider = new TestSpeechProvider();
        var player = new RecordingAudioPlayer();
        var handler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config));
        var service = new VoiceAnnouncementService(
            handler,
            [provider],
            player,
            NullLogger<VoiceAnnouncementService>.Instance);

        try
        {
            using (TemporaryVoiceVolume.Apply(config.VoiceSettings, 30))
            {
                await service.SpeakAsync(
                    "请第一组上台",
                    waitForCompletion: true,
                    TestContext.Current.CancellationToken);
            }

            // 恢复之后再来一次：音量必须回到原值，而不是把上一轮的临时值留下。
            await service.SpeakAsync(
                "请第二组上台",
                waitForCompletion: true,
                TestContext.Current.CancellationToken);

            Assert.Equal(2, player.Volumes.Count);
            Assert.Equal(30, player.Volumes[0]);
            Assert.Equal(80, player.Volumes[1]);
        }
        finally
        {
            foreach (var path in player.Paths)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    // ------------------------------------------------------------ 测试替身

    private const int TestEngine = 7;

    private static ControlMediaPlayRequest Request(int? systemVolumePercent = null) =>
        new(ControlMediaPlayRequest.AnnounceAction, "请第一组上台", null, systemVolumePercent);

    private sealed class TestSpeechProvider : ISpeechProvider
    {
        public int Engine => TestEngine;

        public Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VoiceOption>>([]);

        public Task<SpeechAudio> SynthesizeAsync(
            SpeechSynthesisRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SpeechAudio([1], ".wav"));
    }

    private sealed class RecordingAudioPlayer : ISpeechAudioPlayer
    {
        public List<string> Paths { get; } = [];

        public List<int> Volumes { get; } = [];

        public Task PlayAsync(
            string path,
            int volume,
            int playbackSpeed,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(path);
            Volumes.Add(volume);
            return Task.CompletedTask;
        }
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;

        public override void SaveConfig<T>(T value)
        {
        }

        public override void DeleteConfig<T>(T value)
        {
        }
    }
}
