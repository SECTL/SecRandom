using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MiniExcelLibs;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Services.Notification;
using SecRandom.Helpers;
using SecRandom.Services.RosterTransfer;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.Notification.Texts.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

/// <summary>
/// Exports the fullscreen result template library as a spreadsheet, a JSON file, or a device transfer
/// (quick QR, offline QR, session code) that the import drawer on another device can read back.
/// </summary>
public partial class TextLibraryExportDrawer : UserControl, INotifyPropertyChanged, IDrawerCloseAware
{
    private const string PackageFileName = "text-library.json";
    private static readonly TimeSpan QrFrameInterval = TimeSpan.FromMilliseconds(150);

    private readonly TextLibraryService _textLibraryService;
    private readonly RosterTransferService _transferService = IAppHost.GetService<RosterTransferService>();
    private readonly RosterSyncTransferService _syncTransferService = IAppHost.GetService<RosterSyncTransferService>();
    private readonly SettingsTransferQrService _settingsQrService = new();
    private readonly DispatcherTimer _frameTimer;

    private TextLibraryExportModeOption _selectedExportMode;
    private SettingsTransferQrExportSession? _exportSession;
    private RosterCloudTransferInfo? _cloudTransfer;
    private CancellationTokenSource? _exportCancellationTokenSource;
    private IImage? _currentQrImage;
    private string _statusText;
    private int _frameIndex;
    private bool _isGenerating;
    private bool _isQrExporting;
    private bool _isDrawerClosed;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public TextLibraryExportDrawer() : this(IAppHost.GetService<TextLibraryService>())
    {
    }

