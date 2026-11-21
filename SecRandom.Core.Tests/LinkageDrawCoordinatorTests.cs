using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Linkage;
using SecRandom.Services.Security;

namespace SecRandom.Core.Tests;

/// <summary>
/// 联动闸门在「允许验证后继续」下的拒绝语义。
/// <para>
/// 该开关只承诺"验证之后可以继续"。当宿主没有可用的验证手段（安全保护未启用、
/// 没有任何已配置的验证方式）时，SecurityService 对 BypassClassTimeRestriction 也返回
/// "无需验证"并直接执行动作；闸门若原样转交，课间禁令与"数据源不可用时维持限制"
/// 就会整体 fail-open —— 开关看着是开的，抽取却照常发生。
/// </para>
/// </summary>
public sealed class LinkageDrawCoordinatorTests
{
    [Fact]
    public async Task StrictRestriction_WithoutConfiguredVerification_BlocksDraw()
    {
        var harness = new Harness(restrictWhenUnavailable: true, verificationAvailable: false);

        var executed = false;
        var authorized = await harness.Coordinator.AuthorizeAsync(
            SecurityOperation.RollCallStart, () => { executed = true; return Task.CompletedTask; });

        Assert.False(authorized);
        Assert.False(executed);
        Assert.Equal(LinkageDrawGate.BlockedByClassTime, harness.Coordinator.EvaluateGate(SecurityOperation.RollCallStart));
    }

    [Fact]
    public async Task StrictRestriction_WithConfiguredVerification_RunsTheAction()
    {
        var harness = new Harness(restrictWhenUnavailable: true, verificationAvailable: true);

        var executed = false;
        var authorized = await harness.Coordinator.AuthorizeAsync(
            SecurityOperation.RollCallStart, () => { executed = true; return Task.CompletedTask; });

        Assert.True(authorized);
        Assert.True(executed);
        Assert.Equal(LinkageDrawGate.RequiresLocalVerification, harness.Coordinator.EvaluateGate(SecurityOperation.RollCallStart));
    }

    [Fact]
    public async Task UnavailableSource_WithoutStrictRestriction_StaysAllowed()
    {
        var harness = new Harness(restrictWhenUnavailable: false, verificationAvailable: false);

        var executed = false;
        var authorized = await harness.Coordinator.AuthorizeAsync(
            SecurityOperation.RollCallStart, () => { executed = true; return Task.CompletedTask; });

        Assert.True(authorized);
        Assert.True(executed);
        Assert.False(harness.Linkage.IsClassTimeRestrictionActive);
        Assert.Equal(LinkageDrawGate.Allowed, harness.Coordinator.EvaluateGate(SecurityOperation.RollCallStart));
    }

    [Fact]
    public async Task VerificationDisabled_BlocksEvenWhenVerificationIsConfigured()
    {
        var harness = new Harness(
            restrictWhenUnavailable: true,
            verificationAvailable: true,
            verificationRequired: false);

        var executed = false;
        var authorized = await harness.Coordinator.AuthorizeAsync(
            SecurityOperation.RollCallStart, () => { executed = true; return Task.CompletedTask; });

        Assert.False(authorized);
        Assert.False(executed);
        Assert.Equal(LinkageDrawGate.BlockedByClassTime, harness.Coordinator.EvaluateGate(SecurityOperation.RollCallStart));
    }

