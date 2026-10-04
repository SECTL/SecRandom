using System.Threading.Tasks;
using Avalonia.Threading;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Archive;
using SecRandom.Services.Security;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     Desktop <see cref="IArchivePreImportGuard" />: a settings/backup import that would loosen
///     protection must pass the same fresh password verification the security page uses, so a
///     crafted export cannot silently turn protection off. The archive engine calls this on its
///     worker thread, so the verification dialog is marshaled to the UI thread and awaited there.
/// </summary>
public sealed class SecurityArchivePreImportGuard(ISecurityService securityService) : IArchivePreImportGuard
{
    public bool AuthorizeSecuritySettings(SecuritySettingsConfig candidate)
    {
        return Dispatcher.UIThread
            .InvokeAsync(() => securityService.AuthorizeProtectionDowngradeAsync(candidate))
            .GetAwaiter()
            .GetResult();
    }
}
