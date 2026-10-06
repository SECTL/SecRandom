using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Helpers;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     Desktop shell over the Core <see cref="DataArchiveService" />. All backup/restore and
///     settings transfer behavior lives in Core; this class only owns the desktop-only
///     diagnostic export (logs and crashes) and delegates everything else.
/// </summary>
public sealed class ImportExportService(
    MainConfigHandler configHandler,
    DataArchiveService dataArchiveService,
    ILogger<ImportExportService> logger) : IImportExportService
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

    public Task<string> ExportCloudBackupAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.ExportCloudBackupAsync(destinationPath, cancellationToken);
    }

    public IReadOnlyList<string> GetCloudBackupRoots() => dataArchiveService.GetCloudBackupRoots();

    public Task<ImportInspection> InspectSettingsAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.InspectSettingsAsync(sourcePath, cancellationToken);
    }

    public Task<ImportInspection> InspectAllDataAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.InspectAllDataAsync(sourcePath, cancellationToken);
    }

    public Task<ImportInspection> InspectCloudBackupAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.InspectCloudBackupAsync(sourcePath, cancellationToken);
    }

    public Task<ImportResult> ImportSettingsAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.ImportSettingsAsync(sourcePath, cancellationToken);
    }

    public Task<ImportResult> ImportAllDataAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.ImportAllDataAsync(sourcePath, cancellationToken);
    }

    public Task<ImportResult> ImportCloudBackupAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return dataArchiveService.ImportCloudBackupAsync(sourcePath, cancellationToken);
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

        var unreadable = new List<string>();
        foreach (var path in Directory.EnumerateFiles(logsDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(logsDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            // 日志压缩中的临时文件不是日志，跳过以免把半成品写进诊断包
            if (relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = ReadLogText(path, out var error);
            if (text is null)
            {
                unreadable.Add(error is null ? relativePath : $"{relativePath}（{error}）");
                continue;
            }

            ArchiveZipWriter.WriteTextEntry(archive, $"logs/{relativePath}", RedactDiagnosticText(text), entries);
        }

        if (unreadable.Count == 0)
            return;

        logger.LogWarning("导出诊断数据时跳过 {Count} 个无法读取的日志文件：{Files}", unreadable.Count, string.Join("；", unreadable));
        ArchiveZipWriter.WriteTextEntry(archive, "diagnostic/unreadable-logs.txt",
            "以下日志文件在导出时无法读取，已跳过：" + Environment.NewLine +
            string.Join(Environment.NewLine, unreadable.Select(file => $"- logs/{file}")) + Environment.NewLine, entries);
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
            lottery_histories = CountFiles("history", "lottery_history")
        }, ConfigServiceBase.JsonOptions), entries);

        var crashesDirectory = Path.Combine(_dataDirectory, "crashes");
        if (!Directory.Exists(crashesDirectory))
            return;
        foreach (var path in Directory.EnumerateFiles(crashesDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ReadLogText(path, out _);
            if (text is not null)
                ArchiveZipWriter.WriteTextEntry(archive, $"crashes/{Path.GetFileName(path)}", RedactDiagnosticText(text), entries);
        }
    }

    private int CountFiles(params string[] path)
    {
        var directory = Path.Combine([_dataDirectory, .. path]);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Count() : 0;
    }

    private static string? ReadLogText(string path, out string? error)
    {
        try
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                // 日志文件由宿主整个会话持有写入句柄，必须宽松共享读取
                using var file = SharedFileReader.OpenRead(path);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                error = null;
                return reader.ReadToEnd();
            }

            error = null;
            return SharedFileReader.ReadAllText(path);
        }
        catch (Exception exception)
        {
            // 系统异常消息里通常带完整本地路径，先按诊断包的脱敏规则处理再记录/上报
            error = RedactDiagnosticText(exception.Message);
            return null;
        }
    }

    private static string RedactDiagnosticText(string text) => DiagnosticTextRedactor.Redact(text);

    private static void EnsureParents(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
    }
}
