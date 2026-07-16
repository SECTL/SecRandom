using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Logging;
using SecRandom.Services.Desktop;
using LR = SecRandom.Langs.SettingsPages.LogViewer.Resources;

namespace SecRandom.Views.SettingsPages.LogViewer;

[PageInfo("settings.logs", FluentIcons.DocumentFilled, location: PageLocation.Bottom, isHide: true, useFullWidth: true)]
public partial class LogViewerSettingsPage : UserControl, INotifyPropertyChanged
{
    private IExternalLauncher ExternalLauncher { get; } = IAppHost.GetService<IExternalLauncher>();
    private const int MaxLoadedLines = 2000;

    private readonly ILogger<LogViewerSettingsPage> _logger =
        IAppHost.GetService<ILogger<LogViewerSettingsPage>>();

    private readonly ObservableCollection<LogEntry> _entries = [];
    private readonly ObservableCollection<LogEntry> _filteredEntries = [];
    private LogEntry? _selectedEntry;
    private LogFileItem? _selectedLogFile;
    private string _searchText = string.Empty;
    private LogLevelFilterOption _selectedLevelFilter;
    private string _entryCountText = string.Empty;
    private string _selectedFileInfoText = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public LogViewerSettingsPage()
    {
        LevelFilters =
        [
            new(null, LR.O_Level_All),
            new("Trace", LR.O_Level_Trace),
            new("Debug", LR.O_Level_Debug),
            new("Information", LR.O_Level_Information),
            new("Warning", LR.O_Level_Warning),
            new("Error", LR.O_Level_Error),
            new("Critical", LR.O_Level_Critical)
        ];
        _selectedLevelFilter = LevelFilters[0];
        DataContext = this;
        InitializeComponent();
    }

    public ObservableCollection<LogFileItem> LogFiles { get; } = [];
    public ObservableCollection<LogLevelFilterOption> LevelFilters { get; }
    public IEnumerable<LogEntry> FilteredEntries => _filteredEntries;

    public LogFileItem? SelectedLogFile
    {
        get => _selectedLogFile;
        set
        {
            if (!SetField(ref _selectedLogFile, value))
                return;

            OnPropertyChanged(nameof(CanDeleteSelectedLogFile));
            _ = LoadSelectedLogFileAsync();
        }
    }

    public LogLevelFilterOption SelectedLevelFilter
    {
        get => _selectedLevelFilter;
        set
        {
            if (!SetField(ref _selectedLevelFilter, value))
                return;

            ApplyFilter();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value))
                return;