    private sealed class Harness
    {
        public Harness(
            bool restrictWhenUnavailable,
            bool verificationAvailable,
            bool verificationRequired = true)
        {
            var config = new MainConfigModel();
            config.LinkageSettings.DataSource = LinkageDataSource.Cses;
            config.LinkageSettings.InstantDrawDisable = true;
            config.LinkageSettings.RestrictWhenUnavailable = restrictWhenUnavailable;
            config.LinkageSettings.VerificationRequired = verificationRequired;

            var handler = new MainConfigHandler(
                NullLogger<MainConfigHandler>.Instance,
                new TestConfigService(config));
            var store = new MissingCsesScheduleStore();
            var connection = new ClassIslandIpcConnection(NullLogger<ClassIslandIpcConnection>.Instance);
            var source = new ClassIslandScheduleSource(connection, NullLogger<ClassIslandScheduleSource>.Instance);
            Linkage = new CourseLinkageService(
                handler,
                store,
                new CsesScheduleSource(store),
                source,
                NullLogger<CourseLinkageService>.Instance);
            Coordinator = new LinkageDrawCoordinator(
                Linkage,
                new StubSecurityService(verificationAvailable),
                new UnlockedControlDrawGate());
            // CSES 数据源已选择但课表缺失 —— 正是"数据源不可用"的场景。
            Linkage.RefreshAsync().GetAwaiter().GetResult();
        }

        public CourseLinkageService Linkage { get; }
        public LinkageDrawCoordinator Coordinator { get; }
    }

    private sealed class StubSecurityService(bool verificationAvailable) : ISecurityService
    {
        public SecuritySettingsUiState GetUiState() => new(false, false, false, false, false, false, false, null);

        public bool RequiresVerification(SecurityOperation operation) => verificationAvailable;

        public Task<SecurityVerificationResult> VerifyAsync(
            SecurityVerificationResponse response,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SecurityVerificationResult.Allowed);

        public Task<bool> AuthorizeAsync(
            SecurityOperation operation,
            Func<Task> action,
            CancellationToken cancellationToken = default)
            => AuthorizeAsync([operation], action, cancellationToken);

        public async Task<bool> AuthorizeAsync(
            IReadOnlyCollection<SecurityOperation> operations,
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            await action();
            return true;
        }

        public Task<bool> AuthorizePasswordAsync(
            Avalonia.Controls.TopLevel xamlRoot,
            Func<Task> action,
            CancellationToken cancellationToken = default)
            => AuthorizeAsync(SecurityOperation.OpenSettings, action, cancellationToken);

        public Task<SecurityAuthorizationResult> AuthorizeSettingsAsync(
            Func<Task> action,
            Func<Task> previewAction,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SecurityAuthorizationResult(true));

        public Task<bool> UpdateSecuritySettingsAsync(
            Avalonia.Controls.TopLevel xamlRoot,
            Action update,
            CancellationToken cancellationToken = default)
        {
            update();
            return Task.FromResult(true);
        }

        public Task<bool> SetPasswordAsync(string password, string? currentPassword = null,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> RemovePasswordAsync(string currentPassword, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> BeginTotpSetupAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> BeginTotpSetupAsync(Avalonia.Controls.TopLevel xamlRoot,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task CancelTotpSetupAsync(string secret, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ConfirmTotpAsync(string secret, string code, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> RemoveTotpAsync(Avalonia.Controls.TopLevel xamlRoot,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<UsbBindingInfo>> GetUsbBindingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UsbBindingInfo>>([]);

        public Task<IReadOnlyList<UsbDeviceInfo>> GetUsbDevicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UsbDeviceInfo>>([]);

        public Task<bool> BindUsbAsync(string deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> BindUsbAsync(Avalonia.Controls.TopLevel xamlRoot, string deviceId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> UnbindUsbAsync(string bindingId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> UnbindUsbAsync(Avalonia.Controls.TopLevel xamlRoot, string bindingId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public bool TryUpdateSettings(Action update)
        {
            update();
            return true;
        }

        public Task<bool> AuthorizeArchiveImportAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public bool IsSudoModeActive() => false;

        public bool IsGlobalSudoModeActive() => false;

        public void DeactivateGlobalSudoMode() { }

        public void DeactivateSudoMode() { }

        public void DeactivateSettingsSudoMode() { }

        public event Action? SudoModeChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class MissingCsesScheduleStore : ICsesScheduleStore
    {
        public string SchedulePath => "unused";
        public event EventHandler? ScheduleChanged { add { } remove { } }
        public CsesSchedule? Load() => null;

        public Task<CsesSchedule> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Clear() { }
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}