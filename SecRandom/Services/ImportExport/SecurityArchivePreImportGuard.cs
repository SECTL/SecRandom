using System.Threading.Tasks;
using Avalonia.Threading;
using SecRandom.Core.Services.Archive;
using SecRandom.Services.Security;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     Desktop <see cref="IArchivePreImportGuard" />: importing a settings file or restoring a backup
///     must pass a fresh verification while protection is active, so a crafted export can never be
///     used as a way around the security page. The archive engine calls this on its worker thread, so
///     the verification dialog is marshaled to the UI thread and awaited there.
/// </summary>
public sealed class SecurityArchivePreImportGuard(ISecurityService securityService) : IArchivePreImportGuard
{
    public bool AuthorizeImport()
    {
        return Dispatcher.UIThread
            .InvokeAsync(() => securityService.AuthorizeArchiveImportAsync())
            .GetAwaiter()
            .GetResult();
    }
}
