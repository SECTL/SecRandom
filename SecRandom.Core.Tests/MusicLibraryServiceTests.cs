using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Extensions;
using SecRandom.Services.Music;
using SecRandom.Shared.Models.Profile;

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
    public void Delete_RejectsExternalCompatibilityPaths()
    {
        Directory.CreateDirectory(_directory);
        var externalPath = Path.Combine(_directory, "external.wav");
        File.WriteAllBytes(externalPath, [1]);
        var service = CreateService(out _);

        Assert.False(service.Delete(new MusicTrack(externalPath, "external", 1)));
        Assert.True(File.Exists(externalPath));
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

    [Fact]
    public void Delete_ClearsActiveAttachedMusicReferences()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "track.wav"), [1]);
        var student = new Student();
        student.AttachedObjects[Guid.Parse(GlobalConstants.DrawMusicAttachedSettings)] = new DrawMusicAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            AnimationMusic = "track.wav",
            ResultMusic = "track.wav"
        };
        var profileService = new TestProfileService(student);
        var service = CreateService(out _, profileService);
        service.Refresh();

        Assert.True(service.Delete(Assert.Single(service.Tracks)));
        var settings = student.GetAttachedObject<DrawMusicAttachedSettings>(Guid.Parse(GlobalConstants.DrawMusicAttachedSettings));
        Assert.NotNull(settings);
        Assert.Equal(MusicLibraryService.NoMusicTrackId, settings.AnimationMusic);
        Assert.Equal(MusicLibraryService.NoMusicTrackId, settings.ResultMusic);
        Assert.Equal(1, profileService.SaveCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private MusicLibraryService CreateService(out MainConfigModel config, IProfileService? profileService = null)
    {
        config = new MainConfigModel();
        var handler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config));
        return new MusicLibraryService(handler, NullLogger<MusicLibraryService>.Instance, _directory, profileService);
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }

    private sealed class TestProfileService(Student student) : IProfileService
    {
        public int SaveCount { get; private set; }
        public StudentList? CurrentStudentList { get; } = new() { Students = [student] };
        public StudentHistory? CurrentStudentHistory => null;
        public PrizeList? CurrentPrizeList => null;
        public PrizeHistory? CurrentPrizeHistory => null;
        public StudentListConfig? StudentListConfig => null;
        public StudentHistoryConfig? StudentHistoryConfig => null;
        public PrizeListConfig? PrizeListConfig => null;
        public PrizeHistoryConfig? PrizeHistoryConfig => null;
        public void LoadStudentProfile(string name, bool saveCurrent = true) { }
        public void LoadPrizeProfile(string name, bool saveCurrent = true) { }
        public void RecordStudentHistory(IReadOnlyList<Student> students, DateTime now, int requestedCount,
            string drawGroup = "", string drawGender = "", int drawMethod = 0,
            IReadOnlyDictionary<Student, double>? weights = null, string courseName = "") { }
        public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount) { }
        public void ClearCurrentStudentHistory() { }
        public void ClearCurrentPrizeHistory() { }
        public void SaveProfile() => SaveCount++;
    }
}
