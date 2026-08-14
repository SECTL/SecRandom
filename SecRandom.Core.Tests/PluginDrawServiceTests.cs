using System.Reflection;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Verification;
using SecRandom.Services.Draw;
using SecRandom.Services.Linkage;
using SecRandom.Services.Plugins;
using SecRandom.Services.Security;
using SecRandom.Services.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

public sealed class PluginDrawServiceTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "plugin-draw-tests", Guid.NewGuid().ToString("N"));

    public PluginDrawServiceTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public async Task DrawStudents_Unauthorized_ReturnsFailedWithoutDrawing()
    {
        using var provider = CreateProvider(allowAuthorization: false);
        var service = provider.GetRequiredService<IPluginDrawService>();
        var profile = provider.GetRequiredService<IProfileService>();
        profile.CurrentStudentList!.Students.Add(new Student { Name = "Ada", RecordId = Guid.NewGuid() });
        profile.SaveProfile();

        var result = await service.DrawStudentsAsync(
            new PluginStudentDrawRequest("default"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Winners);
    }

    [Fact]
    public async Task DrawStudents_Authorized_DrawsThroughSharedPipeline()
    {
        using var provider = CreateProvider(allowAuthorization: true);
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.RollCallSettings.DrawMode = DrawMode.Repeat;
        config.Data.RollCallSettings.DrawType = DrawType.Random;
        config.Save();

        var profile = provider.GetRequiredService<IProfileService>();
        var student = new Student { Name = "Ada", RecordId = Guid.NewGuid() };
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        var service = provider.GetRequiredService<IPluginDrawService>();
        var result = await service.DrawStudentsAsync(
            new PluginStudentDrawRequest("default", Count: 1),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(student.RecordId, Assert.Single(result.Winners).RecordId);
        Assert.NotEqual(Guid.Empty, result.ProofId);
        Assert.False(string.IsNullOrWhiteSpace(result.DrawRoundId));

        var temporary = provider.GetRequiredService<IDrawTemporaryRecordService>();
        Assert.Equal(1, temporary.GetStudentCounts("default", string.Empty, string.Empty)[student.RecordId.ToString("D")]);
    }

    [Fact]
    public async Task DrawLottery_Authorized_DrawsPrizesAndOptionalAssignments()
    {
        using var provider = CreateProvider(allowAuthorization: true);
        var config = provider.GetRequiredService<MainConfigHandler>();
        config.Data.LotterySettings.DrawMode = DrawMode.Repeat;
        config.Data.LotterySettings.DrawType = LotteryDrawType.Pan;
        config.Save();

        var profile = provider.GetRequiredService<IProfileService>();
        var prize = new Prize { Name = "Book", RecordId = Guid.NewGuid() };
        var student = new Student { Name = "Lin", RecordId = Guid.NewGuid() };
        profile.CurrentPrizeList!.Prizes.Add(prize);
        profile.CurrentStudentList!.Students.Add(student);
        profile.SaveProfile();

        var service = provider.GetRequiredService<IPluginDrawService>();
        var result = await service.DrawLotteryAsync(new PluginLotteryDrawRequest(
            "default", StudentListName: "default", Count: 1), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(prize.RecordId, Assert.Single(result.Winners).RecordId);
        Assert.Equal(student.RecordId, Assert.Single(result.AssignedStudents).RecordId);
        Assert.NotEqual(Guid.Empty, result.PrizeProofId);
        Assert.False(string.IsNullOrWhiteSpace(result.DrawRoundId));
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static ServiceProvider CreateProvider(bool allowAuthorization)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        services.AddSingleton<IVerificationKernel, ManagedVerificationKernel>();
        services.AddSingleton<IWitnessClient>(_ => new StubWitnessClient());
        services.AddSingleton<DrawProofExportService>();
        services.AddSingleton<DrawProofAttestationService>();
        services.AddSingleton<CsesScheduleParser>();
        services.AddSingleton<ICsesScheduleStore, CsesScheduleStore>();
        services.AddSingleton<CsesScheduleSource>();
        services.AddSingleton<ClassIslandScheduleSource>();
        services.AddSingleton<CourseLinkageService>();
        services.AddSingleton<ISecurityService>(_ => new StubSecurityService(allowAuthorization));
        services.AddSingleton<LinkageDrawCoordinator>();
        services.AddTransient<RollCallDrawService>();
        services.AddTransient<LotteryDrawService>();
        services.AddSingleton<IPluginDrawService, PluginDrawService>();
        services.AddSingleton<VerificationDrawCoordinator>();
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

    private sealed class StubWitnessClient : IWitnessClient
    {
        public Task<string> AttestAsync(DrawProof proof, CancellationToken cancellationToken)
        {
            return Task.FromResult("attested");
        }

        public Task<DrawProof> NotarizeAsync(FormalNotarizationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DrawProof());
        }
    }

    private sealed class StubSecurityService(bool allow) : ISecurityService
    {
        public SecuritySettingsUiState GetUiState() => new(false, false, false, false, false, false, false, null);

        public bool RequiresVerification(SecurityOperation operation) => false;

        public Task<SecurityVerificationResult> VerifyAsync(SecurityVerificationResponse response, CancellationToken cancellationToken = default)
            => Task.FromResult(allow ? SecurityVerificationResult.Allowed : new SecurityVerificationResult(false, SecurityVerificationFailure.Cancelled));

        public Task<bool> AuthorizeAsync(SecurityOperation operation, Func<Task> action, CancellationToken cancellationToken = default)
            => AuthorizeAsync([operation], action, cancellationToken);

        public async Task<bool> AuthorizeAsync(IReadOnlyCollection<SecurityOperation> operations, Func<Task> action, CancellationToken cancellationToken = default)
        {
            if (!allow)
                return false;
            await action();
            return true;
        }

        public Task<bool> AuthorizePasswordAsync(TopLevel xamlRoot, Func<Task> action, CancellationToken cancellationToken = default)
            => AuthorizeAsync(SecurityOperation.OpenSettings, action, cancellationToken);

        public Task<SecurityAuthorizationResult> AuthorizeSettingsAsync(Func<Task> action, Func<Task> previewAction, CancellationToken cancellationToken = default)
            => Task.FromResult(new SecurityAuthorizationResult(allow, false));

        public Task<bool> UpdateSecuritySettingsAsync(TopLevel xamlRoot, Action update, CancellationToken cancellationToken = default)
        {
            if (allow)
                update();
            return Task.FromResult(allow);
        }

        public Task<bool> SetPasswordAsync(string password, string? currentPassword = null, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<bool> RemovePasswordAsync(string currentPassword, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<string?> BeginTotpSetupAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> BeginTotpSetupAsync(TopLevel xamlRoot, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task CancelTotpSetupAsync(string secret, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ConfirmTotpAsync(string secret, string code, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<IReadOnlyList<UsbBindingInfo>> GetUsbBindingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UsbBindingInfo>>([]);

        public Task<IReadOnlyList<UsbDeviceInfo>> GetUsbDevicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UsbDeviceInfo>>([]);

        public Task<bool> BindUsbAsync(string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<bool> BindUsbAsync(TopLevel xamlRoot, string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<bool> UnbindUsbAsync(string bindingId, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<bool> UnbindUsbAsync(TopLevel xamlRoot, string bindingId, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public bool TryUpdateSettings(Action update)
        {
            if (allow)
                update();
            return allow;
        }
    }
}