            ApplyFilter();
        }
    }

    public LogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetField(ref _selectedEntry, value))
                return;

            OnPropertyChanged(nameof(SelectedEntryText));
            OnPropertyChanged(nameof(HasSelectedEntry));
        }
    }

    public string SelectedEntryText => SelectedEntry?.FullText ?? LR.M_SelectEntry;
    public bool HasSelectedEntry => SelectedEntry != null;
    public bool CanDeleteSelectedLogFile => SelectedLogFile is { IsCurrent: false };

    public string EntryCountText
    {
        get => _entryCountText;
        private set => SetField(ref _entryCountText, value);
    }

    public string SelectedFileInfoText
    {
        get => _selectedFileInfoText;
        private set => SetField(ref _selectedFileInfoText, value);
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        RefreshLogFiles();
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshLogFiles(SelectedLogFile?.FileName);
    }

    private void OpenFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenPath(FileLoggerProvider.LogDirectory);
        _logger.LogInformation("已请求打开日志目录：路径={Path}。", FileLoggerProvider.LogDirectory);
    }

    private async void CopyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry == null)
            return;

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        await clipboard.SetTextAsync(SelectedEntry.FullText);
        this.ShowSuccessToast(LR.M_Copied);
        _logger.LogInformation("已复制日志条目。");
    }

    private async void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedLogFile is not { IsCurrent: false } file)
            return;

        if (!await ConfirmDeleteAsync(file.FileName))
            return;

        try
        {
            File.Delete(file.FilePath);
            _logger.LogInformation("已删除日志文件：文件={FileName}。", file.FileName);
            this.ShowSuccessToast(string.Format(CultureInfo.CurrentCulture, LR.M_DeleteSuccess, file.FileName));
            RefreshLogFiles();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除日志文件失败：文件={FileName}。", file.FileName);
            this.ShowErrorToast(string.Format(CultureInfo.CurrentCulture, LR.M_DeleteFailed, ex.Message));
        }
    }

    private void RefreshLogFiles(string? preferredFileName = null)
    {
        var previousFileName = preferredFileName ?? SelectedLogFile?.FileName;
        LogFiles.Clear();

        var currentPath = GetCurrentLogFilePath();
        foreach (var file in Directory.EnumerateFiles(FileLoggerProvider.LogDirectory)
                     .Where(IsLogFile)
                     .Select(path => LogFileItem.FromFile(path, IsSamePath(path, currentPath)))
                     .OrderByDescending(item => item.LastWriteTime))
        {
            LogFiles.Add(file);
        }

        SelectedLogFile = LogFiles.FirstOrDefault(file => file.FileName == previousFileName)
                          ?? LogFiles.FirstOrDefault(file => file.IsCurrent)
                          ?? LogFiles.FirstOrDefault();

        if (SelectedLogFile == null)
        {
            _entries.Clear();
            ApplyFilter();
        }

        _logger.LogInformation("已刷新日志文件列表：文件数量={Count}。", LogFiles.Count);
    }

    private async Task LoadSelectedLogFileAsync()
    {
        var file = SelectedLogFile;
        _entries.Clear();
        SelectedEntry = null;

        if (file == null)
        {
            ApplyFilter();
            return;
        }

        try
        {
            var lines = await Task.Run(() => ReadTailLines(file.FilePath, MaxLoadedLines));
            foreach (var entry in ParseEntries(lines))
                _entries.Add(entry);

            SelectedFileInfoText = string.Format(
                CultureInfo.CurrentCulture,
                LR.C_FileInfo,
                file.SizeText,
                file.LastWriteTime.ToString("G", CultureInfo.CurrentCulture));
            _logger.LogInformation("已加载日志文件：文件={FileName}，读取行数={LineCount}，解析条目={EntryCount}。",
                file.FileName, lines.Count, _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载日志文件失败：文件={FileName}。", file.FileName);
            this.ShowErrorToast(string.Format(CultureInfo.CurrentCulture, LR.M_LoadFailed, ex.Message));
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _filteredEntries.Clear();

        var search = SearchText.Trim();
        foreach (var entry in _entries)
        {
            if (SelectedLevelFilter.Value != null && entry.Level != SelectedLevelFilter.Value)
                continue;

            if (!string.IsNullOrWhiteSpace(search) &&
                !entry.FullText.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;

            _filteredEntries.Add(entry);
        }

        EntryCountText = string.Format(
            CultureInfo.CurrentCulture,
            LR.C_EntryCount,
            _filteredEntries.Count,
            _entries.Count);
        OnPropertyChanged(nameof(FilteredEntries));
    }

    private async Task<bool> ConfirmDeleteAsync(string fileName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_DeleteTitle,
            Content = string.Format(CultureInfo.CurrentCulture, LR.M_DeleteContent, fileName),
            PrimaryButtonText = LR.M_DeletePrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private static List<string> ReadTailLines(string path, int maxLines)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using Stream content = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(stream, CompressionMode.Decompress)
            : stream;
        using var reader = new StreamReader(content, Encoding.UTF8, true);

        Queue<string> lines = new(maxLines);
        while (reader.ReadLine() is { } line)
        {
            if (lines.Count == maxLines)
                lines.Dequeue();

            lines.Enqueue(line);
        }

        return lines.ToList();
    }

    private static IEnumerable<LogEntry> ParseEntries(IReadOnlyList<string> lines)
    {
        LogEntryBuilder? current = null;
        foreach (var line in lines)
        {
            if (TryParseHeader(line, out var next))
            {
                if (current != null)
                    yield return current.Build();

                current = next;
                continue;
            }

            current?.AppendLine(line);
        }

        if (current != null)
            yield return current.Build();
    }

    private static bool TryParseHeader(string line, out LogEntryBuilder entry)
    {
        entry = default!;
        var parts = line.Split('|', 4);
        if (parts.Length < 4 || !DateTime.TryParse(parts[0], CultureInfo.CurrentCulture, out var time))
            return false;

        entry = new LogEntryBuilder(time, parts[1], parts[2], parts[3]);
        return true;
    }

    private static bool IsLogFile(string path)
    {
        return path.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".log.gz", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetCurrentLogFilePath()
    {
        return IAppHost.Host?.Services
            .GetService(typeof(IEnumerable<ILoggerProvider>)) is IEnumerable<ILoggerProvider> providers
            ? providers.OfType<FileLoggerProvider>().FirstOrDefault()?.CurrentLogFilePath
            : null;
    }

    private static bool IsSamePath(string path, string? otherPath)
    {
        return otherPath != null &&
               string.Equals(Path.GetFullPath(path), Path.GetFullPath(otherPath), StringComparison.OrdinalIgnoreCase);
    }

    private void OpenPath(string path)
    {
        ExternalLauncher.TryOpenPath(path);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed record LogFileItem(
        string FileName,
        string FilePath,
        DateTime LastWriteTime,
        long Size,
        bool IsCurrent)
    {
        public string DisplayName => IsCurrent
            ? string.Format(CultureInfo.CurrentCulture, LR.C_CurrentFileName, FileName)
            : FileName;

        public string SizeText => FormatSize(Size);

        public static LogFileItem FromFile(string path, bool isCurrent)
        {
            var info = new FileInfo(path);
            return new LogFileItem(info.Name, info.FullName, info.LastWriteTime, info.Length, isCurrent);
        }
    }

    public sealed record LogEntry(
        DateTime Time,
        string Level,
        string Category,
        string Message)
    {
        public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        public string LevelText => FormatLogLevel(Level);
        public string ShortCategory => Category.Split('.').LastOrDefault() ?? Category;
        public string Preview => Message.Replace(Environment.NewLine, " ");
        public string FullText => $"{TimeText} | {LevelText} | {Category}{Environment.NewLine}{Message}";
    }

    public sealed record LogLevelFilterOption(string? Value, string DisplayName);

    private sealed class LogEntryBuilder(DateTime time, string level, string category, string message)
    {
        private readonly StringBuilder _message = new(message);

        public void AppendLine(string line)
        {
            _message.AppendLine();
            _message.Append(line);
        }

        public LogEntry Build()
        {
            return new LogEntry(time, level, category, _message.ToString());
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{size.ToString("0.#", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    private static string FormatLogLevel(string level)
    {
        return level switch
        {
            "Trace" => LR.O_Level_Trace,
            "Debug" => LR.O_Level_Debug,
            "Information" => LR.O_Level_Information,
            "Warning" => LR.O_Level_Warning,
            "Error" => LR.O_Level_Error,
            "Critical" => LR.O_Level_Critical,
            _ => level
        };
    }
}
