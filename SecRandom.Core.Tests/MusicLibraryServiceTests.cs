using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Music;

namespace SecRandom.Core.Tests;

public sealed class MusicLibraryServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SecRandomMusicTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Import_DoesNotOverwriteDuplicateTracks()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "source.mp3");
        File.WriteAllBytes(source, [1, 2, 3]);
        var service = CreateService(out _);

        var first = Assert.Single(service.Import([source]));
        var second = Assert.Single(service.Import([source]));

        Assert.NotEqual(first.Id, second.Id);
        Assert.True(File.Exists(Path.Combine(_directory, first.Id)));
        Assert.True(File.Exists(Path.Combine(_directory, second.Id)));
    }

    [Fact]
    public void ResolvePath_RejectsUnsupportedAndTraversalSelections()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "track.mp3"), [1]);
        var service = CreateService(out _);
        service.Refresh();

        Assert.NotNull(service.ResolvePath("track.mp3"));
        Assert.Null(service.ResolvePath("../track.mp3"));
        Assert.Null(service.ResolvePath("track.ogg"));
    }

    [Fact]
    public void NewDrawSettings_HaveUsableMusicControlDefaults()
    {
        var settings = new DrawSettingsConfigBase();

        Assert.Equal("$none", settings.AnimationMusic);
        Assert.Equal("$none", settings.ResultMusic);
        Assert.Equal(100, settings.AnimationMusicVolume);
        Assert.Equal(100, settings.ResultMusicVolume);
        Assert.Equal(300, settings.AnimationMusicFadeIn);
        Assert.Equal(300, settings.AnimationMusicFadeOut);
        Assert.Equal(300, settings.ResultMusicFadeIn);
        Assert.Equal(300, settings.ResultMusicFadeOut);
    }

    [Fact]
    public void Delete_ClearsEveryDefaultAndOverrideReference()
    {
        Directory.CreateDirectory(_directory);
        var trackPath = Path.Combine(_directory, "track.wav");
        File.WriteAllBytes(trackPath, [1]);
        var service = CreateService(out var config);
        service.Refresh();

        config.DefaultDrawSettings.AnimationMusic = "track.wav";
        config.RollCallSettings.ResultMusic = "track.wav";
        config.QuickDrawSettings.AnimationMusic = "track.wav";
        config.LotterySettings.ResultMusic = "track.wav";

        Assert.True(service.Delete(Assert.Single(service.Tracks)));
        Assert.Equal(MusicLibraryService.NoMusicTrackId, config.DefaultDrawSettings.AnimationMusic);
        Assert.Equal(MusicLibraryService.NoMusicTrackId, config.RollCallSettings.ResultMusic);
        Assert.Equal(MusicLibraryService.NoMusicTrackId, config.QuickDrawSettings.AnimationMusic);
        Assert.Equal(MusicLibraryService.NoMusicTrackId, config.LotterySettings.ResultMusic);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private MusicLibraryService CreateService(out MainConfigModel config)
    {
        config = new MainConfigModel();
        var handler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config));
        return new MusicLibraryService(handler, NullLogger<MusicLibraryService>.Instance, _directory);
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
