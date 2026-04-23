using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Helpers;

namespace SecRandom.Core.Services.Logging;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly Stream? _logStream;
    private readonly StreamWriter? _logWriter;

    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    private const int LogRetentionDays = 30;

    private readonly object _lock = new object();

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

    private bool _canWrite = true;

    public FileLoggerProvider()
    {
        try
        {
            var logs = Directory.GetFiles(Utils.GetDirectoryPath("logs"));
            var currentLogFile = GetLogFileName();
            _logStream = File.Open(Path.Combine(Utils.GetDirectoryPath("logs"), currentLogFile), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            _logWriter = new StreamWriter(_logStream)
            {
                AutoFlush = true
            };
            _ = Task.Run(() =>  ProcessPreviousLogs(logs, currentLogFile));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static void ProcessPreviousLogs(string[] logs, string currentLogFile)
    {
        foreach (var i in logs.Where(x => Path.GetFileName(x) != currentLogFile && Path.GetExtension(x) == ".log"))
        {
            try
            {
                GZipHelper.CompressFileAndDelete(i);
            }
            catch (Exception e)
            {
                Console.WriteLine("无法删除日志 {0}", Path.GetFileName(i));
            }
        }

        var now = DateTime.Now;
        foreach (var i in logs.Where(x => (now - File.GetLastWriteTime(x)).TotalDays > LogRetentionDays &&
                                          Path.GetFileName(x) != currentLogFile &&
                                          (x.EndsWith(".log") || x.EndsWith(".log.gz"))))
        {
            try
            {
                File.Delete(i);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    private static List<string?> GetLogs()
    {
        return Directory.GetFiles(Utils.GetDirectoryPath("logs")).Select(Path.GetFileName).ToList();
    }

    internal void WriteLog(string log)
    {
        lock (_lock)
        {
            try
            {
                if (!_canWrite)
                {
                    return;
                }
                _logWriter?.WriteLine(log);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                _canWrite = false;
            }
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
}
