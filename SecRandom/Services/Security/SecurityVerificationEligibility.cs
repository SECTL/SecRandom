using System.Linq;

namespace SecRandom.Services.Security;

internal static class SecurityVerificationEligibility
{
    public static bool CanSubmit(
        SecurityVerificationRequest request,
        string? password,
        string? totpCode,
        bool usbPresent)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.LockoutRemaining is not null || request.RequiredFactors.Count == 0)
            return false;

        var factorStates = request.RequiredFactors.Select(factor => factor switch
        {
            SecurityFactor.Password => !string.IsNullOrEmpty(password),
            SecurityFactor.Totp => IsCompleteTotpCode(totpCode),
            SecurityFactor.Usb => usbPresent,
            _ => false
        });

        return request.RequireAllSelectedFactors
            ? factorStates.All(value => value)
            : factorStates.Any(value => value);
    }

    private static bool IsCompleteTotpCode(string? code) =>
        code is { Length: 6 } && code.All(char.IsDigit);
}
