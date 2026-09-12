using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Services.Archive;

namespace SecRandom.Services.ImportExport;

public interface IImportExportService
{
    Task<string> ExportDiagnosticAsync(string destinationPath, bool includeExtendedData = false,
        CancellationToken cancellationToken = default);
    Task<string> ExportSettingsAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<string> ExportAllDataAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<string> ExportCloudBackupAsync(string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>Cloud archive root set the current cloud backup-content selection resolves to.</summary>
    IReadOnlyList<string> GetCloudBackupRoots();
    Task<ImportInspection> InspectSettingsAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ImportInspection> InspectAllDataAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ImportInspection> InspectCloudBackupAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportSettingsAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportAllDataAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportCloudBackupAsync(string sourcePath, CancellationToken cancellationToken = default);
    string CreateManualBackup(IReadOnlyCollection<string> roots);
    string CreateAutomaticBackup(CancellationToken cancellationToken = default);
    Task<ImportResult> RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default);
}
