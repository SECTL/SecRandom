using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Plugins;

namespace SecRandom.Services.Plugins;

public interface IPluginManager
{
    IReadOnlyList<PluginDescriptor> Plugins { get; }
    void Refresh();
    void SetEnabled(string pluginId, bool isEnabled);
    string ImportPluginDirectory(string sourceDirectory);
    string ImportPluginPackage(string packagePath);
    IEnumerable<PluginLogEntry> GetPluginLogs(string pluginId, int maxEntries = 500);
}

public enum PluginImportFailureReason
{
    InvalidFolder,
    InvalidManifest,
    InvalidPackage,
    AlreadyExists,
    CopyFailed
}

public sealed class PluginImportException : Exception
{
    public PluginImportException(PluginImportFailureReason reason, string? message = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public PluginImportFailureReason Reason { get; }
}

public sealed record PluginLogEntry(
    DateTime Time,
    string Level,
    string Category,
    string Message)
{
    public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");
    public string ShortCategory => Category.Split('.').LastOrDefault() ?? Category;
    public string Preview => Message.Replace(Environment.NewLine, " ");
}
