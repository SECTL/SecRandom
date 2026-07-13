using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.Security;

internal sealed class SecurityService(
    MainConfigHandler configHandler,
    SecurityCredentialStore credentialStore,
    ISecurityVerificationPrompt prompt,
    ILogger<SecurityService> logger,
    TimeProvider? timeProvider = null) : ISecurityService
{
    private const int PasswordIterations = 310_000;
    private const int LockoutFailureLimit = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _authorizationGate = new(1, 1);

    private SecuritySettingsConfig Settings => configHandler.Data.SecuritySettings;

    public SecuritySettingsUiState GetUiState()
    {
        lock (_gate)
        {
            var credentials = credentialStore.Load();
            var remaining = GetLockoutRemaining(credentials);
            var hasPassword = credentials.Password is not null;
            var hasTotp = !string.IsNullOrWhiteSpace(credentials.TotpSecret);
            var hasUsb = credentials.UsbBindings.Any(IsBindingPresent);
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
        if (!Settings.SecurityEnabled || !HasValidSelectedFactor())
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
            SecurityOperation.ChangeSecuritySettings => true,
            _ => false
        };
    }

    public async Task<bool> AuthorizeAsync(
        SecurityOperation operation,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        if (!RequiresVerification(operation))
        {
            await action();
            return true;
        }

        await _authorizationGate.WaitAsync(cancellationToken);
        try
        {
            if (!RequiresVerification(operation))
            {
                await action();
                return true;
            }

            var credentials = credentialStore.Load();
            var request = new SecurityVerificationRequest(
                GetRequiredFactors(credentials),
                Settings.RequireAllSelectedFactors,
                GetLockoutRemaining(credentials));
            var response = await prompt.RequestAsync(request, cancellationToken);
            var result = await VerifyAsync(response, cancellationToken);
            if (!result.IsAuthorized)
            {
                logger.LogInformation("Security authorization rejected for {Operation}: {Failure}", operation, result.Failure);
                return false;
            }

            await action();
            return true;
        }
        finally
        {
            _authorizationGate.Release();
        }
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

            var credentials = credentialStore.Load();
            var request = new SecurityVerificationRequest(
                GetRequiredFactors(credentials),
                Settings.RequireAllSelectedFactors,
                GetLockoutRemaining(credentials),
                Settings.AllowSettingsPreview);
            var response = await prompt.RequestAsync(request, cancellationToken);
            if (response.PreviewRequested && request.AllowPreview)
            {
                await previewAction();
                return new SecurityAuthorizationResult(false, true);
            }

            var result = await VerifyAsync(response, cancellationToken);
            if (!result.IsAuthorized)
            {
                logger.LogInformation("Security settings authorization rejected: {Failure}", result.Failure);
                return new SecurityAuthorizationResult(false);
            }

            await action();
            return new SecurityAuthorizationResult(true);
        }
        finally
        {
            _authorizationGate.Release();
        }
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

            var credentials = credentialStore.Load();
            var remaining = GetLockoutRemaining(credentials);
            if (remaining is not null)
                return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.LockedOut, remaining));

            var factors = GetRequiredFactors(credentials);
            if (factors.Count == 0)
                return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.NotConfigured));

            var passwordPassed = VerifyFactor(SecurityFactor.Password, credentials, response);
            var additionalFactors = factors.Where(factor => factor != SecurityFactor.Password).ToArray();
            var additionalPassed = additionalFactors.Select(factor => VerifyFactor(factor, credentials, response)).ToArray();
            var additionalAuthorized = additionalPassed.Length == 0 ||
                (Settings.RequireAllSelectedFactors ? additionalPassed.All(value => value) : additionalPassed.Any(value => value));
            var authorized = passwordPassed && additionalAuthorized;
            if (authorized)
            {
                credentials.FailedAttempts = 0;
                credentials.LockedUntilUtc = null;
                credentialStore.Save(credentials);
                return Task.FromResult(SecurityVerificationResult.Allowed);
            }

            credentials.FailedAttempts++;
            if (credentials.FailedAttempts >= LockoutFailureLimit)
                credentials.LockedUntilUtc = _timeProvider.GetUtcNow().Add(LockoutDuration);
            credentialStore.Save(credentials);
            remaining = GetLockoutRemaining(credentials);
            return Task.FromResult(new SecurityVerificationResult(false, SecurityVerificationFailure.InvalidCredentials, remaining));
        }
    }

    public Task<bool> SetPasswordAsync(string password, string? currentPassword = null, CancellationToken cancellationToken = default)
    {
        if (password.Length < 6)
            return Task.FromResult(false);

        lock (_gate)
        {
            var credentials = credentialStore.Load();
            if (credentials.Password is not null && !VerifyPassword(credentials.Password, currentPassword ?? string.Empty))
                return Task.FromResult(false);

            credentials.Password = CreatePasswordCredential(password);
            Settings.PasswordEnabled = true;
            credentials.FailedAttempts = 0;
            credentials.LockedUntilUtc = null;
            credentialStore.Save(credentials);
            configHandler.Save();
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemovePasswordAsync(string currentPassword, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var credentials = credentialStore.Load();
            if (credentials.Password is null || !VerifyPassword(credentials.Password, currentPassword))
                return Task.FromResult(false);

            credentials.Password = null;
            credentials.TotpSecret = null;
            credentials.UsbBindings.Clear();
            credentials.FailedAttempts = 0;
            credentials.LockedUntilUtc = null;
            credentialStore.Save(credentials);

            Settings.SecurityEnabled = false;
            Settings.PasswordEnabled = false;
            Settings.TotpEnabled = false;
            Settings.UsbBindingEnabled = false;
            DisableOperationProtections();
            configHandler.Save();
            return Task.FromResult(true);
        }
    }

    public Task<string?> BeginTotpSetupAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (credentialStore.Load().Password is null || !credentialStore.CanStoreSecrets)
                return Task.FromResult<string?>(null);
            var secret = TotpService.GenerateSecret();
            return Task.FromResult<string?>(secret);
        }
    }

    public Task<bool> ConfirmTotpAsync(string secret, string code, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!credentialStore.CanStoreSecrets || !TotpService.Verify(secret, code, _timeProvider.GetUtcNow()))
                return Task.FromResult(false);

            var credentials = credentialStore.Load();
            if (credentials.Password is null)
                return Task.FromResult(false);
            credentials.TotpSecret = secret;
            credentialStore.Save(credentials);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<UsbBindingInfo>> GetUsbBindingsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var bindings = credentialStore.Load().UsbBindings
                .Select(binding => new UsbBindingInfo(binding.Id, binding.DisplayName, IsBindingPresent(binding)))
                .ToList();
            return Task.FromResult<IReadOnlyList<UsbBindingInfo>>(bindings);
        }
    }

    public Task<bool> BindUsbAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var credentials = credentialStore.Load();
            if (!credentialStore.CanStoreSecrets || credentials.Password is null || !Directory.Exists(rootPath))
                return Task.FromResult(false);

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var keyPath = Path.Combine(rootPath, "SecRandom.safety.key");
            try
            {
                File.WriteAllText(keyPath, token, Encoding.ASCII);
            }
            catch (IOException)
            {
                return Task.FromResult(false);
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(false);
            }

            var fullPath = Path.GetFullPath(rootPath);
            credentials.UsbBindings.RemoveAll(binding => string.Equals(binding.RootPath, fullPath, StringComparison.OrdinalIgnoreCase));
            credentials.UsbBindings.Add(new UsbBindingCredential
            {
                Id = Guid.NewGuid().ToString("N"),
                RootPath = fullPath,
                TokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(token))),
                DisplayName = new DirectoryInfo(fullPath).Name
            });
            credentialStore.Save(credentials);
            return Task.FromResult(true);
        }
    }

    public Task<bool> UnbindUsbAsync(string bindingId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var credentials = credentialStore.Load();
            var binding = credentials.UsbBindings.FirstOrDefault(item => item.Id == bindingId);
            if (binding is null)
                return Task.FromResult(false);

            credentials.UsbBindings.Remove(binding);
            TryDeleteUsbKey(binding.RootPath);
            if (!credentials.UsbBindings.Any(IsBindingPresent))
                Settings.UsbBindingEnabled = false;
            NormalizeSettings(credentials);
            credentialStore.Save(credentials);
            configHandler.Save();
            return Task.FromResult(true);
        }
    }

    public bool TryUpdateSettings(Action update)
    {
        lock (_gate)
        {
            var credentials = credentialStore.Load();
            update();
            NormalizeSettings(credentials);
            configHandler.Save();
            return true;
        }
    }

    private bool HasValidSelectedFactor()
    {
        var credentials = credentialStore.Load();
        return GetRequiredFactors(credentials).Count > 0;
    }

    private List<SecurityFactor> GetRequiredFactors(SecurityCredentials credentials)
    {
        var factors = new List<SecurityFactor>();
        if (credentials.Password is not null)
            factors.Add(SecurityFactor.Password);
        if (Settings.TotpEnabled && !string.IsNullOrWhiteSpace(credentials.TotpSecret))
            factors.Add(SecurityFactor.Totp);
        if (Settings.UsbBindingEnabled && credentials.UsbBindings.Any(IsBindingPresent))
            factors.Add(SecurityFactor.Usb);
        return factors;
    }

    private bool VerifyFactor(SecurityFactor factor, SecurityCredentials credentials, SecurityVerificationResponse response)
    {
        return factor switch
        {
            SecurityFactor.Password => credentials.Password is not null && VerifyPassword(credentials.Password, response.Password),
            SecurityFactor.Totp => credentials.TotpSecret is not null && TotpService.Verify(credentials.TotpSecret, response.TotpCode, _timeProvider.GetUtcNow()),
            SecurityFactor.Usb => response.UsbPresent && credentials.UsbBindings.Any(IsBindingPresent),
            _ => false
        };
    }

    private TimeSpan? GetLockoutRemaining(SecurityCredentials credentials)
    {
        if (credentials.LockedUntilUtc is not { } lockedUntil)
            return null;
        var remaining = lockedUntil - _timeProvider.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    private static PasswordCredential CreatePasswordCredential(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA512, 32);
        return new PasswordCredential
        {
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash),
            Iterations = PasswordIterations
        };
    }

    private static bool VerifyPassword(PasswordCredential credential, string password)
    {
        try
        {
            var salt = Convert.FromBase64String(credential.Salt);
            var expected = Convert.FromBase64String(credential.Hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, credential.Iterations, HashAlgorithmName.SHA512, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsBindingPresent(UsbBindingCredential binding)
    {
        if (!Directory.Exists(binding.RootPath))
            return false;
        try
        {
            var token = File.ReadAllText(Path.Combine(binding.RootPath, "SecRandom.safety.key"), Encoding.ASCII).Trim();
            var tokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(tokenHash), Encoding.ASCII.GetBytes(binding.TokenHash));
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
            if (!credentials.UsbBindings.Any(IsBindingPresent))
                Settings.UsbBindingEnabled = false;
        }

        if (!GetRequiredFactors(credentials).Any())
        {
            Settings.SecurityEnabled = false;
            DisableOperationProtections();
        }
    }

    private void DisableOperationProtections()
    {
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

    private static void TryDeleteUsbKey(string rootPath)
    {
        try
        {
            var path = Path.Combine(rootPath, "SecRandom.safety.key");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
