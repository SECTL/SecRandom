using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Platforms.Abstractions;
using IntegrityAction = SecRandom.Core.Enums.Configs.SettingsIntegrityAction;

namespace SecRandom.Services.Security;

internal sealed class SecurityService(
    SecuritySettingsStore settingsStore,
    SecurityCredentialStore credentialStore,
    ISecurityVerificationPrompt prompt,
    IUsbDeviceCatalog usbDeviceCatalog,
    ILogger<SecurityService> logger,
    IRemovableStorageBindingMarker? bindingMarker = null,
    TimeProvider? timeProvider = null) : ISecurityService
{
    private const int LockoutFailureLimit = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IRemovableStorageBindingMarker _bindingMarker =
        bindingMarker ?? PortableRemovableStorageBindingMarker.Instance;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _authorizationGate = new(1, 1);
    private string? _pendingTotpSecret;
    private SecurityCredentialContext? _pendingTotpContext;
    private DateTimeOffset? _sudoModeExpirationUtc;
    private DateTimeOffset? _settingsSudoExpirationUtc;

    // 放宽防护的改动不接受「打开设置」时那一次验证，必须有一次专门为它发生的密码验证；
    // 该验证之后同样按 SudoModeDurationSeconds 计时，避免连续调整时反复弹窗
    private DateTimeOffset? _protectionDowngradeAuthorizationUtc;

    public event Action? SudoModeChanged;

    private SecuritySettingsConfig Settings => settingsStore.Data;

    /// <summary>
    ///     安全验证对话框的宿主根窗口。宿主尚未建立时（单元测试直接驱动本服务）
    ///     没有可用的 <see cref="App" /> 实例，此时返回 null 而不是抛出异常。
    /// </summary>
    private static TopLevel VerificationRootWindow => App.Current?.GetRootWindow()!;

    public SecuritySettingsUiState GetUiState()
    {
        lock (_gate)
        {
            var metadata = credentialStore.LoadMetadata();
            var remaining = GetLockoutRemaining(metadata.LockedUntilUtc);
            var hasPassword = metadata.Password is not null;
            var hasTotp = metadata.HasTotp;
            var hasUsb = metadata.UsbBindings.Any(IsBindingPresent);
            return new SecuritySettingsUiState(
                hasPassword,
                hasTotp,
                hasUsb,
                Settings.SecurityEnabled,
                hasPassword,
                hasPassword && remaining is null,
                Settings.SecurityEnabled && hasPassword && remaining is null,
                remaining);
        }
    }

    public bool RequiresVerification(SecurityOperation operation)
    {
        if (!Settings.SecurityEnabled)
            return false;

        // 这两项不接受任何免验证状态：放宽安全设置本身要重新验证，
        // 「上课时间禁止抽取」的例外验证也不能被一段 Sudo 窗口覆盖
        if (operation is SecurityOperation.ChangeSecuritySettings or SecurityOperation.BypassClassTimeRestriction)
            return true;

        // Check the appropriate sudo mode based on operation
        if (operation == SecurityOperation.OpenSettings)
        {
            if (IsSettingsSudoModeActive())
                return false;
        }
        else
        {
            // 这里刻意不调用 IsGlobalSudoModeActive：它会触发 SudoModeChanged，
            // 而本方法可能从非 UI 线程被调用，托盘菜单项的刷新必须留在 UI 线程
            if (Settings.SudoModeEnabled &&
                _sudoModeExpirationUtc is { } expiration && _timeProvider.GetUtcNow() < expiration)
                return false;
        }

        var metadata = credentialStore.LoadMetadata();
        if (!metadata.IsReadable)
            return true;
        if (GetRequiredFactors(metadata).Count == 0)
            return false;

        return operation switch
        {
            SecurityOperation.OpenSettings => Settings.ProtectOpenSettings,
            SecurityOperation.ToggleMainWindow => Settings.ProtectToggleMainWindow,
            SecurityOperation.ToggleFloatingWindow => Settings.ProtectToggleFloatingWindow,
            SecurityOperation.RestartApplication => Settings.ProtectRestart,
            SecurityOperation.ExitApplication => Settings.ProtectExit,
            SecurityOperation.RollCallStart => Settings.ProtectRollCallStart,
            SecurityOperation.RollCallReset => Settings.ProtectRollCallReset,
            SecurityOperation.QuickDrawStart => Settings.ProtectQuickDrawStart,
            SecurityOperation.QuickDrawReset => Settings.ProtectQuickDrawReset,
            SecurityOperation.LotteryStart => Settings.ProtectLotteryStart,
            SecurityOperation.LotteryReset => Settings.ProtectLotteryReset,
            SecurityOperation.LinkageAction => Settings.ProtectLinkage,
            SecurityOperation.BypassClassTimeRestriction => true,
            SecurityOperation.ChangeSecuritySettings => true,
            _ => false
        };
    }

    public async Task<bool> AuthorizeAsync(
        SecurityOperation operation,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        return await AuthorizeAsync([operation], action, cancellationToken);
    }

    public async Task<bool> AuthorizeAsync(
        IReadOnlyCollection<SecurityOperation> operations,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        if (!RequiresVerification(operations))
        {
            await action();
            return true;
        }

        await _authorizationGate.WaitAsync(cancellationToken);
        try
        {
            if (!RequiresVerification(operations))
            {
                await action();
                return true;
            }

            var metadata = credentialStore.LoadMetadata();
            var request = new SecurityVerificationRequest(
                GetRequiredFactors(metadata),
                Settings.RequireAllSelectedFactors,
                GetLockoutRemaining(metadata.LockedUntilUtc),
                TotpStandaloneReady: credentialStore.LoadStandaloneTotp() is not null);
            var result = await prompt.RequestAsync(VerificationRootWindow, request, VerifyAsync, cancellationToken);
            if (!result.IsAuthorized)
            {
                logger.LogInformation("Security authorization rejected for {Operations}: {Failure}", string.Join(',', operations), result.Failure);
                return false;
            }

            ActivateGlobalSudoMode();

            await action();
            return true;
        }
        finally
        {
            _authorizationGate.Release();
        }
    }

    private bool RequiresVerification(IReadOnlyCollection<SecurityOperation> operations)
    {
        return operations.Any(RequiresVerification);
    }

    public Task<bool> AuthorizePasswordAsync(
        TopLevel xamlRoot,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        return AuthorizePasswordCoreAsync(
            xamlRoot,
            async _ =>
            {
                await action();
                return false;
            },
            cancellationToken);
    }

    public async Task<SecurityAuthorizationResult> AuthorizeSettingsAsync(
        Func<Task> action,
        Func<Task> previewAction,
        CancellationToken cancellationToken = default)
    {
        if (!RequiresVerification(SecurityOperation.OpenSettings))
        {
            await action();
            return new SecurityAuthorizationResult(true);
        }

        await _authorizationGate.WaitAsync(cancellationToken);
        try
        {
            if (!RequiresVerification(SecurityOperation.OpenSettings))
            {
                await action();
                return new SecurityAuthorizationResult(true);
            }

            var metadata = credentialStore.LoadMetadata();
            var request = new SecurityVerificationRequest(
                GetRequiredFactors(metadata),
                Settings.RequireAllSelectedFactors,
                GetLockoutRemaining(metadata.LockedUntilUtc),
                Settings.AllowSettingsPreview,
                TotpStandaloneReady: credentialStore.LoadStandaloneTotp() is not null);
            var result = await prompt.RequestAsync(VerificationRootWindow, request, VerifyAsync, cancellationToken);
            if (result.Failure == SecurityVerificationFailure.PreviewRequested && request.AllowPreview)
            {
                await previewAction();
                return new SecurityAuthorizationResult(false, true);
            }
            if (!result.IsAuthorized)
            {
                logger.LogInformation("Security settings authorization rejected: {Failure}", result.Failure);
                return new SecurityAuthorizationResult(false);
            }

            ActivateSettingsSudoMode();

            await action();
            return new SecurityAuthorizationResult(true);
        }
        finally
        {
            _authorizationGate.Release();
        }
    }

    public Task<bool> UpdateSecuritySettingsAsync(
        TopLevel xamlRoot,
        Action update,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (IsSettingsSudoModeActive())
            {
                var snapshot = SecuritySettingsSnapshot.Capture(Settings);
                update();
                NormalizeSettings(credentialStore.LoadMetadata());
                if (!snapshot.IsProtectionDowngrade(SecuritySettingsSnapshot.Capture(Settings)) ||
                    IsProtectionDowngradeAuthorized())
                {
                    settingsStore.Save();
                    return Task.FromResult(true);
                }

                // 放宽防护的改动不能靠设置页 sudo 免验证：先撤回这次试改，再走密码验证
                snapshot.Restore(Settings);
            }
        }

        return AuthorizePasswordCoreAsync(xamlRoot, context =>
        {
            lock (_gate)
            {
                var snapshot = SecuritySettingsSnapshot.Capture(Settings);
                var requireAllBefore = Settings.RequireAllSelectedFactors;
                update();
                NormalizeSettings(context.Credentials);
                if (requireAllBefore != Settings.RequireAllSelectedFactors && !TrySaveCredentials(context))
                {
                    // 「任意已选验证方式」模式需要 TOTP 免密副本；副本写入失败时回滚模式，
                    // 而不是留下一个无法在磁盘上执行的配置
                    Settings.RequireAllSelectedFactors = requireAllBefore;
                }

                if (snapshot.IsProtectionDowngrade(SecuritySettingsSnapshot.Capture(Settings)))
                    _protectionDowngradeAuthorizationUtc = _timeProvider.GetUtcNow();

                settingsStore.Save();
            }

            ActivateSettingsSudoMode();
            return Task.FromResult(false);
        }, cancellationToken);
    }

    public Task<SecurityVerificationResult> VerifyAsync(
        SecurityVerificationResponse response,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (response.PreviewRequested)
                return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.PreviewRequested));
            if (response.Cancelled)
                return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.Cancelled));

            var metadata = credentialStore.LoadMetadata();
            var remaining = GetLockoutRemaining(metadata.LockedUntilUtc);
            if (remaining is not null)
                return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.LockedOut, remaining));

            var factors = GetRequiredFactors(metadata);
            if (factors.Count == 0)
                return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.NotConfigured));

            var usbPassed = factors.Contains(SecurityFactor.Usb) &&
                            response.UsbPresent &&
                            metadata.UsbBindings.Any(IsBindingPresent);
            // 「任意已选验证方式」模式下 TOTP 也能单独通过：该模式会用凭据文件旁的
            // 免密副本校验验证码，因此不需要先解开信封
            var totpPassed = factors.Contains(SecurityFactor.Totp) &&
                             credentialStore.LoadStandaloneTotp() is { } standaloneTotp &&
                             TotpService.Verify(standaloneTotp, response.TotpCode, _timeProvider.GetUtcNow());
            if (!Settings.RequireAllSelectedFactors && (usbPassed || totpPassed))
            {
                metadata.FailedAttempts = 0;
                metadata.LockedUntilUtc = null;
                if (!TrySaveMetadata(metadata))
                    return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.FactorUnavailable));
                return Task.FromResult(SecurityVerificationResult.Allowed);
            }

            if (!Settings.RequireAllSelectedFactors && string.IsNullOrEmpty(response.Password))
            {
                // 没有提交主密码，说明这是一次免密尝试（例如验证码错误）。它无法再借助
                // 信封解锁通过，但仍要计入失败次数，以便限制验证码暴破
                metadata.FailedAttempts++;
                if (metadata.FailedAttempts >= LockoutFailureLimit)
                    metadata.LockedUntilUtc = _timeProvider.GetUtcNow().Add(LockoutDuration);
                if (!TrySaveMetadata(metadata))
                    return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.FactorUnavailable));
                return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.InvalidCredentials,
                    GetLockoutRemaining(metadata.LockedUntilUtc)));
            }

            var unlockResult = credentialStore.TryUnlock(response.Password, out var context);
            if (unlockResult != CredentialUnlockResult.Succeeded || context is null)
                return Task.FromResult(HandleUnlockFailure(metadata, unlockResult));

            using (context)
            {
                var credentials = context.Credentials;
                var factorStates = factors.Select(factor => factor switch
                {
                    SecurityFactor.Password => true,
                    SecurityFactor.Usb => usbPassed,
                    _ => VerifyFactor(factor, credentials, response)
                });
                var isAuthorized = Settings.RequireAllSelectedFactors
                    ? factorStates.All(value => value)
                    : factorStates.Any(value => value);
                if (isAuthorized)
                {
                    credentials.FailedAttempts = 0;
                    credentials.LockedUntilUtc = null;
                    if (!TrySaveCredentials(context))
                        return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.FactorUnavailable));
                    return Task.FromResult(SecurityVerificationResult.Allowed);
                }

                credentials.FailedAttempts++;
                if (credentials.FailedAttempts >= LockoutFailureLimit)
                    credentials.LockedUntilUtc = _timeProvider.GetUtcNow().Add(LockoutDuration);
                if (!TrySaveCredentials(context))
                    return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.FactorUnavailable));
                remaining = GetLockoutRemaining(credentials.LockedUntilUtc);
                return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.InvalidCredentials, remaining));
            }
        }
    }

    private async Task<bool> AuthorizePasswordCoreAsync(
        TopLevel xamlRoot,
        Func<SecurityCredentialContext, Task<bool>> action,
        CancellationToken cancellationToken)
    {
        var retainContext = false;
        SecurityCredentialContext? context = null;
        await _authorizationGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                var metadata = credentialStore.LoadMetadata();
                if (metadata.Password is null)
                    return false;
                var remaining = GetLockoutRemaining(metadata.LockedUntilUtc);
                if (remaining is not null)
                    return false;
            }

            var request = new SecurityVerificationRequest(
                [SecurityFactor.Password],
                RequireAllSelectedFactors: true,
                GetLockoutRemaining(credentialStore.LoadMetadata().LockedUntilUtc));

            Task<SecurityVerificationResult> VerifyPasswordAsync(
                SecurityVerificationResponse response, CancellationToken ct)
            {
                lock (_gate)
                {
                    var metadata = credentialStore.LoadMetadata();
                    var remaining = GetLockoutRemaining(metadata.LockedUntilUtc);
                    if (remaining is not null)
                        return Task.FromResult(
                            new SecurityVerificationResult(false, SecurityVerificationFailure.LockedOut, remaining));

                    var unlockResult = credentialStore.TryUnlock(response.Password, out var unlocked);
                    if (unlockResult != CredentialUnlockResult.Succeeded || unlocked is null)
                    {
                        logger.LogInformation(
                            "Password authorization rejected: {Failure}",
                            HandleUnlockFailure(metadata, unlockResult).Failure);
                        return Task.FromResult(new SecurityVerificationResult(
                            false,
                            SecurityVerificationFailure.InvalidCredentials,
                            GetLockoutRemaining(metadata.LockedUntilUtc)));
                    }

                    unlocked.Credentials.FailedAttempts = 0;
                    unlocked.Credentials.LockedUntilUtc = null;
                    if (!TrySaveCredentials(unlocked))
                    {
                        unlocked.Dispose();
                        return Task.FromResult(
                            new SecurityVerificationResult(false, SecurityVerificationFailure.FactorUnavailable));
                    }

                    context = unlocked;
                    return Task.FromResult(SecurityVerificationResult.Allowed);
                }
            }

            var result = await prompt.RequestAsync(xamlRoot, request, VerifyPasswordAsync, cancellationToken);
            if (!result.IsAuthorized || context is null)
                return false;

            retainContext = await action(context);
            return true;
        }
        finally
        {
            if (!retainContext)
                context?.Dispose();
            _authorizationGate.Release();
        }
    }

    public Task<bool> SetPasswordAsync(string password, string? currentPassword = null, CancellationToken cancellationToken = default)
    {
        if (password.Length < 6)
            return Task.FromResult(false);

        lock (_gate)
        {
            var metadata = credentialStore.LoadMetadata();
            if (metadata.Password is null)
            {
                using var created = credentialStore.Create(password);
                if (!TrySaveCredentials(created))
                    return Task.FromResult(false);
            }
            else
            {
                if (GetLockoutRemaining(metadata.LockedUntilUtc) is not null)
                    return Task.FromResult(false);
                var unlockResult = credentialStore.TryUnlock(currentPassword ?? string.Empty, out var context);
                if (unlockResult != CredentialUnlockResult.Succeeded || context is null)
                {
                    HandleUnlockFailure(metadata, unlockResult);
                    return Task.FromResult(false);
                }

                using (context)
                {
                    credentialStore.Rekey(context, password);
                    context.Credentials.FailedAttempts = 0;
                    context.Credentials.LockedUntilUtc = null;
                    if (!TrySaveCredentials(context))
                        return Task.FromResult(false);
                }
            }

            Settings.PasswordEnabled = true;
            _pendingTotpContext?.Dispose();
            _pendingTotpContext = null;
            _pendingTotpSecret = null;
            settingsStore.Save();
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemovePasswordAsync(string currentPassword, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var metadata = credentialStore.LoadMetadata();
            if (GetLockoutRemaining(metadata.LockedUntilUtc) is not null)
                return Task.FromResult(false);
            var unlockResult = credentialStore.TryUnlock(currentPassword, out var context);
            if (unlockResult != CredentialUnlockResult.Succeeded || context is null)
            {
                HandleUnlockFailure(metadata, unlockResult);
                return Task.FromResult(false);
            }

            using (context)
            {
                var credentials = context.Credentials;
                var bindings = credentials.UsbBindings.ToArray();
                try
                {
                    credentialStore.Delete();
                }
                catch (CryptographicException exception)
                {
                    logger.LogWarning(exception, "Security credential persistence is unavailable.");
                    return Task.FromResult(false);
                }
                catch (IOException exception)
                {
                    logger.LogWarning(exception, "Security credential persistence failed.");
                    return Task.FromResult(false);
                }
                catch (UnauthorizedAccessException exception)
                {
                    logger.LogWarning(exception, "Security credential persistence was denied.");
                    return Task.FromResult(false);
                }

                foreach (var binding in bindings)
                    TryDeleteUsbKey(binding);
            }

            // 凭据文件已删除，免密 TOTP 副本必须一并移除，避免可读种子继续留在磁盘上
            DiscardStandaloneTotp();
            _pendingTotpContext?.Dispose();
            _pendingTotpContext = null;
            _pendingTotpSecret = null;
            Settings.SecurityEnabled = false;
            Settings.PasswordEnabled = false;
            Settings.TotpEnabled = false;
            Settings.UsbBindingEnabled = false;
            DisableOperationProtections();
            settingsStore.Save();
            return Task.FromResult(true);
        }
    }

    public Task<string?> BeginTotpSetupAsync(CancellationToken cancellationToken = default)
    {
        return BeginTotpSetupAsync(VerificationRootWindow, cancellationToken);
    }

    public async Task<string?> BeginTotpSetupAsync(TopLevel xamlRoot, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _pendingTotpSecret = null;
            _pendingTotpContext?.Dispose();
            _pendingTotpContext = null;
        }

        string? secret = null;
        var authorized = await AuthorizePasswordCoreAsync(xamlRoot, context =>
        {
            lock (_gate)
            {
                secret = TotpService.GenerateSecret();
                _pendingTotpSecret = secret;
                _pendingTotpContext = context;
            }

            return Task.FromResult(true);
        }, cancellationToken);
        return authorized ? secret : null;
    }

    public Task<bool> ConfirmTotpAsync(string secret, string code, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!string.Equals(_pendingTotpSecret, secret, StringComparison.Ordinal) || _pendingTotpContext is null)
                return Task.FromResult(false);

            if (!TotpService.Verify(secret, code, _timeProvider.GetUtcNow()))
                return Task.FromResult(false);

            var context = _pendingTotpContext;
            var credentials = context.Credentials;
            if (credentials.Password is null)
                return Task.FromResult(false);
            credentials.TotpSecret = secret;
            if (!TrySaveCredentials(context))
                return Task.FromResult(false);
            _pendingTotpSecret = null;
            _pendingTotpContext = null;
            context.Dispose();
            return Task.FromResult(true);
        }
    }

    public Task CancelTotpSetupAsync(string secret, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (string.Equals(_pendingTotpSecret, secret, StringComparison.Ordinal))
            {
                _pendingTotpSecret = null;
                _pendingTotpContext?.Dispose();
                _pendingTotpContext = null;
            }
            return Task.CompletedTask;
        }
    }

    public async Task<bool> RemoveTotpAsync(TopLevel xamlRoot, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // 没有配置 TOTP 就不用弹密码，也不该白扣一次锁定计数
            if (!credentialStore.LoadMetadata().HasTotp)
                return false;
        }

        var removed = false;
        await AuthorizePasswordCoreAsync(xamlRoot, context =>
        {
            lock (_gate)
                removed = RemoveTotpCore(context);
            return Task.FromResult(false);
        }, cancellationToken);
        return removed;
    }

    private bool RemoveTotpCore(SecurityCredentialContext context)
    {
        lock (_gate)
        {
            var credentials = context.Credentials;
            if (string.IsNullOrWhiteSpace(credentials.TotpSecret))
                return false;

            var previousSecret = credentials.TotpSecret;
            credentials.TotpSecret = null;
            // TrySaveCredentials 在种子为空时同步删除免密 TOTP 副本，失败时连副本一起清掉
            if (!TrySaveCredentials(context))
            {
                credentials.TotpSecret = previousSecret;
                return false;
            }

            // 移除后任何尚未确认的待定设置都不该再被确认，否则会立刻把 TOTP 重新装回去
            _pendingTotpSecret = null;
            if (!ReferenceEquals(_pendingTotpContext, context))
            {
                _pendingTotpContext?.Dispose();
                _pendingTotpContext = null;
            }

            Settings.TotpEnabled = false;
            NormalizeSettings(credentials);
            settingsStore.Save();
            return true;
        }
    }

    public Task<IReadOnlyList<UsbBindingInfo>> GetUsbBindingsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var bindings = credentialStore.LoadMetadata().UsbBindings
                .Select(binding => new UsbBindingInfo(binding.Id, binding.DisplayName, IsBindingPresent(binding)))
                .ToList();
            return Task.FromResult<IReadOnlyList<UsbBindingInfo>>(bindings);
        }
    }

    public Task<IReadOnlyList<UsbDeviceInfo>> GetUsbDevicesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var metadata = credentialStore.LoadMetadata();
            var drives = usbDeviceCatalog.GetRemovableDevices();
            var devices = new List<UsbDeviceInfo>(drives.Count + metadata.UsbBindings.Count);
            foreach (var drive in drives)
            {
                // A stable device ID alone is not a binding. The persisted
                // record and its volume marker must agree before projection.
                var binding = metadata.UsbBindings.FirstOrDefault(item =>
                    BindingMatchesDevice(item, drive) && IsBindingPresent(item, drive));
                devices.Add(new UsbDeviceInfo(
                    drive.DriveLetter,
                    drive.DisplayName,
                    drive.DeviceId,
                    binding is not null,
                    binding?.Id,
                    IsPresent: true)
                {
                    HardwareName = drive.HardwareName
                });
            }

            foreach (var binding in metadata.UsbBindings.Where(binding => drives.All(drive => !BindingMatchesDevice(binding, drive))))
            {
                devices.Add(new UsbDeviceInfo(
                    string.Empty,
                    binding.DisplayName,
                    binding.DeviceId ?? $"unknown:{binding.Id}",
                    IsBound: !binding.MarkerCleanupPending && IsBindingPresent(binding),
                    binding.Id,
                    IsBindingPresent(binding)));
            }

            return Task.FromResult<IReadOnlyList<UsbDeviceInfo>>(devices);
        }
    }

    public Task<bool> BindUsbAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        return BindUsbAsync(VerificationRootWindow, deviceId, cancellationToken);
    }

    public async Task<bool> BindUsbAsync(TopLevel xamlRoot, string deviceId, CancellationToken cancellationToken = default)
    {
        var bound = false;
        var authorized = await AuthorizePasswordCoreAsync(xamlRoot, context =>
        {
            lock (_gate)
                bound = BindUsbCore(context, deviceId);
            return Task.FromResult(false);
        }, cancellationToken);
        return authorized && bound;
    }

    private bool BindUsbCore(SecurityCredentialContext context, string deviceId)
    {
        lock (_gate)
        {
            var credentials = context.Credentials;
            if (credentials.Password is null)
                return false;

            var drive = FindDevice(deviceId);
            if (drive is null)
                return false;

            var matchingBindings = credentials.UsbBindings
                .Where(item => BindingMatchesDevice(item, drive))
                .ToArray();
            if (matchingBindings.Any(item => !item.MarkerCleanupPending && IsBindingPresent(item, drive)))
                return false;

            var pendingBinding = matchingBindings.FirstOrDefault(item => item.MarkerCleanupPending);

            // A record without its matching marker is stale. Remove it while
            // re-binding this same physical volume so the record area cannot
            // accumulate duplicate identities.
            foreach (var staleBinding in matchingBindings)
                credentials.UsbBindings.Remove(staleBinding);

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            if (!TryWriteUsbKey(drive.RootPath, token, pendingBinding?.TokenHash))
                return false;

            var binding = new UsbBindingCredential
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = drive.DeviceId,
                TokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(token))),
                DisplayName = drive.DisplayName
            };
            credentials.UsbBindings.Add(binding);
            if (TrySaveCredentials(context))
            {
                return true;
            }

            credentials.UsbBindings.Remove(binding);
            credentials.UsbBindings.AddRange(matchingBindings);
            TryDeleteUsbKeyAtPath(drive.RootPath, binding.TokenHash);
            return false;
        }
    }

    public Task<bool> UnbindUsbAsync(string bindingId, CancellationToken cancellationToken = default)
    {
        return UnbindUsbAsync(VerificationRootWindow, bindingId, cancellationToken);
    }

    public async Task<bool> UnbindUsbAsync(TopLevel xamlRoot, string bindingId, CancellationToken cancellationToken = default)
    {
        var unbound = false;
        var authorized = await AuthorizePasswordCoreAsync(xamlRoot, context =>
        {
            lock (_gate)
                unbound = UnbindUsbCore(context, bindingId);
            return Task.FromResult(false);
        }, cancellationToken);
        return authorized && unbound;
    }

    private bool UnbindUsbCore(SecurityCredentialContext context, string bindingId)
    {
        lock (_gate)
        {
            var credentials = context.Credentials;
            var binding = credentials.UsbBindings.FirstOrDefault(item => item.Id == bindingId);
            if (binding is null)
                return false;

            var rootPath = ResolveBindingRootPath(binding);
            var markerCleanupPending = rootPath is null;
            if (markerCleanupPending)
                binding.MarkerCleanupPending = true;
            else
                credentials.UsbBindings.Remove(binding);

            if (!TrySaveCredentials(context))
            {
                if (markerCleanupPending)
                    binding.MarkerCleanupPending = false;
                else
                    credentials.UsbBindings.Add(binding);
                return false;
            }

            if (rootPath is not null)
                TryDeleteUsbKeyAtPath(rootPath);
            if (!HasActiveUsbBindings(credentials.UsbBindings))
                Settings.UsbBindingEnabled = false;
            NormalizeSettings(credentials);
            settingsStore.Save();
            return true;
        }
    }

    public bool TryUpdateSettings(Action update)
    {
        lock (_gate)
        {
            if (Settings.SecurityEnabled)
                return false;

            var snapshot = SecuritySettingsSnapshot.Capture(Settings);
            var metadata = credentialStore.LoadMetadata();
            try
            {
                update();
                if (Settings.SecurityEnabled)
                {
                    snapshot.Restore(Settings);
                    return false;
                }

                NormalizeSettings(metadata);
                settingsStore.Save();
                return true;
            }
            catch
            {
                snapshot.Restore(Settings);
                throw;
            }
        }
    }

    /// <summary>
    ///     导入/恢复前的一次新鲜验证。安全设置已经不随归档走了，但"导入一份备份"仍然是能重写这台
    ///     机器配置的操作，所以保护开着时它照样要过一次验证。这里刻意不看任何 Sudo 状态：一次导入
    ///     就是一次整体替换，值得为此单独确认一次。调用方必须已经处于能弹出验证对话框的线程上，
    ///     宿主由 <c>SecurityArchivePreImportGuard</c> 负责把归档线程切回 UI 线程。
    /// </summary>
    public async Task<bool> AuthorizeArchiveImportAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!Settings.SecurityEnabled)
                return true;

            var metadata = credentialStore.LoadMetadata();
            if (!metadata.IsReadable || GetRequiredFactors(metadata).Count == 0)
                return true;
        }

        // 调用方负责已经处于能弹出验证对话框的线程上（见 SecurityArchivePreImportGuard）
        return await AuthorizePasswordAsync(VerificationRootWindow, () => Task.CompletedTask, cancellationToken);
    }

    private List<SecurityFactor> GetRequiredFactors(SecurityCredentialMetadata metadata)
    {
        var factors = new List<SecurityFactor>();
        if (metadata.Password is not null)
            factors.Add(SecurityFactor.Password);
        if (Settings.TotpEnabled && metadata.HasTotp)
            factors.Add(SecurityFactor.Totp);
        if (Settings.UsbBindingEnabled && HasActiveUsbBindings(metadata.UsbBindings))
            factors.Add(SecurityFactor.Usb);
        return factors;
    }

    private List<SecurityFactor> GetRequiredFactors(SecurityCredentials credentials)
    {
        var factors = new List<SecurityFactor>();
        if (credentials.Password is not null)
            factors.Add(SecurityFactor.Password);
        if (Settings.TotpEnabled && !string.IsNullOrWhiteSpace(credentials.TotpSecret))
            factors.Add(SecurityFactor.Totp);
        if (Settings.UsbBindingEnabled && HasActiveUsbBindings(credentials.UsbBindings))
            factors.Add(SecurityFactor.Usb);
        return factors;
    }

    private static bool HasActiveUsbBindings(IEnumerable<UsbBindingCredential> bindings) =>
        bindings.Any(binding => !binding.MarkerCleanupPending);

    private bool VerifyFactor(SecurityFactor factor, SecurityCredentials credentials, SecurityVerificationResponse response)
    {
        return factor switch
        {
            SecurityFactor.Totp => credentials.TotpSecret is not null && TotpService.Verify(credentials.TotpSecret, response.TotpCode, _timeProvider.GetUtcNow()),
            SecurityFactor.Usb => response.UsbPresent && credentials.UsbBindings.Any(IsBindingPresent),
            _ => false
        };
    }

    private TimeSpan? GetLockoutRemaining(DateTimeOffset? lockedUntilUtc)
    {
        if (lockedUntilUtc is not { } lockedUntil)
            return null;
        var remaining = lockedUntil - _timeProvider.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private SecurityVerificationResult HandleUnlockFailure(
        SecurityCredentialMetadata metadata,
        CredentialUnlockResult unlockResult)
    {
        if (unlockResult != CredentialUnlockResult.InvalidPassword)
            return new SecurityVerificationResult(false, SecurityVerificationFailure.FactorUnavailable);

        metadata.FailedAttempts++;
        if (metadata.FailedAttempts >= LockoutFailureLimit)
            metadata.LockedUntilUtc = _timeProvider.GetUtcNow().Add(LockoutDuration);
        if (!TrySaveMetadata(metadata))
            return new SecurityVerificationResult(false, SecurityVerificationFailure.FactorUnavailable);
        return new SecurityVerificationResult(false, SecurityVerificationFailure.InvalidCredentials,
            GetLockoutRemaining(metadata.LockedUntilUtc));
    }

    private bool TrySaveCredentials(SecurityCredentialContext context)
    {
        try
        {
            credentialStore.Save(context);
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(exception, "Security credential persistence is unavailable.");
            return false;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Security credential persistence failed.");
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Security credential persistence was denied.");
            return false;
        }

        // 信封先落盘，再同步免密 TOTP 副本，避免出现「新种子 / 旧信封」的错配
        try
        {
            if (Settings.RequireAllSelectedFactors || string.IsNullOrWhiteSpace(context.Credentials.TotpSecret))
                credentialStore.DeleteStandaloneTotp();
            else
                credentialStore.SaveStandaloneTotp(context.Credentials.TotpSecret);
            return true;
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(exception, "Standalone TOTP persistence is unavailable.");
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Standalone TOTP persistence failed.");
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Standalone TOTP persistence was denied.");
        }

        DiscardStandaloneTotp();
        return false;
    }

    private void DiscardStandaloneTotp()
    {
        try
        {
            credentialStore.DeleteStandaloneTotp();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Standalone TOTP cleanup failed.");
        }
    }

    private bool TrySaveMetadata(SecurityCredentialMetadata metadata)
    {
        try
        {
            credentialStore.SaveMetadata(metadata);
            return true;
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(exception, "Security credential persistence is unavailable.");
            return false;
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Security credential persistence failed.");
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Security credential persistence was denied.");
            return false;
        }
    }

    private bool IsBindingPresent(UsbBindingCredential binding)
    {
        if (binding.MarkerCleanupPending)
            return false;

        var rootPath = ResolveBindingRootPath(binding);
        if (rootPath is null)
            return false;

        var drive = FindDevice(binding.DeviceId ?? string.Empty);
        return drive is not null && IsBindingPresent(binding, drive);
    }

    private bool IsBindingPresent(UsbBindingCredential binding, UsbDriveInfo drive)
    {
        if (binding.MarkerCleanupPending || !BindingMatchesDevice(binding, drive))
            return false;

        return IsBindingTokenPresent(binding, drive.RootPath);
    }

    private bool IsBindingTokenPresent(UsbBindingCredential binding, string rootPath)
    {
        try
        {
            var path = GetBindingMarkerPath(rootPath);
            return File.Exists(path) && MarkerTokenMatches(path, binding.TokenHash);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private readonly record struct SecuritySettingsSnapshot(
        bool SecurityEnabled,
        bool PasswordEnabled,
        bool TotpEnabled,
        bool UsbBindingEnabled,
        bool RequireAllSelectedFactors,
        bool AllowSettingsPreview,
        bool ProtectOpenSettings,
        bool ProtectToggleMainWindow,
        bool ProtectToggleFloatingWindow,
        bool ProtectRestart,
        bool ProtectExit,
        bool ProtectRollCallStart,
        bool ProtectRollCallReset,
        bool ProtectQuickDrawStart,
        bool ProtectQuickDrawReset,
        bool ProtectLotteryStart,
        bool ProtectLotteryReset,
        bool ProtectLinkage,
        bool SettingsIntegrityCheckEnabled,
        SettingsIntegrityAction SettingsIntegrityAction,
        SettingsIntegrityRestoreSource SettingsIntegrityRestoreSource,
        int SudoModeDurationSeconds,
        bool SudoModeEnabled)
    {
        public static SecuritySettingsSnapshot Capture(SecuritySettingsConfig settings) => new(
            settings.SecurityEnabled,
            settings.PasswordEnabled,
            settings.TotpEnabled,
            settings.UsbBindingEnabled,
            settings.RequireAllSelectedFactors,
            settings.AllowSettingsPreview,
            settings.ProtectOpenSettings,
            settings.ProtectToggleMainWindow,
            settings.ProtectToggleFloatingWindow,
            settings.ProtectRestart,
            settings.ProtectExit,
            settings.ProtectRollCallStart,
            settings.ProtectRollCallReset,
            settings.ProtectQuickDrawStart,
            settings.ProtectQuickDrawReset,
            settings.ProtectLotteryStart,
            settings.ProtectLotteryReset,
            settings.ProtectLinkage,
            settings.SettingsIntegrityCheckEnabled,
            settings.SettingsIntegrityAction,
            settings.SettingsIntegrityRestoreSource,
            settings.SudoModeDurationSeconds,
            settings.SudoModeEnabled);
        public void Restore(SecuritySettingsConfig settings)
        {
            settings.SecurityEnabled = SecurityEnabled;
            settings.PasswordEnabled = PasswordEnabled;
            settings.TotpEnabled = TotpEnabled;
            settings.UsbBindingEnabled = UsbBindingEnabled;
            settings.RequireAllSelectedFactors = RequireAllSelectedFactors;
            settings.AllowSettingsPreview = AllowSettingsPreview;
            settings.ProtectOpenSettings = ProtectOpenSettings;
            settings.ProtectToggleMainWindow = ProtectToggleMainWindow;
            settings.ProtectToggleFloatingWindow = ProtectToggleFloatingWindow;
            settings.ProtectRestart = ProtectRestart;
            settings.ProtectExit = ProtectExit;
            settings.ProtectRollCallStart = ProtectRollCallStart;
            settings.ProtectRollCallReset = ProtectRollCallReset;
            settings.ProtectQuickDrawStart = ProtectQuickDrawStart;
            settings.ProtectQuickDrawReset = ProtectQuickDrawReset;
            settings.ProtectLotteryStart = ProtectLotteryStart;
            settings.ProtectLotteryReset = ProtectLotteryReset;
            settings.ProtectLinkage = ProtectLinkage;
            settings.SettingsIntegrityCheckEnabled = SettingsIntegrityCheckEnabled;
            settings.SettingsIntegrityAction = SettingsIntegrityAction;
            settings.SettingsIntegrityRestoreSource = SettingsIntegrityRestoreSource;
            settings.SudoModeDurationSeconds = SudoModeDurationSeconds;
            settings.SudoModeEnabled = SudoModeEnabled;
        }

        /// <summary>
        ///     判断这次改动是否放宽了防护。放宽防护的动作不接受任何既有的免验证状态，
        ///     否则一次授权就能把整套防护拆掉。
        /// </summary>
        public bool IsProtectionDowngrade(SecuritySettingsSnapshot after) =>
            Removed(SecurityEnabled, after.SecurityEnabled)
            || Removed(PasswordEnabled, after.PasswordEnabled)
            || Removed(TotpEnabled, after.TotpEnabled)
            || Removed(UsbBindingEnabled, after.UsbBindingEnabled)
            || Removed(RequireAllSelectedFactors, after.RequireAllSelectedFactors)
            || Added(AllowSettingsPreview, after.AllowSettingsPreview)
            || Removed(SettingsIntegrityCheckEnabled, after.SettingsIntegrityCheckEnabled)
            || (SettingsIntegrityAction == IntegrityAction.Confirm &&
                after.SettingsIntegrityAction == IntegrityAction.AutoRestore)
            || Removed(ProtectOpenSettings, after.ProtectOpenSettings)
            || Removed(ProtectToggleMainWindow, after.ProtectToggleMainWindow)
            || Removed(ProtectToggleFloatingWindow, after.ProtectToggleFloatingWindow)
            || Removed(ProtectRestart, after.ProtectRestart)
            || Removed(ProtectExit, after.ProtectExit)
            || Removed(ProtectRollCallStart, after.ProtectRollCallStart)
            || Removed(ProtectRollCallReset, after.ProtectRollCallReset)
            || Removed(ProtectQuickDrawStart, after.ProtectQuickDrawStart)
            || Removed(ProtectQuickDrawReset, after.ProtectQuickDrawReset)
            || Removed(ProtectLotteryStart, after.ProtectLotteryStart)
            || Removed(ProtectLotteryReset, after.ProtectLotteryReset)
            || Removed(ProtectLinkage, after.ProtectLinkage)
            || after.SudoModeDurationSeconds > SudoModeDurationSeconds;

        private static bool Removed(bool before, bool after) => before && !after;

        private static bool Added(bool before, bool after) => !before && after;
    }

    private void NormalizeSettings(SecurityCredentials credentials)
    {
        if (credentials.Password is null)
        {
            Settings.PasswordEnabled = false;
            Settings.TotpEnabled = false;
            Settings.UsbBindingEnabled = false;
        }
        else
        {
            // Password is the non-optional base factor once credentials exist.
            Settings.PasswordEnabled = true;
            if (string.IsNullOrWhiteSpace(credentials.TotpSecret))
                Settings.TotpEnabled = false;
            if (!HasActiveUsbBindings(credentials.UsbBindings))
                Settings.UsbBindingEnabled = false;
        }

        if (!GetRequiredFactors(credentials).Any())
        {
            Settings.SecurityEnabled = false;
            DisableOperationProtections();
        }

        // 防篡改校验属于安全保护的一部分，保护关闭时一并关闭
        if (!Settings.SecurityEnabled)
            Settings.SettingsIntegrityCheckEnabled = false;
    }

    private void DisableOperationProtections()
    {
        // 防篡改校验以安全凭据为锚点，保护被整体关闭时它也不再成立
        Settings.SettingsIntegrityCheckEnabled = false;
        Settings.ProtectOpenSettings = false;
        Settings.ProtectToggleMainWindow = false;
        Settings.ProtectToggleFloatingWindow = false;
        Settings.ProtectRestart = false;
        Settings.ProtectExit = false;
        Settings.ProtectRollCallStart = false;
        Settings.ProtectRollCallReset = false;
        Settings.ProtectQuickDrawStart = false;
        Settings.ProtectQuickDrawReset = false;
        Settings.ProtectLotteryStart = false;
        Settings.ProtectLotteryReset = false;
        Settings.ProtectLinkage = false;
    }

    public bool IsSudoModeActive()
    {
        if (IsSettingsSudoModeActive())
            return true;

        return IsGlobalSudoModeActive();
    }

    public bool IsGlobalSudoModeActive()
    {
        if (!Settings.SudoModeEnabled)
        {
            _sudoModeExpirationUtc = null;
            return false;
        }

        if (_sudoModeExpirationUtc is { } expiration)
        {
            var now = _timeProvider.GetUtcNow();
            if (now >= expiration)
            {
                _sudoModeExpirationUtc = null;
                SudoModeChanged?.Invoke();
                return false;
            }
            return true;
        }
        return false;
    }

    public void DeactivateGlobalSudoMode()
    {
        _sudoModeExpirationUtc = null;
        SudoModeChanged?.Invoke();
    }

    public void DeactivateSudoMode()
    {
        _settingsSudoExpirationUtc = null;
        _protectionDowngradeAuthorizationUtc = null;
        _sudoModeExpirationUtc = null;
        SudoModeChanged?.Invoke();
    }

    public void DeactivateSettingsSudoMode()
    {
        _settingsSudoExpirationUtc = null;
        _protectionDowngradeAuthorizationUtc = null;
    }

    private bool IsSettingsSudoModeActive()
    {
        if (!Settings.SudoModeEnabled)
        {
            _settingsSudoExpirationUtc = null;
            return false;
        }

        if (_settingsSudoExpirationUtc is not { } expiration)
            return false;

        var now = _timeProvider.GetUtcNow();
        if (now < expiration)
            return true;

        _settingsSudoExpirationUtc = null;
        return false;
    }

    /// <summary>
    ///     放宽防护的改动必须有一次专门为它发生的密码验证；这次验证同样按时长过期，
    ///     否则每调一个开关就要重新输一次密码。
    /// </summary>
    private bool IsProtectionDowngradeAuthorized()
    {
        if (!Settings.SudoModeEnabled)
        {
            _protectionDowngradeAuthorizationUtc = null;
            return false;
        }

        if (_protectionDowngradeAuthorizationUtc is not { } authorizedAt)
            return false;

        if (_timeProvider.GetUtcNow() < authorizedAt.AddSeconds(Settings.SudoModeDurationSeconds))
            return true;

        _protectionDowngradeAuthorizationUtc = null;
        return false;
    }

    private void ActivateSettingsSudoMode()
    {
        if (!Settings.SudoModeEnabled)
            return;

        _settingsSudoExpirationUtc = _timeProvider.GetUtcNow().AddSeconds(Settings.SudoModeDurationSeconds);
    }

    private void ActivateGlobalSudoMode()
    {
        if (!Settings.SudoModeEnabled)
            return;

        _sudoModeExpirationUtc = _timeProvider.GetUtcNow().AddSeconds(Settings.SudoModeDurationSeconds);
        SudoModeChanged?.Invoke();
    }

    private UsbDriveInfo? FindDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        return usbDeviceCatalog.GetRemovableDevices().FirstOrDefault(device =>
            string.Equals(device.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool BindingMatchesDevice(UsbBindingCredential binding, UsbDriveInfo device)
    {
        return !string.IsNullOrWhiteSpace(binding.DeviceId) &&
               string.Equals(binding.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveBindingRootPath(UsbBindingCredential binding)
    {
        return !string.IsNullOrWhiteSpace(binding.DeviceId)
            ? FindDevice(binding.DeviceId)?.RootPath
            : null;
    }

    private void TryDeleteUsbKey(UsbBindingCredential binding)
    {
        var rootPath = ResolveBindingRootPath(binding);
        if (rootPath is null)
            return;

        TryDeleteUsbKeyAtPath(rootPath);
    }

    private bool TryWriteUsbKey(string rootPath, string token, string? expectedExistingTokenHash)
    {
        var path = GetBindingMarkerPath(rootPath);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(path))
            {
                if (expectedExistingTokenHash is null ||
                    !MarkerTokenMatches(path, expectedExistingTokenHash))
                    return false;

                // The marker belongs to a locally revoked binding. Clear its
                // hidden/system attributes before replacing it on rebind.
                TryDeleteFile(path);
                if (File.Exists(path))
                    return false;
            }

            File.WriteAllBytes(temporaryPath, Encoding.ASCII.GetBytes(token));
            File.Move(temporaryPath, path);
            if (_bindingMarker.TryHide(path))
                return true;

            TryDeleteFile(path);
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private void NormalizeSettings(SecurityCredentialMetadata metadata)
    {
        if (metadata.Password is null)
        {
            Settings.PasswordEnabled = false;
            Settings.TotpEnabled = false;
            Settings.UsbBindingEnabled = false;
        }
        else
        {
            Settings.PasswordEnabled = true;
            if (!metadata.HasTotp)
                Settings.TotpEnabled = false;
            if (!HasActiveUsbBindings(metadata.UsbBindings))
                Settings.UsbBindingEnabled = false;
        }

        if (!GetRequiredFactors(metadata).Any())
        {
            Settings.SecurityEnabled = false;
            DisableOperationProtections();
        }

        // 防篡改校验属于安全保护的一部分，保护关闭时一并关闭
        if (!Settings.SecurityEnabled)
            Settings.SettingsIntegrityCheckEnabled = false;
    }

    private void TryDeleteUsbKeyAtPath(string rootPath)
    {
        TryDeleteFile(GetBindingMarkerPath(rootPath));
    }

    private void TryDeleteUsbKeyAtPath(string rootPath, string expectedTokenHash)
    {
        var path = GetBindingMarkerPath(rootPath);
        try
        {
            if (File.Exists(path) && MarkerTokenMatches(path, expectedTokenHash))
                TryDeleteFile(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string GetBindingMarkerPath(string rootPath)
    {
        return Path.Combine(rootPath, _bindingMarker.FileName);
    }

    private static bool MarkerTokenMatches(string path, string expectedTokenHash)
    {
        var token = File.ReadAllText(path, Encoding.ASCII).Trim();
        var actualTokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actualTokenHash),
            Encoding.ASCII.GetBytes(expectedTokenHash));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        File.SetAttributes(path, attributes & ~(FileAttributes.Hidden | FileAttributes.System));
                }
                catch (PlatformNotSupportedException)
                {
                }

                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RestoreCredentialsAfterPasswordRemovalFailure(
        SecurityCredentials credentials,
        PasswordCredential? password,
        string? totpSecret,
        IEnumerable<UsbBindingCredential> bindings,
        int failedAttempts,
        DateTimeOffset? lockedUntilUtc,
        string? pendingTotpSecret)
    {
        credentials.Password = password;
        credentials.TotpSecret = totpSecret;
        credentials.UsbBindings.Clear();
        credentials.UsbBindings.AddRange(bindings);
        credentials.FailedAttempts = failedAttempts;
        credentials.LockedUntilUtc = lockedUntilUtc;
        _pendingTotpSecret = pendingTotpSecret;
    }
}
