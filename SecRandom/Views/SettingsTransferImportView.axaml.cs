using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Services.RosterTransfer;

namespace SecRandom.Views;

public partial class SettingsTransferImportView : UserControl, INotifyPropertyChanged, IDrawerCloseAware
{
    private const int SessionCodeLength = 12;
    private readonly SyncTransferContentType _expectedContentType;
    private readonly RosterCloudTransferMode _mode;
    private readonly Func<SyncTransferPackage, Task<bool>> _completeImportAsync;
    private readonly Func<string, string> _getResource;
    private readonly RosterTransferService _rosterTransferService = IAppHost.GetService<RosterTransferService>();
    private readonly RosterSyncTransferService _syncTransferService = IAppHost.GetService<RosterSyncTransferService>();
    private readonly IRosterQrCameraCaptureFactory _cameraCaptureFactory = IAppHost.GetService<IRosterQrCameraCaptureFactory>();
    private readonly SettingsTransferQrService _offlineQrService = new();
    private readonly SettingsTransferQrService.SettingsTransferQrImportAccumulator _offlineImport;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _sessionCancellation;
    private IRosterQrCameraCapture? _cameraCapture;
    private bool _isScanning;
    private bool _isClosed;
    private bool _isCompleting;
    private bool _isUpdatingSessionCode;
    private bool _hasStartedQrScanner;
    private string _statusText;
    private long _receivedBytes;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public SettingsTransferImportView(SyncTransferContentType expectedContentType, RosterCloudTransferMode mode,
        Func<SyncTransferPackage, Task<bool>> completeImportAsync, Func<string, string> getResource)
    {
        if (expectedContentType is not (SyncTransferContentType.Settings or SyncTransferContentType.AllData))
            throw new ArgumentOutOfRangeException(nameof(expectedContentType));
        if (mode is not (RosterCloudTransferMode.QuickQr or RosterCloudTransferMode.OfflineQr or RosterCloudTransferMode.SessionCode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        _expectedContentType = expectedContentType;
        _mode = mode;
        _completeImportAsync = completeImportAsync ?? throw new ArgumentNullException(nameof(completeImportAsync));
        _getResource = getResource ?? throw new ArgumentNullException(nameof(getResource));
        _offlineImport = _offlineQrService.CreateImportAccumulator();
        _statusText = IsQrMode ? GetResource("C_TransferStatusWaitingForQr") : GetResource("C_TransferStatusReady");
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (IsQrMode && !_hasStartedQrScanner)
            {
                _hasStartedQrScanner = true;
                _ = StartCameraAsync();
            }
        };
    }

    public string Title => _mode switch
    {
        RosterCloudTransferMode.QuickQr => GetResource("C_TransferQuickQrTitle"),
        RosterCloudTransferMode.OfflineQr => GetResource("C_TransferOfflineQrTitle"),
        RosterCloudTransferMode.SessionCode => GetResource("C_TransferSessionCodeTitle"),
        _ => GetResource("C_TransferQuickQrTitle")
    };

    public string Description => _mode == RosterCloudTransferMode.SessionCode
        ? GetResource("C_TransferPortalHint")
        : GetResource("C_TransferScanHint");
    public string TransferMethodLabel => _mode switch
    {
        RosterCloudTransferMode.QuickQr => GetResource("C_TransferQuickQrImport"),
        RosterCloudTransferMode.OfflineQr => GetResource("C_TransferOfflineQrImport"),
        RosterCloudTransferMode.SessionCode => GetResource("C_TransferSessionCodeImport"),
        _ => GetResource("C_TransferQuickQrImport")
    };
    public string CloseLabel => GetResource("C_Close");
    public bool IsQrMode => _mode is RosterCloudTransferMode.QuickQr or RosterCloudTransferMode.OfflineQr;
    public bool IsOfflineQrMode => _mode == RosterCloudTransferMode.OfflineQr;
    public bool IsSessionCodeMode => _mode == RosterCloudTransferMode.SessionCode;
    public bool IsCameraPreviewSupported => _cameraCaptureFactory.IsPreviewSupported;
    public string SessionCodeHint => GetResource("C_TransferPortalHint");
    public string TransferProgressLabel => GetResource("C_TransferProgress");
    public string TransferSpeedLabel => GetResource("C_TransferSpeed");
    public string TransferReceivedLabel => GetResource("C_TransferReceived");
    public string TransferFramesDetailLabel => GetResource("C_TransferFramesDetail");
    public string TransferSessionLabel => GetResource("C_TransferSession");
    public string TransferElapsedLabel => GetResource("C_TransferElapsed");
    public double FrameProgress => _offlineImport.TotalFrames == 0
        ? 0
        : (double)_offlineImport.AcceptedFrames / _offlineImport.TotalFrames;
    public string ProgressText => _offlineImport.TotalFrames == 0 ? "-" : $"{_offlineImport.AcceptedFrames} / {_offlineImport.TotalFrames}";
    public string DecodeSpeedText => _offlineImport.StartedAt == default
        ? "-"
        : string.Format(GetResource("C_TransferFramesPerSecond"), _offlineImport.AcceptedFrames /
            Math.Max(0.1, (DateTimeOffset.UtcNow - _offlineImport.StartedAt).TotalSeconds));
    public string ReceivedText => _offlineImport.PayloadLength == 0
        ? "-"
        : $"{(_offlineImport.PayloadLength * FrameProgress):F0} B / {_offlineImport.PayloadLength} B";
    public string FramesText => $"{_offlineImport.AcceptedFrames}/{_offlineImport.DuplicateFrames}/{_offlineImport.RejectedFrames}";
    public string SessionText => string.IsNullOrWhiteSpace(_offlineImport.SessionId) ? "-" : _offlineImport.SessionId[..8];
    public string ElapsedText => _offlineImport.StartedAt == default
        ? "-"
        : $"{Math.Max(0, (DateTimeOffset.UtcNow - _offlineImport.StartedAt).TotalSeconds):F1} s";
    public string PayloadText => _receivedBytes <= 0 ? string.Empty : FormatBytes(_receivedBytes);
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private async Task StartCameraAsync()
    {
        if (_isClosed || _isScanning)
            return;
        try
        {
            _scanCancellation = new CancellationTokenSource();
            _cameraCapture = _cameraCaptureFactory.Create(CameraControl);
            _cameraCapture.CameraError += CameraCapture_OnCameraError;
            _isScanning = true;
            var result = await _cameraCapture.StartAsync(ProcessCapturedQrImageAsync, _scanCancellation.Token);
            if (result == RosterQrCameraStartResult.PermissionDenied)
            {
                StatusText = string.Format(GetResource("M_TransferFailed"), "Camera permission was denied.");
                await StopCameraAsync(keepStatus: true);
                return;
            }
            StatusText = GetResource("C_TransferStatusWaitingForQr");
        }
        catch (Exception exception)
        {
            StatusText = string.Format(GetResource("M_TransferFailed"), exception.Message);
            await StopCameraAsync(keepStatus: true);
        }
    }

    private async Task ProcessCapturedQrImageAsync(byte[] imageBytes)
    {
        if (!_isScanning || _scanCancellation is null || _isCompleting)
            return;
        try
        {
            await using var stream = new MemoryStream(imageBytes, writable: false);
            var text = await _rosterTransferService.DecodeQrTextAsync(stream, _scanCancellation.Token);
            if (string.IsNullOrWhiteSpace(text))
                return;
            StatusText = GetResource("C_TransferStatusReceivingQr");
            if (_mode == RosterCloudTransferMode.QuickQr)
            {
                var result = await _syncTransferService.ImportQuickPackageAsync(text, _scanCancellation.Token);
                await CompleteAsync(result.Package);
                return;
            }

            var frameResult = _offlineImport.Add(text);
            if (frameResult == SettingsTransferQrFrameImportResult.Rejected)
            {
                StatusText = string.Format(GetResource("M_TransferFailed"), "The QR code is not part of this transfer.");
                return;
            }
            NotifyOfflineProgress();
            if (!_offlineImport.IsComplete)
                return;
            await CompleteAsync(_offlineImport.GetCompletedPackage());
        }
        catch (OperationCanceledException)
        {
            // Closing or stopping the capture cancels its queued decode work.
        }
        catch (Exception exception)
        {
            StatusText = string.Format(GetResource("M_TransferFailed"), exception.Message);
        }
    }

    private async void CameraCapture_OnCameraError(object? sender, string error)
    {
        if (!_isScanning)
            return;
        StatusText = string.Format(GetResource("M_TransferFailed"), error);
        await StopCameraAsync(keepStatus: true);
    }

    private async void SessionCodeInput_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSessionCode || sender is not TextBox textBox || _isCompleting)
            return;
        var normalized = RosterSyncTransferService.NormalizeSessionCode(textBox.Text);
        if (!string.Equals(textBox.Text, normalized, StringComparison.Ordinal))
        {
            _isUpdatingSessionCode = true;
            textBox.Text = normalized;
            _isUpdatingSessionCode = false;
        }
        if (normalized.Length != SessionCodeLength)
            return;

        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;
        try
        {
            StatusText = GetResource("C_TransferStatusReceiving");
            var result = await _syncTransferService.ImportSessionPackageAsync(normalized, cancellation.Token);
            if (!ReferenceEquals(_sessionCancellation, cancellation))
                return;
            await CompleteAsync(result.Package);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Replacing the session code cancels the earlier lookup.
        }
        catch (Exception exception)
        {
            StatusText = string.Format(GetResource("M_TransferFailed"), exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_sessionCancellation, cancellation))
                _sessionCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task CompleteAsync(SyncTransferPackage package)
    {
        if (_isCompleting || _isClosed)
            return;
        if (package.ContentType != _expectedContentType)
        {
            StatusText = GetResource("M_TransferWrongContent");
            return;
        }

        _isCompleting = true;
        _receivedBytes = package.Content.LongLength;
        NotifyOfflineProgress();
        StatusText = GetResource("C_TransferStatusCompleted");
        await StopCameraAsync(keepStatus: true);
        try
        {
            if (await _completeImportAsync(package))
            {
                await ((IDrawerCloseAware)this).OnDrawerClosedAsync();
                SettingsView.Current?.CloseDrawer();
                return;
            }
            StatusText = GetResource("C_TransferStatusReady");
        }
        finally
        {
            _isCompleting = false;
        }
    }

    private async Task StopCameraAsync(bool keepStatus = false)
    {
        var cancellation = _scanCancellation;
        _scanCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        var capture = _cameraCapture;
        _cameraCapture = null;
        if (capture is not null)
        {
            capture.CameraError -= CameraCapture_OnCameraError;
            await capture.DisposeAsync();
        }
        _isScanning = false;
        if (!keepStatus)
            StatusText = GetResource("C_TransferStatusReady");
    }

    private async void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ((IDrawerCloseAware)this).OnDrawerClosedAsync();
        SettingsView.Current?.CloseDrawer();
    }

    async Task IDrawerCloseAware.OnDrawerClosedAsync()
    {
        if (_isClosed)
            return;
        _isClosed = true;
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        await StopCameraAsync(keepStatus: true);
    }

    private void NotifyOfflineProgress()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(FrameProgress), nameof(ProgressText), nameof(DecodeSpeedText), nameof(ReceivedText),
                     nameof(FramesText), nameof(SessionText), nameof(ElapsedText), nameof(PayloadText)
                 })
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    private string GetResource(string key) => _getResource(key);

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes / (1024d * 1024d):F1} MB"
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
