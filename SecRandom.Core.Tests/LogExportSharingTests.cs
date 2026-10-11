using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Helpers;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Logging;
using SecRandom.Services.ImportExport;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

/// <summary>
///     导出日志的共享模式回归用例。<see cref="FileLoggerProvider" /> 整个会话都以
///     FileAccess.ReadWrite + FileShare.Read 持有当天日志，普通 File.OpenRead / File.ReadAllText
///     （FileShare.Read）会共享冲突：导出全部数据会整包失败，诊断导出会静默丢掉日志。
/// </summary>
public sealed class LogExportSharingTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "log-sharing-tests", Guid.NewGuid().ToString("N"));
    private readonly string _exportDirectory = Path.Combine(Path.GetTempPath(), "SecRandom", "log-sharing-exports", Guid.NewGuid().ToString("N"));

    public LogExportSharingTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
        Directory.CreateDirectory(_exportDirectory);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
        if (Directory.Exists(_exportDirectory))
            Directory.Delete(_exportDirectory, recursive: true);
    }

    [Fact]
    public async Task ExportDiagnostic_IncludesTheLogFileTheAppStillHoldsOpenForWriting()
    {
        using var provider = CreateProvider();
        var logPath = Utils.GetFilePath("logs", "log-current.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        using (var stream = new FileStream(logPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        using (var writer = new StreamWriter(stream) { AutoFlush = true })
        {
            writer.WriteLine("2024/1/1 0:00:00|Information|SecRandom|导出诊断数据");

            var destination = Path.Combine(_exportDirectory, "diagnostic-live-log.zip");
            await CreateDiagnosticExportService(provider)
                .ExportDiagnosticAsync(destination, cancellationToken: TestContext.Current.CancellationToken);

            using var archive = ZipFile.OpenRead(destination);
            var entry = archive.GetEntry("logs/log-current.log");
            Assert.NotNull(entry);
            Assert.Contains("导出诊断数据", ReadEntryText(entry));
            Assert.Null(archive.GetEntry("diagnostic/unreadable-logs.txt"));
        }
    }

    [Fact]
    public async Task ExportDiagnostic_ReportsLogsItCannotReadInsteadOfDroppingThemSilently()
    {
        using var provider = CreateProvider();
        var logPath = Utils.GetFilePath("logs", "log-locked.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        using (var stream = new FileStream(logPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var writer = new StreamWriter(stream) { AutoFlush = true })
        {
            writer.WriteLine("2024/1/1 0:00:00|Information|SecRandom|独占持有的日志");

            var destination = Path.Combine(_exportDirectory, "diagnostic-locked-log.zip");
            await CreateDiagnosticExportService(provider)
                .ExportDiagnosticAsync(destination, cancellationToken: TestContext.Current.CancellationToken);

            using var archive = ZipFile.OpenRead(destination);
            Assert.Null(archive.GetEntry("logs/log-locked.log"));
            var note = archive.GetEntry("diagnostic/unreadable-logs.txt");
            Assert.NotNull(note);
            Assert.Contains("logs/log-locked.log", ReadEntryText(note));
        }
    }

    [Fact]
    public void CompressFileAndDelete_ReplacesTheArchiveWithoutBlockingConcurrentReaders()
    {
        var logPath = Utils.GetFilePath("logs", "log-previous.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, "2024/1/1 0:00:00|Information|SecRandom|上一条日志" + Environment.NewLine);

        // 日志查看器 / 诊断导出随时可能这样读旧日志，压缩必须能同时进行且不留下半成品
        using (var reader = SharedFileReader.OpenRead(logPath))
        {
            Assert.True(reader.CanRead);

            GZipHelper.CompressFileAndDelete(logPath);

            Assert.False(File.Exists(logPath));
            Assert.False(File.Exists(logPath + ".gz.tmp"));
        }

        using var compressed = SharedFileReader.OpenRead(logPath + ".gz");
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader2 = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        Assert.Contains("上一条日志", reader2.ReadToEnd());
    }

    private static ImportExportService CreateDiagnosticExportService(IServiceProvider provider)
    {
        return new ImportExportService(
            provider.GetRequiredService<MainConfigHandler>(),
            provider.GetRequiredService<DataArchiveService>(),
            NullLogger<ImportExportService>.Instance);
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}
