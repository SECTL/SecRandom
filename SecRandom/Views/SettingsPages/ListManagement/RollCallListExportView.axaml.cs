using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MiniExcelLibs;
using SecRandom.Core.Abstraction;
using SecRandom.Services.RosterTransfer;
using SecRandom.Shared.Models.Profile;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.ListManagement.RollCallList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

public partial class RosterListExportView : UserControl, INotifyPropertyChanged, IDrawerCloseAware
{
    private static readonly TimeSpan QrFrameInterval = TimeSpan.FromMilliseconds(200);
    private readonly RosterTransferDocument _document;
    private readonly IReadOnlyList<Dictionary<string, object?>> _fileRows;
    private readonly Func<string, string> _getResource;
    private readonly RosterTransferService _transferService = IAppHost.GetService<RosterTransferService>();
    private readonly RosterSyncTransferService _syncTransferService = IAppHost.GetService<RosterSyncTransferService>();
    private readonly DispatcherTimer _frameTimer;
    private IImage? _currentQrImage;
    private RosterCloudTransferInfo? _cloudTransfer;
    private RosterQrExportSession? _exportSession;
    private RosterExportModeOption _selectedExportMode = null!;
    private int _frameIndex;
    private bool _isGenerating;
    private bool _isQrExporting;
    private bool _isDrawerClosed;
    private string _statusValue;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public RosterListExportView(
        string listName,
        RosterTransferDocument document,
        IReadOnlyList<Dictionary<string, object?>> fileRows,
        Func<string, string> getResource)
    {
        TargetListName = listName;
        _document = document;
        _fileRows = fileRows;
        _getResource = getResource;
        _statusValue = GetResource("C_QrIdle");
        ExportModes =
        [
            new RosterExportModeOption(RosterExportMode.File, GetResource("C_FileExport")),
            new RosterExportModeOption(RosterExportMode.QuickQr, GetResource("C_ModeQuickQr")),
            new RosterExportModeOption(RosterExportMode.OfflineQr, GetResource("C_ModeOfflineQr")),
            new RosterExportModeOption(RosterExportMode.SessionCode, GetResource("C_ModeSessionCode"))
        ];
        _selectedExportMode = ExportModes[0];
        ExampleQrImage = CreateImage(_transferService.CreateExampleQrPng());
        _frameTimer = new DispatcherTimer { Interval = QrFrameInterval };
        _frameTimer.Tick += (_, _) => AdvanceQrFrame();
        DataContext = this;
        InitializeComponent();
    }

