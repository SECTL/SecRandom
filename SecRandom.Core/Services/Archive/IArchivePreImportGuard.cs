using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Services.Archive;

/// <summary>
///     One authorization check before an import or restore is committed. The candidate security
///     settings are handed to the host, which must require a fresh verification when the incoming
///     configuration loosens protection; returning false makes
///     <see cref="DataArchiveService" /> reject the import before anything is written.
///     Without this gate a crafted settings export could turn security protection off without a
///     password, because the protection switches live in the very settings file an archive carries.
///     The check runs synchronously on the archive worker thread, so an implementation that needs
///     UI (the verification dialog) marshals to its UI thread and blocks until the user answers.
/// </summary>
public interface IArchivePreImportGuard
{
    bool AuthorizeSecuritySettings(SecuritySettingsConfig candidate);
}

/// <summary>
///     Default implementation: always allow. Hosts without an authorization service (mobile,
///     tests, headless tooling) keep the previous behavior until they register a real guard.
/// </summary>
public sealed class NullArchivePreImportGuard : IArchivePreImportGuard
{
    public bool AuthorizeSecuritySettings(SecuritySettingsConfig candidate) => true;
}
