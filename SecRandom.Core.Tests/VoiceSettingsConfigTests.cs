using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Voice;

namespace SecRandom.Core.Tests;

public class VoiceSettingsConfigTests
{
    [Fact]
    public void DefaultVoiceEngineIsEdgeTts()
    {
        Assert.Equal(1, new VoiceSettingsConfig().VoiceEngine);
    }

    [Fact]
    public void VoiceAnnouncementDefaultsToNamesWithoutIdentifiers()
    {
        var voice = new VoiceSettingsConfig();
        var config = new MainConfigModel();

        Assert.False(voice.AnnounceId);
        Assert.True(voice.AnnounceName);
        Assert.True(config.DefaultDrawSettings.VoiceAnnouncementEnabled);
        Assert.True(config.RollCallSettings.VoiceAnnouncementEnabled);
        Assert.True(config.QuickDrawSettings.VoiceAnnouncementEnabled);
        Assert.True(config.LotterySettings.VoiceAnnouncementEnabled);
    }

    [Fact]
    public void PerDrawVoiceAnnouncementCanOverrideTheDefaultSetting()
    {
        var config = new MainConfigModel();
        config.DefaultDrawSettings.VoiceAnnouncementEnabled = false;
        config.RollCallSettings.VoiceAnnouncementEnabled = true;

        Assert.False(config.GetOverrideDrawSettings(
            DrawSettingsType.RollCall,
            OverridableDrawSettingsType.VoiceAnnouncement).VoiceAnnouncementEnabled);

        config.RollCallSettings.OverrideVoiceAnnouncementSettings = true;

        Assert.True(config.GetOverrideDrawSettings(
            DrawSettingsType.RollCall,
            OverridableDrawSettingsType.VoiceAnnouncement).VoiceAnnouncementEnabled);
    }

    [Theory]
    [InlineData(LanguageMode.ChineseSimplified, "zh-CN-XiaoxiaoNeural")]
    [InlineData(LanguageMode.English, "en-US-JennyNeural")]
    [InlineData(LanguageMode.Japanese, "ja-JP-NanamiNeural")]
    public void DefaultEdgeVoiceMatchesConfiguredLanguage(LanguageMode language, string expectedVoice)
    {
        Assert.Equal(expectedVoice, VoiceSettingsConfig.GetDefaultEdgeTtsVoiceName(language));
    }

    [Fact]
    public async Task EdgeProviderLoadsXiaoxiaoFromEmbeddedVoiceList()
    {
        var voices = await new EdgeTtsSpeechProvider().GetVoicesAsync(TestContext.Current.CancellationToken);

        Assert.Contains(voices, voice => voice.Id == "zh-CN-XiaoxiaoNeural");
    }

    [Fact]
    public async Task VoiceCacheKeysAreBoundedAndDistinctAfterFilenameSanitization()
    {
        var config = new MainConfigModel();
        config.VoiceSettings.VoiceEnable = true;
        config.VoiceSettings.VoiceEngine = 7;
        var provider = new TestSpeechProvider();
        var player = new TestSpeechAudioPlayer();
        var handler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config));
        var service = new VoiceAnnouncementService(
            handler,
            [provider],
            player,
            NullLogger<VoiceAnnouncementService>.Instance);
        var sharedText = $"{Guid.NewGuid():N}{new string('x', 300)}";

        try
        {
            await service.SpeakAsync(
                $"{sharedText}/",
                waitForCompletion: true,
                TestContext.Current.CancellationToken);
            await service.SpeakAsync(
                $"{sharedText}\\",
                waitForCompletion: true,
                TestContext.Current.CancellationToken);

            Assert.Equal(2, provider.SynthesisCount);
            Assert.Equal(2, player.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(player.Paths, path =>
            {
                var fileName = Path.GetFileName(path);
                Assert.True(fileName.Length <= 182);
                Assert.DoesNotContain('\\', fileName);
                Assert.DoesNotContain('/', fileName);
            });
        }
        finally
        {
            foreach (var path in player.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private sealed class TestSpeechProvider : ISpeechProvider
    {
        public int Engine => 7;
        public int SynthesisCount { get; private set; }

        public Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VoiceOption>>([]);

        public Task<SpeechAudio> SynthesizeAsync(
            SpeechSynthesisRequest request,
            CancellationToken cancellationToken = default)
        {
            SynthesisCount++;
            return Task.FromResult(new SpeechAudio([1], ".wav"));
        }
    }

    private sealed class TestSpeechAudioPlayer : ISpeechAudioPlayer
    {
        public List<string> Paths { get; } = [];

        public Task PlayAsync(
            string path,
            int volume,
            int playbackSpeed,
            CancellationToken cancellationToken = default)
        {
            Paths.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