    public Action? CloseHandler { get; set; }
    public string TargetListName { get; }
    public string Title => GetResource("C_ExportTitle");
    public string TargetListDescription => string.Format(GetResource("C_TargetList"), TargetListName);
    public string FileExportTitle => GetResource("C_FileExport");
    public string ExportFileButtonText => GetResource("C_ExportToFile");
    public string SupportedFileTypesText => GetResource("C_ExportSupportedFormats");
    public string DeviceExportTitle => SelectedExportMode.Mode == RosterExportMode.SessionCode
        ? GetResource("C_SessionCodeExport")
        : GetResource("C_QrExport");
    public string QrIdleHint => GetResource("C_QrIdleHint");
    public string CloseButtonText => GetResource("C_Cancel");
    public string QrExportButtonText => IsQrExporting ? GetResource("C_StopQrExport") : GetResource("C_ExportToDevices");
    public string ExportModeLabel => GetResource("C_SelectExportMode");
    public IReadOnlyList<RosterExportModeOption> ExportModes { get; }
    public RosterExportModeOption SelectedExportMode
    {
        get => _selectedExportMode;
        set
        {
            if (value is null || ReferenceEquals(_selectedExportMode, value)) return;
            if (IsQrExporting)
                _ = StopQrExportAsync();
            SetField(ref _selectedExportMode, value);
            StatusValue = GetResource("C_QrIdle");
            NotifyStatsChanged();
        }
    }
    public bool IsQrExporting { get => _isQrExporting; private set => SetField(ref _isQrExporting, value); }
    public bool CanToggleQrExport => !_isGenerating;
    public bool IsFileExportMode => SelectedExportMode.Mode == RosterExportMode.File;
    public bool IsDeviceExportMode => !IsFileExportMode;
    public bool IsSessionCodeVisible => IsQrExporting && _cloudTransfer?.Mode == RosterCloudTransferMode.SessionCode;
    public bool IsQrImageVisible => IsDeviceExportMode &&
                                    SelectedExportMode.Mode != RosterExportMode.SessionCode &&
                                    !IsSessionCodeVisible;
    public bool IsOfflineQrExportVisible => IsQrExporting && SelectedExportMode.Mode == RosterExportMode.OfflineQr;
    public double ExportProgress => _exportSession is not null
        ? (double)(_frameIndex + 1) / _exportSession.Frames.Count
        : IsQrExporting && _cloudTransfer is not null ? 1 : 0;
    public IImage ExampleQrImage { get; }
    public IImage? CurrentQrImage { get => _currentQrImage; private set => SetField(ref _currentQrImage, value); }
    public string FramesLabel => GetResource("C_TransferFrames");
    public string SpeedLabel => GetResource("C_TransferSpeed");
    public string PayloadLabel => GetResource("C_TransferSize");
    public string RecordsLabel => GetResource("C_TransferRecords");
    public string SessionLabel => GetResource("C_TransferSession");
    public string StatusLabel => GetResource("C_TransferStatus");
    public string FramesValue => _exportSession is not null
        ? $"{_frameIndex + 1} / {_exportSession.Frames.Count}"
        : IsQrExporting && _cloudTransfer?.Mode == RosterCloudTransferMode.QuickQr ? "1 / 1" : "-";
    public string SpeedValue => IsQrExporting && _exportSession is not null
        ? string.Format(GetResource("C_TransferFramesPerSecond"), 1d / QrFrameInterval.TotalSeconds)
        : "-";
    public string PayloadValue => _exportSession is not null ? FormatBytes(_exportSession.PayloadBytes)
        : _cloudTransfer is not null ? FormatBytes(_cloudTransfer.PayloadBytes) : "-";
    public string RecordsValue => (_exportSession?.RecordCount ?? _cloudTransfer?.RecordCount ?? _document.Rows.Count).ToString(CultureInfo.CurrentCulture);
    public string SessionValue => _cloudTransfer?.SessionCode is { } code ? RosterSyncTransferService.FormatSessionCode(code)
        : _cloudTransfer?.TransferId?[..8] ?? _exportSession?.SessionId[..8] ?? "-";
    public string SessionCodeLabel => GetResource("C_SessionCode");
    public string SessionCodeCopyLabel => GetResource("C_CopySessionCode");
    public string SessionCodeValue => _cloudTransfer?.SessionCode is { } code ? RosterSyncTransferService.FormatSessionCode(code) : "-";
    public string StatusValue { get => _statusValue; private set => SetField(ref _statusValue, value); }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private async void ExportFileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = GetResource("C_FileExport"),
            SuggestedFileName = $"{TargetListName}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("Excel") { Patterns = ["*.xlsx"] },
                new Avalonia.Platform.Storage.FilePickerFileType("CSV") { Patterns = ["*.csv"] }
            ]
        });
        if (file is null)
            return;

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        var temporaryPath = file.TryGetLocalPath() ?? Path.Combine(Path.GetTempPath(), $"SecRandom-{Guid.NewGuid():N}{extension}");
        try
        {
            var rows = CreateFileRows();
            if (extension == ".csv")
                await File.WriteAllTextAsync(temporaryPath, CreateCsv(rows));
            else
                MiniExcel.SaveAs(temporaryPath, rows);

            if (file.TryGetLocalPath() is null)
            {
                await using var source = File.OpenRead(temporaryPath);
                await using var destination = await file.OpenWriteAsync();
                await source.CopyToAsync(destination);
            }

            StatusValue = GetResource("C_FileExported");
        }
        finally
        {
            if (file.TryGetLocalPath() is null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async void ToggleQrExportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IsQrExporting)
        {
            await StopQrExportAsync();
            return;
        }

        _isGenerating = true;
        OnPropertyChanged(nameof(CanToggleQrExport));
        StatusValue = GetResource("C_QrGenerating");
        try
        {
            if (SelectedExportMode.Mode == RosterExportMode.OfflineQr)
                await StartOfflineQrExportAsync();
            else
                await StartCloudExportAsync();
        }
        catch (Exception exception)
        {
            StatusValue = string.Format(GetResource("M_QrTransferFailed"), exception.Message);
        }
        finally
        {
            _isGenerating = false;
            OnPropertyChanged(nameof(CanToggleQrExport));
        }
    }

    private async Task StartOfflineQrExportAsync()
    {
        var exportSession = await _transferService.CreateExportSessionAsync(_document);
        if (_isDrawerClosed)
            return;

        _exportSession = exportSession;
        _frameIndex = 0;
        SetQrImage(_exportSession.Frames[0]);
        IsQrExporting = true;
        StatusValue = GetResource("C_QrExporting");
        NotifyStatsChanged();
        _frameTimer.Start();
    }

    private async Task StartCloudExportAsync()
    {
        var mode = SelectedExportMode.Mode switch
        {
            RosterExportMode.QuickQr => RosterCloudTransferMode.QuickQr,
            RosterExportMode.SessionCode => RosterCloudTransferMode.SessionCode,
            _ => throw new InvalidOperationException("The selected export method does not use cloud transfer.")
        };
        var cloudTransfer = await _syncTransferService.CreateAsync(_document, _fileRows, mode);
        if (_isDrawerClosed)
        {
            await RevokeCloudTransferAsync(cloudTransfer);
            return;
        }

        _cloudTransfer = cloudTransfer;
        if (cloudTransfer.PairingUrl is { } pairingUrl)
            SetQrImage(RosterSyncTransferService.CreatePairingQrPng(pairingUrl));
        IsQrExporting = true;
        StatusValue = GetResource(cloudTransfer.Mode == RosterCloudTransferMode.SessionCode
            ? "C_SessionCodeReady"
            : "C_CloudQrExporting");
        NotifyStatsChanged();
    }

    private void AdvanceQrFrame()
    {
        if (_exportSession is null || _exportSession.Frames.Count == 0)
            return;
        _frameIndex = (_frameIndex + 1) % _exportSession.Frames.Count;
        SetQrImage(_exportSession.Frames[_frameIndex]);
        NotifyStatsChanged();
    }

    private async Task StopQrExportAsync()
    {
        _frameTimer.Stop();
        var cloudTransfer = _cloudTransfer;
        _cloudTransfer = null;
        IsQrExporting = false;
        _exportSession = null;
        _frameIndex = 0;
        var previous = CurrentQrImage as IDisposable;
        CurrentQrImage = null;
        previous?.Dispose();
        StatusValue = GetResource("C_QrStopped");
        NotifyStatsChanged();
        if (cloudTransfer is not null)
            await RevokeCloudTransferAsync(cloudTransfer);
    }

    private async Task RevokeCloudTransferAsync(RosterCloudTransferInfo transfer)
    {
        try
        {
            await _syncTransferService.RevokeAsync(transfer);
        }
        catch
        {
            // The server expiry remains the fallback when the client cannot reach it.
        }
    }

    private void SetQrImage(byte[] png)
    {
        var previous = CurrentQrImage as IDisposable;
        CurrentQrImage = CreateImage(png);
        previous?.Dispose();
    }

    private async void CopySessionCode_OnClick(object? sender, RoutedEventArgs e)
    {
        var code = _cloudTransfer?.SessionCode;
        if (string.IsNullOrWhiteSpace(code))
            return;
        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(RosterSyncTransferService.NormalizeSessionCode(code))
            ?? Task.CompletedTask);
    }

    private IReadOnlyList<Dictionary<string, object?>> CreateFileRows() => _fileRows;

    private static string CreateCsv(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var headers = rows.FirstOrDefault()?.Keys.ToArray() ?? [];
        var lines = new List<string> { string.Join(',', headers.Select(EscapeCsv)) };
        lines.AddRange(rows.Select(row => string.Join(',', headers.Select(header => EscapeCsv(row[header]?.ToString() ?? string.Empty)))));
        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static Bitmap CreateImage(byte[] png) => new(new MemoryStream(png));
    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes / (1024d * 1024d):F1} MB"
    };
    protected string GetResource(string name) => _getResource(name);

    private async void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ((IDrawerCloseAware)this).OnDrawerClosedAsync();
        (ExampleQrImage as IDisposable)?.Dispose();
        if (CloseHandler is not null)
            CloseHandler();
        else
            SettingsView.Current?.CloseDrawer();
    }

    async Task IDrawerCloseAware.OnDrawerClosedAsync()
    {
        _isDrawerClosed = true;
        await StopQrExportAsync();
    }

    private void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(ExportProgress));
        OnPropertyChanged(nameof(FramesValue));
        OnPropertyChanged(nameof(SpeedValue));
        OnPropertyChanged(nameof(PayloadValue));
        OnPropertyChanged(nameof(RecordsValue));
        OnPropertyChanged(nameof(SessionValue));
        OnPropertyChanged(nameof(QrExportButtonText));
        OnPropertyChanged(nameof(IsFileExportMode));
        OnPropertyChanged(nameof(IsDeviceExportMode));
        OnPropertyChanged(nameof(IsSessionCodeVisible));
        OnPropertyChanged(nameof(IsQrImageVisible));
        OnPropertyChanged(nameof(IsOfflineQrExportVisible));
        OnPropertyChanged(nameof(SessionCodeValue));
        OnPropertyChanged(nameof(DeviceExportTitle));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
