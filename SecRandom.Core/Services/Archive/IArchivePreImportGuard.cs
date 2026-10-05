namespace SecRandom.Core.Services.Archive;

/// <summary>
///     One authorization check before an import or restore is committed. The host must obtain a
///     fresh verification (the security password) while protection is active; returning false makes
///     <see cref="DataArchiveService" /> reject the import before anything is written.
///     Importing a backup replaces this machine's whole configuration, so it stays a protected
///     operation even though the archive can no longer carry the protection switches themselves:
///     those now live in an encrypted file no archive can reach. The check runs synchronously on the
///     archive worker thread, so an implementation that needs UI (the verification dialog) marshals
///     to its UI thread and blocks until the user answers.
/// </summary>
public interface IArchivePreImportGuard
{
    bool AuthorizeImport();
}

/// <summary>
///     Default implementation: always allow. Hosts without an authorization service (mobile,
///     tests, headless tooling) keep the previous behavior until they register a real guard.
/// </summary>
public sealed class NullArchivePreImportGuard : IArchivePreImportGuard
{
    public bool AuthorizeImport() => true;
}
