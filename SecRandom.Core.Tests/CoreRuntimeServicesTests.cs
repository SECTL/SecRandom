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
