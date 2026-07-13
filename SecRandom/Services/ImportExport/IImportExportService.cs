using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.ImportExport;

public interface IImportExportService
{
    Task<string> ExportDiagnosticAsync(string destinationPath, bool includeExtendedData = false,
        CancellationToken cancellationToken = default);
    Task<string> ExportSettingsAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<string> ExportAllDataAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<ImportInspection> InspectSettingsAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ImportInspection> InspectAllDataAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportSettingsAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportAllDataAsync(string sourcePath, CancellationToken cancellationToken = default);
    string CreateManualBackup(IReadOnlyCollection<string> roots);
    Task<ImportResult> RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default);
}
