using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Helpers;
using SecRandom.Shared;

namespace SecRandom.Core.Services.Logging;

public class FileLoggerProvider : ILoggerProvider
{
    private const int LogRetentionDays = 30;

    /// <summary>
    ///     攒够这么多字符就立刻落盘，避免一次长时间的批量写入把缓冲撑得过大。
    /// </summary>
    private const int FlushThresholdChars = 32 * 1024;

    /// <summary>
    ///     普通级别日志的落盘延迟。逐行 <c>AutoFlush</c> 会让每条日志都同步刷一次磁盘，
    ///     设置页翻页这种高频 Info 日志的写入开销能达到 CPU 占用的可观比例。
    /// </summary>
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();

    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly Stream? _logStream;
    private readonly StreamWriter? _logWriter;

    /// <summary>单次定时器，只在有未落盘内容时挂起一次，落盘后不再重排。</summary>
    private readonly Timer? _flushTimer;

    private int _pendingChars;
    private bool _flushScheduled;

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
                // 关闭逐行自动刷新，改由 WriteLog 的阈值/定时刷盘控制。
                AutoFlush = false
            };
            _flushTimer = new Timer(_ => OnFlushTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Current = this;
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
        // 缓冲写入后退出前必须显式刷盘，否则最后一段日志会随进程消失。
        try
        {
            _flushTimer?.Dispose();
        }
        catch (Exception)
        {
            // 忽略定时器释放失败。
        }

        _logWriter?.Close();
        _loggers.Clear();
        if (ReferenceEquals(Current, this)) Current = null;
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
        foreach (var i in logs.Where(x => 
                     Path.GetFileName(x) != currentLogFile && 
                     File.Exists(x) &&
                     (now - File.GetLastWriteTime(x)).TotalDays > LogRetentionDays &&
                     (x.EndsWith(@".log") || x.EndsWith(@".log.gz"))))
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

                // 不再逐行 AutoFlush：只累加待落盘字符数，攒够阈值或定时器到点才真正刷盘。
                // 日志查看器读的是同一个文件，所以日志内容本身还是完整的，只是最多晚一个
                // FlushDelay 可见；退出、崩溃前的最后一段已由 Dispose 刷新。
                _pendingChars += log.Length + Environment.NewLine.Length;
                if (_pendingChars >= FlushThresholdChars)
                {
                    FlushCore();
                    return;
                }

                ScheduleFlush();
            }
            catch (Exception)
            {
                _canWrite = false;
            }
        }
    }

    /// <summary>
    ///     立即把缓冲区写入磁盘。日志查看器在读取当前日志前调用，避免看不到最近几秒的内容。
    /// </summary>
    public static void FlushAll()
    {
        Current?.FlushCore();
    }

    internal static FileLoggerProvider? Current { get; private set; }

    private void FlushCore()
    {
        lock (_lock)
        {
            if (!_canWrite || _logWriter is null) return;

            try
            {
                _logWriter.Flush();
            }
            catch (Exception)
            {
                _canWrite = false;
            }

            _pendingChars = 0;
        }
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled || _flushTimer is null) return;

        _flushScheduled = true;
        _flushTimer.Change(FlushDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    ///     定时器回调：先复位挂起标记再刷盘，这样刷盘期间新到的日志能重新排一次定时器。
    /// </summary>
    private void OnFlushTimer()
    {
        lock (_lock)
        {
            _flushScheduled = false;
        }

        FlushCore();
    }
}
