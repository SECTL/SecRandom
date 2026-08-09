using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace SecRandom.Services.Security;

public sealed class SecurityVerificationPrompt : ISecurityVerificationPrompt
{
    private bool _isShowing;

    public async Task<SecurityVerificationResponse> RequestAsync(
        TopLevel xamlRoot,
        SecurityVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_isShowing)
            return new SecurityVerificationResponse(string.Empty, string.Empty, false, Cancelled: true);

        _isShowing = true;
        try
        {
            return await SecurityVerificationDialog.ShowAsync(xamlRoot, request)
                .WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new SecurityVerificationResponse(string.Empty, string.Empty, false, Cancelled: true);
        }
        finally
        {
            _isShowing = false;
        }
    }
}