    public TextLibraryExportDrawer(TextLibraryService service)
    {
        _textLibraryService = service;
        ExportModes =
        [
            new TextLibraryExportModeOption(TextLibraryExportMode.File, Text("M_ExportFile")),
            new TextLibraryExportModeOption(TextLibraryExportMode.QuickQr, Text("M_ExportQuickQr")),
            new TextLibraryExportModeOption(TextLibraryExportMode.OfflineQr, Text("M_ExportOfflineQr")),
            new TextLibraryExportModeOption(TextLibraryExportMode.SessionCode, Text("M_ExportSessionCode"))
        ];
        _selectedExportMode = ExportModes[0];
        _statusText = Text("M_ExportHintIdle");
        try
        {
            ExampleQrImage = new Bitmap(new MemoryStream(_transferService.CreateExampleQrPng()));
        }
        catch (Exception)
        {
            // The placeholder is cosmetic: a device without image support still gets a usable drawer.
            ExampleQrImage = null;
        }
        _frameTimer = new DispatcherTimer { Interval = QrFrameInterval };
        _frameTimer.Tick += (_, _) => AdvanceQrFrame();
        DataContext = this;
        InitializeComponent();
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    public IReadOnlyList<TextLibraryExportModeOption> ExportModes { get; }

    public TextLibraryExportModeOption SelectedExportMode
    {
        get => _selectedExportMode;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedExportMode) ||
                value.Value == _selectedExportMode.Value)
                return;
            if (IsQrExporting)
                _ = StopQrExportAsync();
            _selectedExportMode = value;
            OnPropertyChanged(nameof(SelectedExportMode));
            StatusText = Text("M_ExportHintIdle");
            NotifyStatsChanged();
        }
    }

    public string TargetDescription => string.Format(Text("M_ExportHint"), _textLibraryService.Entries.Count);

    public bool IsFileExportMode => _selectedExportMode.Value == TextLibraryExportMode.File;

    public bool IsDeviceExportMode => !IsFileExportMode;

    public bool CanExport => !_isGenerating && _textLibraryService.Entries.Count > 0;

    public bool CanToggleExport => CanExport;

    public bool IsQrExporting
    {
        get => _isQrExporting;
        private set
        {
            if (_isQrExporting == value)
                return;
            _isQrExporting = value;
            OnPropertyChanged(nameof(IsQrExporting));
            OnPropertyChanged(nameof(QrExportButtonText));
        }
    }

    public string QrExportButtonText => IsQrExporting ? Text("C_StopExport") : Text("C_StartExport");

    public string TransferHintText => _selectedExportMode.Value switch
    {
        TextLibraryExportMode.QuickQr => Text("M_QuickQrExportHint"),
        TextLibraryExportMode.OfflineQr => Text("M_OfflineQrExportHint"),
        TextLibraryExportMode.SessionCode => Text("M_SessionCodeExportHint"),
        _ => Text("M_ExportFileHint")
    };

    public IImage? ExampleQrImage { get; }

    public IImage? CurrentQrImage
    {
        get => _currentQrImage;
        private set
        {
            if (ReferenceEquals(_currentQrImage, value))
                return;
            _currentQrImage = value;
            OnPropertyChanged(nameof(CurrentQrImage));
        }
    }

    public bool IsQrImageVisible => IsDeviceExportMode && !IsSessionCodeVisible;

    public bool IsSessionCodeVisible =>
        IsQrExporting && _cloudTransfer?.Mode == RosterCloudTransferMode.SessionCode;

    public bool IsOfflineQrExportVisible =>
        IsQrExporting && _selectedExportMode.Value == TextLibraryExportMode.OfflineQr;

    public double ExportProgress => _exportSession is not null && _exportSession.Frames.Count > 0
        ? (double)(_frameIndex + 1) / _exportSession.Frames.Count
        : IsQrExporting && _cloudTransfer is not null ? 1 : 0;

    public string FramesValue => _exportSession is not null
        ? string.Format(Text("M_ExportFrameStats"), _frameIndex + 1, _exportSession.Frames.Count)
        : "-";

    public string SessionCodeValue => _cloudTransfer?.SessionCode is { } code
        ? RosterSyncTransferService.FormatSessionCode(code)
        : "-";

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (string.Equals(_statusText, value, StringComparison.Ordinal))
                return;
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private async void ExportFileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var entries = _textLibraryService.Entries;
        if (entries.Count == 0)
        {
            StatusText = Text("M_ExportEmpty");
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Text("M_ExportFile"),
            SuggestedFileName = $"{Text("M_ExportFileName")}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType(Text("M_ExportFileTypeExcel")) { Patterns = ["*.xlsx"] },
                new FilePickerFileType(Text("M_ExportFileTypeJson")) { Patterns = ["*.json"] }
            ]
        });
        if (file is null)
            return;

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        var temporaryPath = file.TryGetLocalPath()
                            ?? Path.Combine(Path.GetTempPath(), $"SecRandom-{Guid.NewGuid():N}{extension}");
        try
        {
            if (extension == ".json")
                await File.WriteAllBytesAsync(temporaryPath, BuildPackage().Content);
            else
                MiniExcel.SaveAs(temporaryPath, BuildFileRows(entries));

            if (file.TryGetLocalPath() is null)
            {
                await using var source = File.OpenRead(temporaryPath);
                await using var destination = await file.OpenWriteAsync();
                await source.CopyToAsync(destination);
            }

            StatusText = string.Format(Text("M_ExportDoneFile"), entries.Count);
            this.ShowSuccessToast(StatusText);
        }
        catch (Exception exception)
        {
            StatusText = string.Format(Text("M_ExportFailed"), exception.Message);
            this.ShowErrorToast(Text("M_ExportFailedTitle"), exception);
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

        if (_textLibraryService.Entries.Count == 0)
        {
            StatusText = Text("M_ExportEmpty");
            return;
        }

        _isGenerating = true;
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanToggleExport));
        StatusText = Text("M_ExportGenerating");
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _exportCancellationTokenSource, cancellation)?.Dispose();
        try
        {
            var package = BuildPackage();
            if (_selectedExportMode.Value == TextLibraryExportMode.OfflineQr)
                await StartOfflineQrExportAsync(package, cancellation.Token);
            else
                await StartCloudExportAsync(package, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Closing the drawer or switching modes superseded this export.
        }
        catch (Exception exception)
        {
            StatusText = string.Format(Text("M_ExportFailed"), exception.Message);
            this.ShowErrorToast(Text("M_ExportFailedTitle"), exception);
        }
        finally
        {
            _isGenerating = false;
            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(CanToggleExport));
        }
    }

    private async Task StartOfflineQrExportAsync(SyncTransferPackage package, CancellationToken cancellationToken)
    {
        var exportSession = await _settingsQrService.CreateExportSessionAsync(package, cancellationToken);
        if (_isDrawerClosed || cancellationToken.IsCancellationRequested)
            return;

        _exportSession = exportSession;
        _frameIndex = 0;
        SetQrImage(exportSession.Frames[0]);
        IsQrExporting = true;
        StatusText = Text("M_OfflineQrExporting");
        NotifyStatsChanged();
        _frameTimer.Start();
    }

    private async Task StartCloudExportAsync(SyncTransferPackage package, CancellationToken cancellationToken)
    {
        var mode = _selectedExportMode.Value switch
        {
            TextLibraryExportMode.QuickQr => RosterCloudTransferMode.QuickQr,
            TextLibraryExportMode.SessionCode => RosterCloudTransferMode.SessionCode,
            _ => throw new InvalidOperationException("The selected export method does not use cloud transfer.")
        };
        var cloudTransfer = await _syncTransferService.CreateFileAsync(package, mode, cancellationToken);
        if (_isDrawerClosed || cancellationToken.IsCancellationRequested)
        {
            await RevokeCloudTransferAsync(cloudTransfer);
            return;
        }

        _cloudTransfer = cloudTransfer;
        if (cloudTransfer.PairingUrl is { } pairingUrl)
            SetQrImage(RosterSyncTransferService.CreatePairingQrPng(pairingUrl));
        IsQrExporting = true;
        StatusText = Text(cloudTransfer.Mode == RosterCloudTransferMode.SessionCode
            ? "M_SessionCodeReady"
            : "M_QuickQrReady");
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
        if (previous is not null)
            ImageSourceLifetime.DisposeAfterRender(previous);
        StatusText = Text("M_ExportQrStopped");
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
        catch (Exception)
        {
            // The server-side expiry remains the fallback when the client cannot reach it.
        }
    }

    private void SetQrImage(byte[] png)
    {
        var previous = CurrentQrImage as IDisposable;
        CurrentQrImage = new Bitmap(new MemoryStream(png));
        if (previous is not null)
            ImageSourceLifetime.DisposeAfterRender(previous);
    }

    private async void CopySessionCode_OnClick(object? sender, RoutedEventArgs e)
    {
        var code = _cloudTransfer?.SessionCode;
        if (string.IsNullOrWhiteSpace(code))
            return;
        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(RosterSyncTransferService.NormalizeSessionCode(code))
               ?? Task.CompletedTask);
    }

    private async void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ((IDrawerCloseAware)this).OnDrawerClosedAsync();
        if (ExampleQrImage is IDisposable exampleQrImage)
            ImageSourceLifetime.DisposeAfterRender(exampleQrImage);
        SettingsView.Current?.CloseDrawer();
    }

    async Task IDrawerCloseAware.OnDrawerClosedAsync()
    {
        if (_isDrawerClosed)
            return;
        _isDrawerClosed = true;
        var cancellation = Interlocked.Exchange(ref _exportCancellationTokenSource, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        await StopQrExportAsync();
    }

    /// <summary>
    ///     The device-transfer package must stay readable by <see cref="TextLibraryImporter.ParseJson" />,
    ///     so it carries the same document shape the library file uses (snake_case via
    ///     <see cref="ConfigServiceBase.JsonOptions" />).
    /// </summary>
    private SyncTransferPackage BuildPackage()
    {
        var document = new TextLibraryDocument
        {
            FormatVersion = 1,
            Texts = _textLibraryService.Entries.ToList()
        };
        var json = JsonSerializer.Serialize(document, ConfigServiceBase.JsonOptions);
        return new SyncTransferPackage(SyncTransferContentType.TextLibrary, PackageFileName,
            Encoding.UTF8.GetBytes(json));
    }

    private List<Dictionary<string, object?>> BuildFileRows(IReadOnlyList<TextLibraryEntry> entries)
    {
        var header = Text("C_Text");
        return entries.Select(entry => new Dictionary<string, object?> { [header] = entry.Text }).ToList();
    }

    private void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(IsFileExportMode));
        OnPropertyChanged(nameof(IsDeviceExportMode));
        OnPropertyChanged(nameof(TransferHintText));
        OnPropertyChanged(nameof(IsQrImageVisible));
        OnPropertyChanged(nameof(IsSessionCodeVisible));
        OnPropertyChanged(nameof(IsOfflineQrExportVisible));
        OnPropertyChanged(nameof(ExportProgress));
        OnPropertyChanged(nameof(FramesValue));
        OnPropertyChanged(nameof(SessionCodeValue));
        OnPropertyChanged(nameof(QrExportButtonText));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanToggleExport));
    }

    private void OnPropertyChanged(string propertyName) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string Text(string key) => LR.ResourceManager.GetString(key, LR.Culture) ?? key;
}

public enum TextLibraryExportMode
{
    File,
    QuickQr,
    OfflineQr,
    SessionCode
}

public sealed record TextLibraryExportModeOption(TextLibraryExportMode Value, string Label);
