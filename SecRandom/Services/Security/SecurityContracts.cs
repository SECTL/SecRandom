using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Services.Security;

public enum SecurityVerificationFailure
{
    None,
    NotRequired,
    NotConfigured,
    LockedOut,
    InvalidCredentials,
    FactorUnavailable,
    Cancelled,
    PreviewRequested
}

public sealed record SecurityVerificationRequest(
    IReadOnlyList<SecurityFactor> RequiredFactors,
    bool RequireAllSelectedFactors,
    TimeSpan? LockoutRemaining,
    bool AllowPreview = false);

public sealed record SecurityVerificationResponse(
    string Password,
    string TotpCode,
    bool UsbPresent,
    bool Cancelled = false,
    bool PreviewRequested = false);

public sealed record SecurityVerificationResult(
    bool IsAuthorized,
    SecurityVerificationFailure Failure,
    TimeSpan? LockoutRemaining = null)
{
    public static SecurityVerificationResult Allowed { get; } = new(true, SecurityVerificationFailure.None);
}

public sealed record SecurityAuthorizationResult(bool IsAuthorized, bool PreviewOpened = false);

public sealed record SecuritySettingsUiState(
    bool HasPassword,
    bool HasTotp,
    bool HasUsbBinding,
    bool SecurityEnabled,
    bool CanConfigureAdditionalFactors,
    bool CanEditFactorSelection,
    bool CanEditProtectedOperations,
    TimeSpan? LockoutRemaining);

public enum SecurityFactor
{
    Password,
    Totp,
    Usb
}

public interface ISecurityVerificationPrompt
{
    Task<SecurityVerificationResponse> RequestAsync(TopLevel xamlRoot, SecurityVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISecurityService
{
    SecuritySettingsUiState GetUiState();
    bool RequiresVerification(SecurityOperation operation);
    Task<SecurityVerificationResult> VerifyAsync(SecurityVerificationResponse response, CancellationToken cancellationToken = default);
    Task<bool> AuthorizeAsync(SecurityOperation operation, Func<Task> action, CancellationToken cancellationToken = default);
    Task<bool> AuthorizeAsync(IReadOnlyCollection<SecurityOperation> operations, Func<Task> action, CancellationToken cancellationToken = default);
    Task<bool> AuthorizePasswordAsync(TopLevel xamlRoot, Func<Task> action, CancellationToken cancellationToken = default);
    Task<SecurityAuthorizationResult> AuthorizeSettingsAsync(
        Func<Task> action,
        Func<Task> previewAction,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateSecuritySettingsAsync(TopLevel xamlRoot, Action update, CancellationToken cancellationToken = default);
    Task<bool> SetPasswordAsync(string password, string? currentPassword = null, CancellationToken cancellationToken = default);
    Task<bool> RemovePasswordAsync(string currentPassword, CancellationToken cancellationToken = default);
    Task<string?> BeginTotpSetupAsync(CancellationToken cancellationToken = default);
    Task<string?> BeginTotpSetupAsync(TopLevel xamlRoot, CancellationToken cancellationToken = default);
    Task CancelTotpSetupAsync(string secret, CancellationToken cancellationToken = default);
    Task<bool> ConfirmTotpAsync(string secret, string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsbBindingInfo>> GetUsbBindingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsbDeviceInfo>> GetUsbDevicesAsync(CancellationToken cancellationToken = default);
    Task<bool> BindUsbAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<bool> BindUsbAsync(TopLevel xamlRoot, string deviceId, CancellationToken cancellationToken = default);
    Task<bool> UnbindUsbAsync(string bindingId, CancellationToken cancellationToken = default);
    Task<bool> UnbindUsbAsync(TopLevel xamlRoot, string bindingId, CancellationToken cancellationToken = default);
    bool TryUpdateSettings(Action update);
}

public sealed record UsbBindingInfo(string Id, string DisplayName, bool IsPresent);

public sealed record UsbDeviceInfo(
    string DriveLetter,
    string DisplayName,
    string DeviceId,
    bool IsBound,
    string? BindingId,
    bool IsPresent)
{
    public string? HardwareName { get; init; }
}
