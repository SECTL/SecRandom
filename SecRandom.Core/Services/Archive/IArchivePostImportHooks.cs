using System.Collections.Generic;

namespace SecRandom.Core.Services.Archive;

/// <summary>
///     Platform-specific follow-up work after an import has been committed.
///     <see cref="DataArchiveService" /> reloads the main configuration and the configured
///     profiles before invoking these hooks; implementations refresh platform runtime services
///     (shortcuts, telemetry, desktop integrations, course linkage, ...) and return
///     non-fatal warning messages that are appended to the import result.
///     Hooks run synchronously under the archive operation lock and must not block.
/// </summary>
public interface IArchivePostImportHooks
{
    IReadOnlyList<string> OnSettingsImported();
    IReadOnlyList<string> OnAllDataImported();
}

/// <summary>
///     Default no-op hooks. Mobile and other hosts can rely on the Core reload performed by
///     <see cref="DataArchiveService" /> itself until they register a richer implementation.
/// </summary>
public sealed class NullArchivePostImportHooks : IArchivePostImportHooks
{
    public IReadOnlyList<string> OnSettingsImported() => [];
    public IReadOnlyList<string> OnAllDataImported() => [];
}
