using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;

namespace SecRandom.Services.Ipc;

/// <summary>
/// Append-only jsonl audit for automation writes. It records method, scope, decision,
/// duration and error code only: never roster names or setting values.
/// </summary>
public sealed class IpcAuditLogger(ILogger<IpcAuditLogger> logger)
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int RetainDays = 7;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _warningWritten;

    public void Write(string method, string scope, bool allowed, long durationMs, string? errorCode)
    {
        _ = WriteCoreAsync(new
        {
            ts = DateTimeOffset.Now,
            id = Guid.NewGuid().ToString("N")[..12],
            method,
            scope,
            decision = allowed ? "allowed" : "denied",
            durationMs,
            error = errorCode
        });
    }

    private async Task WriteCoreAsync(object entry)
    {
        try
        {
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var directory = Path.Combine(Utils.DataRoot, "logs");
                Directory.CreateDirectory(directory);
                Prune(directory);
                var path = Path.Combine(directory, $"ipc-audit-{DateTime.Now:yyyyMMdd}.jsonl");
                await RollIfNeededAsync(path).ConfigureAwait(false);
                await File.AppendAllTextAsync(path,
                    JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine,
                    Encoding.UTF8).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _warningWritten, 1) == 0)
                logger.LogWarning(exception, "IPC audit logging failed; automation continues.");
        }
    }

    private static void Prune(string directory)
    {
        try
        {
            var threshold = DateTime.Now.AddDays(-RetainDays);
            foreach (var file in Directory.EnumerateFiles(directory, "ipc-audit-*.jsonl*"))
            {
                if (File.GetLastWriteTime(file) < threshold)
                    File.Delete(file);
            }
        }
        catch
        {
            // Retention is best effort and never blocks a request.
        }
    }

    private static async Task RollIfNeededAsync(string path)
    {
        if (!File.Exists(path))
            return;
        var info = new FileInfo(path);
        if (info.Length < MaxFileBytes)
            return;

        var archive = path + ".1";
        try
        {
            if (File.Exists(archive))
                File.Delete(archive);
            File.Move(path, archive);
        }
        catch (IOException)
        {
            // Another request is rotating at the same instant; keep appending to the current file.
            await Task.CompletedTask;
        }
    }
}