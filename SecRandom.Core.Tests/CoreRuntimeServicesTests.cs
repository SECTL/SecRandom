using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Models.Draw;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class CoreRuntimeServicesTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "runtime-tests", Guid.NewGuid().ToString("N"));

    public CoreRuntimeServicesTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void ConfiguredDataRoot_UsesTheConfiguredPrivateDirectoryAndCannotChangeLater()
    {
        var settingsPath = Utils.GetFilePath("config", "settings.json");

        Assert.Equal(Path.Combine(Path.GetFullPath(_dataRoot), "config", "settings.json"), settingsPath);
        var exception = Assert.Throws<TargetInvocationException>(() => ConfigureDataRootForTests(Path.Combine(_dataRoot, "other")));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void ConfigDirectory_AppliesThePlatformHiddenPolicy()
    {
        _ = Utils.GetFilePath("config", "settings.json");

        var configDirectory = Path.Combine(_dataRoot, "config");
        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(configDirectory);
            Assert.True(attributes.HasFlag(FileAttributes.Hidden));
            Assert.True(attributes.HasFlag(FileAttributes.System));
            return;
        }

        var hiddenEntries = File.ReadAllLines(Path.Combine(_dataRoot, ".hidden"));
        Assert.Contains("config", hiddenEntries);
    }

    [Fact]
    public void FileConfigService_DoesNotOverwriteAnEncryptedEnvelope()
    {
        using var provider = CreateProvider();
        var service = provider.GetRequiredService<ConfigServiceBase>();
        var fallback = new MainConfigModel();
        var path = fallback.ConfigFilePath;
        const string encryptedEnvelope = """
                                         {"version":1,"nonce":"nonce","tag":"tag","ciphertext":"ciphertext"}
                                         """;

        File.WriteAllText(path, encryptedEnvelope);

        var loaded = service.LoadConfig(fallback);

        Assert.Same(fallback, loaded);
        Assert.Equal(encryptedEnvelope, File.ReadAllText(path));
    }

    [Fact]
    public void ProfileAndTemporaryRecordServices_PersistStableRecordIdsAtTheConfiguredRoot()
    {
        using var provider = CreateProvider();
        var configHandler = provider.GetRequiredService<MainConfigHandler>();
        configHandler.Data.RollCallSettings.DefaultClass = "mobile-class";
        configHandler.Data.LotterySettings.DefaultPool = "mobile-pool";
        configHandler.Data.RollCallSettings.DrawMode = DrawMode.Repeat;
        configHandler.Data.RollCallSettings.DrawType = DrawType.Random;
        configHandler.Save();

        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        var prize = new Prize { Name = "Book", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.SaveProfile();

        var temporaryRecords = provider.GetRequiredService<IDrawTemporaryRecordService>();
        temporaryRecords.RecordStudents("mobile-class", string.Empty, string.Empty, [student]);
        temporaryRecords.RecordPrizes("mobile-pool", [prize]);

        using var reloadedProvider = CreateProvider();
        var reloadedProfile = reloadedProvider.GetRequiredService<IProfileService>();
        var reloadedTemporaryRecords = reloadedProvider.GetRequiredService<IDrawTemporaryRecordService>();

        Assert.Equal(student.RecordId, Assert.Single(reloadedProfile.CurrentStudentList!.Students).RecordId);
        Assert.Equal(prize.RecordId, Assert.Single(reloadedProfile.CurrentPrizeList!.Prizes).RecordId);
        Assert.Equal(1, reloadedTemporaryRecords.GetStudentCounts("mobile-class", string.Empty, string.Empty)[student.RecordId.ToString("D")]);
        Assert.Equal(1, reloadedTemporaryRecords.GetPrizeCounts("mobile-pool")[prize.RecordId.ToString("D")]);
        Assert.StartsWith(Path.GetFullPath(_dataRoot), reloadedProfile.CurrentStudentList.ConfigFilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporaryRecordReset_OverwritesEmptyStateWithoutDeletingFiles()
    {
        using var provider = CreateProvider();
        var temporaryRecords = provider.GetRequiredService<IDrawTemporaryRecordService>();
        var student = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        var prize = new Prize { Name = "Book", RecordId = Guid.NewGuid() };

        temporaryRecords.RecordStudents("reset-class", string.Empty, string.Empty, [student]);
        temporaryRecords.RecordPrizes("reset-pool", [prize]);
        temporaryRecords.ResetStudentList("reset-class");
        temporaryRecords.ResetPrizeList("reset-pool");

        Assert.Empty(temporaryRecords.GetStudentCounts("reset-class", string.Empty, string.Empty));
        Assert.Empty(temporaryRecords.GetPrizeCounts("reset-pool"));
        Assert.True(File.Exists(Utils.GetFilePath("TEMP", "roll_call_record_reset-class.json")));
        Assert.True(File.Exists(Utils.GetFilePath("TEMP", "lottery_record_reset-pool.json")));
    }

    [Fact]
    public void DrawEngineAndFeatureAvailabilityService_WorkWithoutStaticHostResolution()
    {
        using var provider = CreateProvider();
        var configHandler = provider.GetRequiredService<MainConfigHandler>();
        configHandler.Data.RollCallSettings.DefaultClass = "draw-class";
        configHandler.Data.RollCallSettings.DrawMode = DrawMode.Repeat;
        configHandler.Data.RollCallSettings.DrawType = DrawType.Random;
        configHandler.Save();

        var profile = provider.GetRequiredService<IProfileService>();
        profile.CurrentStudentList!.Students.Add(new Student { Name = "Grace", RecordId = Guid.NewGuid() });
        profile.SaveProfile();

        var engine = provider.GetRequiredService<DrawEngine>();
        var result = engine.DrawStudent(1, _ => true);
        var featureAvailability = provider.GetRequiredService<IFeatureAvailabilityService>();
        var changeCount = 0;
        featureAvailability.Changed += (_, _) => changeCount++;

        configHandler.Data.MoreSettings.LotteryEnabled = false;

        Assert.True(result.IsSuccess);
        Assert.False(featureAvailability.IsLotteryEnabled);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void HistoryQueryService_ReadsPersistedRecordsWithoutSwitchingActiveProfiles()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.RollCallSettings.DefaultClass = "history-class";
        config.Data.LotterySettings.DefaultPool = "history-pool";
        config.Save();

        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();
        profile.RecordStudentHistory([student], DateTime.Now, 1);

        var activeStudentList = profile.StudentListConfig;
        var query = provider.GetRequiredService<IHistoryQueryService>();
        var item = Assert.Single(query.GetRecentItems(10));

        Assert.Equal("Lin", item.DisplayName);
        Assert.False(item.IsPrize);
        Assert.Same(activeStudentList, profile.StudentListConfig);
    }

    [Fact]
    public void RollCallSession_UsesFixedMobileFairPolicyAndCommitsBothRecords()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.RollCallSettings.DefaultClass = "session-class";
        config.Data.RollCallSettings.DrawMode = DrawMode.Repeat;
        config.Data.RollCallSettings.DrawType = DrawType.Fair;
        config.Save();

        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Kai", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        var session = provider.GetRequiredService<IRollCallSession>();
        var result = session.DrawOnce();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();

        Assert.True(result.IsSuccess);
        Assert.Equal(student.RecordId, Assert.Single(result.Result).RecordId);
        Assert.Single(profile.CurrentStudentHistory!.Students.Values.Single().Histories);
        var listName = profile.StudentListConfig!.Name;
        Assert.Equal(1, temporary.GetStudentCounts(listName, string.Empty, string.Empty)[student.RecordId.ToString("D")]);
    }

    [Fact]
    public void LotterySession_CountModeUsesInventoryAndCommitsBothRecords()
    {
        using var provider = CreateProvider();
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.LotterySettings.DrawType = LotteryDrawType.Count;
        config.Data.LotterySettings.DrawMode = DrawMode.Repeat;
        config.Save();

        var profile = provider.GetRequiredService<IProfileService>();
        var prize = new Prize { Name = "Notebook", Count = 1, RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.SaveProfile();

        var session = provider.GetRequiredService<ILotterySession>();
        var first = session.DrawOnce();
        var second = session.DrawOnce();
        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();

        Assert.True(first.IsSuccess);
        Assert.Equal(DrawStatus.NoEligibleCandidates, second.Status);
        Assert.Single(profile.CurrentPrizeHistory!.Prizes.Values.Single().Histories);
        var listName = profile.PrizeListConfig!.Name;
        Assert.Equal(1, temporary.GetPrizeCounts(listName)[prize.RecordId.ToString("D")]);
    }

    [Fact]
    public void ProfileCatalogEditor_UsesRecordIdsAndPersistsMutations()
    {
        using var provider = CreateProvider();
        var profile = provider.GetRequiredService<IProfileService>();
        var editor = provider.GetRequiredService<IProfileCatalogEditor>();

        Assert.True(editor.AddStudent("  Mei ", "  07 "));
        var student = Assert.Single(editor.GetStudents());
        var studentId = student.RecordId.ToString("D");
        Assert.Equal("Mei", student.Name);
        Assert.Equal("07", student.Id);
        Assert.True(editor.SetStudentEnabled(studentId, false));
        Assert.False(student.Exists);
        Assert.True(editor.RemoveStudent(studentId));
        Assert.Empty(profile.CurrentStudentList!.Students);

        Assert.False(editor.AddPrize("", ""));
        Assert.True(editor.AddPrize("Book", "P1"));
        var prize = Assert.Single(editor.GetPrizes());
        Assert.True(editor.RemovePrize(prize.RecordId.ToString("D")));
        Assert.Empty(profile.CurrentPrizeList!.Prizes);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}
