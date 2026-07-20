using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Plugins;
using SecRandom.Shared;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     Desktop shell over the Core <see cref="DataArchiveService" />. All backup/restore and
///     settings transfer behavior lives in Core; this class only owns the desktop-only
///     diagnostic export (logs, crashes, plugin inventory) and delegates everything else.
/// </summary>
public sealed class ImportExportService(
    MainConfigHandler configHandler,
    DataArchiveService dataArchiveService,
    IPluginManager pluginManager) : IImportExportService
{
    private readonly string _dataDirectory = Utils.DataRoot;

    public Task<string> ExportSettingsAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.ExportSettingsAsync(destinationPath, cancellationToken);
    }

    public Task<string> ExportAllDataAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.ExportAllDataAsync(destinationPath, cancellationToken);
    }

    public Task<ImportInspection> InspectSettingsAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.InspectSettingsAsync(sourcePath, cancellationToken);
    }

    public Task<ImportInspection> InspectAllDataAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.InspectAllDataAsync(sourcePath, cancellationToken);
    }

    public Task<ImportResult> ImportSettingsAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.ImportSettingsAsync(sourcePath, cancellationToken);
    }

    public Task<ImportResult> ImportAllDataAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.ImportAllDataAsync(sourcePath, cancellationToken);
    }

    public string CreateManualBackup(IReadOnlyCollection<string> roots)
    {
        return dataArchiveService.CreateManualBackup(roots);
    }

    public string CreateAutomaticBackup(CancellationToken cancellationToken = default)
    {
        return dataArchiveService.CreateAutomaticBackup(cancellationToken);
    }

    public Task<ImportResult> RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.RestoreBackupAsync(sourcePath, cancellationToken);
    }

    public Task<string> ExportDiagnosticAsync(string destinationPath, bool includeExtendedData = false,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            EnsureParents(destinationPath);
            using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
            var entries = new List<ArchiveFileEntry>();

            ArchiveZipWriter.WriteTextEntry(archive, "diagnostic/runtime.json", JsonSerializer.Serialize(new
            {
                software = "SecRandom",
                version = GlobalConstants.Version,
                runtime = Environment.Version.ToString(),
                operating_system = Environment.OSVersion.Platform.ToString(),
                architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                exported_utc = DateTime.UtcNow
            }, ConfigServiceBase.JsonOptions), entries);

            AddDiagnosticLogs(archive, entries, cancellationToken);
            if (includeExtendedData)
                AddExtendedDiagnosticData(archive, entries, cancellationToken);

            ArchiveZipWriter.WriteManifest(archive, ArchiveKind.Diagnostic, entries);
            return destinationPath;
        }, cancellationToken);
    }

    private void AddDiagnosticLogs(ZipArchive archive, List<ArchiveFileEntry> entries, CancellationToken cancellationToken)
    {
        var logsDirectory = Path.Combine(_dataDirectory, "logs");
        if (!Directory.Exists(logsDirectory))
            return;

        foreach (var path in Directory.EnumerateFiles(logsDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ReadLogText(path);
            if (text is null)
                continue;
            var relativePath = Path.GetRelativePath(logsDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            ArchiveZipWriter.WriteTextEntry(archive, $"logs/{relativePath}", RedactDiagnosticText(text), entries);
        }
    }

    private void AddExtendedDiagnosticData(ZipArchive archive, List<ArchiveFileEntry> entries, CancellationToken cancellationToken)
    {
        var settings = JsonSerializer.Serialize(configHandler.Data, ConfigServiceBase.JsonOptions);
        ArchiveZipWriter.WriteTextEntry(archive, "diagnostic/settings.redacted.json", RedactDiagnosticText(settings), entries);

        ArchiveZipWriter.WriteTextEntry(archive, "diagnostic/summary.json", JsonSerializer.Serialize(new
        {
            roll_call_lists = CountFiles("list", "roll_call_list"),
            lottery_pools = CountFiles("list", "lottery_list"),
            roll_call_histories = CountFiles("history", "roll_call_history"),
            lottery_histories = CountFiles("history", "lottery_history"),
            plugins = pluginManager.Plugins.Select(plugin => new
            {
                id = plugin.Manifest.Id,
                version = plugin.Manifest.Version,
                enabled = plugin.IsEnabled,
                status = plugin.Status.ToString()
            })
        }, ConfigServiceBase.JsonOptions), entries);

        var crashesDirectory = Path.Combine(_dataDirectory, "crashes");
        if (!Directory.Exists(crashesDirectory))
            return;
        foreach (var path in Directory.EnumerateFiles(crashesDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ReadLogText(path);
            if (text is not null)
                ArchiveZipWriter.WriteTextEntry(archive, $"crashes/{Path.GetFileName(path)}", RedactDiagnosticText(text), entries);
        }
    }

    private int CountFiles(params string[] path)
    {
        var directory = Path.Combine([_dataDirectory, .. path]);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Count() : 0;
    }

    private static string? ReadLogText(string path)
    {
        try
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var file = File.OpenRead(path);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private static string RedactDiagnosticText(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text,
            "(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]+=*", "Bearer <redacted>");
        text = System.Text.RegularExpressions.Regex.Replace(text,
            "(?i)(\\\"?(?:password|secret|token)\\\"?\\s*[:=]\\s*)(\\\"[^\\\"]*\\\"|[^\\s,;}\\]]+)", "$1<redacted>");
        text = System.Text.RegularExpressions.Regex.Replace(text,
            "(?i)(authorization\\s*[:=]\\s*)(\\\"?Bearer\\s+)?(\\\"[^\\\"]*\\\"|[^\\s,;}\\]]+)", "$1$2<redacted>");
        text = System.Text.RegularExpressions.Regex.Replace(text, "[A-Za-z]:\\\\[^\\s\"]+|/[^\\s\"]+", "<path>");
        text = System.Text.RegularExpressions.Regex.Replace(text, "[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", "<email>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text;
    }

    private static void EnsureParents(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
    }
}
