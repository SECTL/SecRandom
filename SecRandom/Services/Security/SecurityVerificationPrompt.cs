using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Security;

public sealed class SecurityVerificationPrompt : ISecurityVerificationPrompt
{
    private bool _isShowing;

    public async Task<SecurityVerificationResult> RequestAsync(
        SecurityVerificationRequest request,
        Func<SecurityVerificationResponse, CancellationToken, Task<SecurityVerificationResult>> verify,
        CancellationToken cancellationToken = default)
    {
        if (_isShowing)
            return new SecurityVerificationResult(false, SecurityVerificationFailure.Cancelled);

        _isShowing = true;
        try
        {
            return await SecurityVerificationDialog.ShowAsync(request, verify, cancellationToken)
                .WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new SecurityVerificationResult(false, SecurityVerificationFailure.Cancelled);
        }
        finally
        {
            _isShowing = false;
        }
    }
}
