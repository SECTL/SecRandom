using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Security;

public sealed class SecurityVerificationPrompt : ISecurityVerificationPrompt
{
    private SecurityVerificationWindow? _activeWindow;

    public async Task<SecurityVerificationResponse> RequestAsync(
        SecurityVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_activeWindow is not null)
            return new SecurityVerificationResponse(string.Empty, string.Empty, false, Cancelled: true);

        var window = new SecurityVerificationWindow(request);
        _activeWindow = window;
        window.Closed += (_, _) => _activeWindow = null;
        window.Show();
        return await window.Completion;
    }
}
