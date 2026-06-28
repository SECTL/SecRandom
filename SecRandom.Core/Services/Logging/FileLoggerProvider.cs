using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Helpers;
using SecRandom.Shared;

namespace SecRandom.Core.Services.Logging;

public class FileLoggerProvider : ILoggerProvider
{
    private const int LogRetentionDays = 30;

    private readonly object _lock = new();

    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly Stream? _logStream;
    private readonly StreamWriter? _logWriter;

    private bool _canWrite = true;
    public static string LogDirectory => Utils.GetDirectoryPath("logs");
    public static int RetentionDays => LogRetentionDays;
    public string? CurrentLogFilePath { get; }

#if DEBUG
    internal LogLevel MinimumLevel { get; } = LogLevel.Trace;
#else
    internal LogLevel MinimumLevel { get; } = LogLevel.Information;
#endif

    public FileLoggerProvider()
    {
        try
        {
            var logs = Directory.GetFiles(LogDirectory);
            var currentLogFile = GetLogFileName();
            CurrentLogFilePath = Path.Combine(LogDirectory, currentLogFile);
            _logStream = File.Open(CurrentLogFilePath, FileMode.Create,
                FileAccess.ReadWrite, FileShare.Read);
            _logWriter = new StreamWriter(_logStream)
            {
                AutoFlush = true
            };
            _ = Task.Run(() => ProcessPreviousLogs(logs, currentLogFile));
        }
        catch (Exception e)
        {
            CreateLogger(typeof(FileLoggerProvider).FullName!)
                .LogError(e, "Failed to initialize file logger provider.");
        }
    }

    public void Dispose()
    {
        _logWriter?.Close();
        _loggers.Clear();
        GC.SuppressFinalize(this);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, new FileLogger(this, categoryName));
    }

    internal bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && logLevel >= MinimumLevel;
    }

    public static string GetLogFileName()
    {
        var n = 1;
        var logs = GetLogs();
        string filename;
        do
        {
            filename = $"log-{DateTime.Now:yyyy-M-d-HH-mm-ss}-{n}.log";
            n++;
        } while (logs.Contains(filename));

        return filename;
    }

    private void ProcessPreviousLogs(string[] logs, string currentLogFile)
    {
        var logger = CreateLogger(typeof(FileLoggerProvider).FullName!);
        foreach (var i in logs.Where(x => Path.GetFileName(x) != currentLogFile && Path.GetExtension(x) == ".log"))
            try
            {
                GZipHelper.CompressFileAndDelete(i);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to compress previous log file: {LogFile}", Path.GetFileName(i));
            }

        var now = DateTime.Now;
        foreach (var i in logs.Where(x => (now - File.GetLastWriteTime(x)).TotalDays > LogRetentionDays &&
                                          Path.GetFileName(x) != currentLogFile &&
                                          (x.EndsWith(".log") || x.EndsWith(".log.gz"))))
            try
            {
                File.Delete(i);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to delete expired log file: {LogFile}", Path.GetFileName(i));
            }
    }

    private static List<string?> GetLogs()
    {
        return Directory.GetFiles(LogDirectory).Select(Path.GetFileName).ToList();
    }

    internal void WriteLog(string log)
    {
        lock (_lock)
        {
            try
            {
                if (!_canWrite) return;

                _logWriter?.WriteLine(log);
            }
            catch (Exception)
            {
                _canWrite = false;
            }
        }
    }
}
