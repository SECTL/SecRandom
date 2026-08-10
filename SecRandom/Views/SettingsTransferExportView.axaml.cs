using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Helpers;
using SecRandom.Services.RosterTransfer;

namespace SecRandom.Views;

public partial class SettingsTransferExportView : UserControl, INotifyPropertyChanged, IDrawerCloseAware
{
private static readonly TimeSpan QrFrameInterval = TimeSpan.FromMilliseconds(150);
    private const string SyncPortalAddressValue = "secrandom-sync.sectl.cn";
    private readonly SyncTransferPackage _package;
    private readonly RosterCloudTransferMode _mode;
    private readonly Func<string, string> _getResource;
    private readonly RosterSyncTransferService _syncTransferService = IAppHost.GetService<RosterSyncTransferService>();
    private readonly SettingsTransferQrService _offlineQrService = new();
    private readonly DispatcherTimer _frameTimer;
    private RosterCloudTransferInfo? _cloudTransfer;
    private SettingsTransferQrExportSession? _offlineExport;
    private IImage? _currentQrImage;
    private int _frameIndex;
    private bool _hasStarted;
    private bool _isClosed;
    private string _statusText = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public SettingsTransferExportView(SyncTransferPackage package, RosterCloudTransferMode mode,
        Func<string, string> getResource)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _mode = mode;
        _getResource = getResource ?? throw new ArgumentNullException(nameof(getResource));
        _statusText = GetResource("C_TransferStatusReady");
        _frameTimer = new DispatcherTimer { Interval = QrFrameInterval };
        _frameTimer.Tick += (_, _) => AdvanceFrame();
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => _ = StartAsync();
    }

    public string Title => _mode switch
    {
        RosterCloudTransferMode.QuickQr => GetResource("C_TransferQuickQrTitle"),
        RosterCloudTransferMode.OfflineQr => GetResource("C_TransferOfflineQrTitle"),
        RosterCloudTransferMode.SessionCode => GetResource("C_TransferSessionCodeTitle"),
        _ => GetResource("C_TransferQuickQrTitle")
    };

    public string Description => GetResource("C_TransferExportHint");
    public string TransferMethodLabel => _mode switch
    {
        RosterCloudTransferMode.QuickQr => GetResource("C_TransferQuickQrExport"),
        RosterCloudTransferMode.OfflineQr => GetResource("C_TransferOfflineQrExport"),
        RosterCloudTransferMode.SessionCode => GetResource("C_TransferSessionCodeExport"),
        _ => GetResource("C_TransferQuickQrExport")
    };
    public string CloseLabel => GetResource("C_Close");
    public string CopySessionCodeLabel => GetResource("C_CopySessionCode");
    public string CopySyncPortalAddressLabel => GetResource("C_TransferCopyAddress");
    public string SessionCodeLabel => GetResource("C_TransferSessionCodeLabel");
    public IImage? CurrentQrImage { get => _currentQrImage; private set => SetField(ref _currentQrImage, value); }
    public bool IsQrImageVisible => _mode != RosterCloudTransferMode.SessionCode && CurrentQrImage is not null;
    public bool IsSessionCodeVisible => _cloudTransfer?.SessionCode is not null;
    public bool IsCloudTransferLinkVisible => _cloudTransfer is not null;
    public bool IsOfflineQrVisible => _offlineExport is not null;
    public double FrameProgress => _offlineExport is null || _offlineExport.Frames.Count == 0
        ? 0
        : (double)(_frameIndex + 1) / _offlineExport.Frames.Count;
    public string FramesLabel => GetResource("C_TransferFrames");
    public string SpeedLabel => GetResource("C_TransferSpeed");
    public string PayloadLabel => GetResource("C_TransferSize");
    public string SessionLabel => GetResource("C_TransferSession");
    public string StatusLabel => GetResource("C_TransferStatus");
    public string FramesValue => _offlineExport is null ? "-" : $"{_frameIndex + 1} / {_offlineExport.Frames.Count}";
    public string SpeedValue => _offlineExport is null
        ? "-"
        : string.Format(GetResource("C_TransferFramesPerSecond"), 1d / QrFrameInterval.TotalSeconds);
    public string PayloadValue => PayloadText;
    public string SessionValue => _cloudTransfer?.SessionCode is { } code
        ? RosterSyncTransferService.FormatSessionCode(code)
        : _cloudTransfer?.TransferId?[..8] ?? (_offlineExport is not null ? GetResource("C_TransferLocal") : "-");
    public string SessionCode => _cloudTransfer?.SessionCode is { } code ? RosterSyncTransferService.FormatSessionCode(code) : string.Empty;
    public string SyncPortalAddress => SyncPortalAddressValue;
    public string SyncPortalDescription => GetResource("C_TransferPortalHint");
    public string PayloadText => FormatBytes(_offlineExport is not null ? _package.Content.LongLength : _cloudTransfer?.PayloadBytes ?? 0);
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private async Task StartAsync()
    {
        if (_hasStarted || _isClosed)
            return;
        _hasStarted = true;
        try
        {
            if (_mode == RosterCloudTransferMode.OfflineQr)
            {
                _offlineExport = await _offlineQrService.CreateExportSessionAsync(_package);
                if (_isClosed || _offlineExport.Frames.Count == 0)
                    return;
                _frameIndex = 0;
                SetQrImage(_offlineExport.Frames[0]);
                _frameTimer.Start();
                StatusText = GetResource("C_TransferStatusSending");
            }
            else
            {
                _cloudTransfer = await _syncTransferService.CreateFileAsync(_package, _mode);
                if (_isClosed)
                {
                    await RevokeCloudTransferAsync(_cloudTransfer);
                    return;
                }
                if (_cloudTransfer.PairingUrl is { } pairingUrl)
                    SetQrImage(RosterSyncTransferService.CreatePairingQrPng(pairingUrl));
                StatusText = GetResource("C_TransferStatusReady");
            }
        }
        catch (Exception exception)
        {
            StatusText = string.Format(GetResource("M_TransferFailed"), exception.Message);
        }
        finally
        {
            NotifyTransferStateChanged();
        }
    }

    private void AdvanceFrame()
    {
        if (_offlineExport is null || _offlineExport.Frames.Count == 0)
            return;
        _frameIndex = (_frameIndex + 1) % _offlineExport.Frames.Count;
        SetQrImage(_offlineExport.Frames[_frameIndex]);
        NotifyTransferStateChanged();
    }

    private void SetQrImage(byte[] png)
    {
        var previous = CurrentQrImage as IDisposable;
        CurrentQrImage = new Bitmap(new MemoryStream(png));
        if (previous is not null)
            ImageSourceLifetime.DisposeAfterRender(previous);
    }

    private async void CopySessionCodeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SessionCode))
            return;
        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(SessionCode) ?? Task.CompletedTask);
    }

    private async void CopySyncPortalAddressButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(SyncPortalAddress) ?? Task.CompletedTask);
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
        _frameTimer.Stop();
        var transfer = _cloudTransfer;
        _cloudTransfer = null;
        if (transfer is not null)
            await RevokeCloudTransferAsync(transfer);
        var image = CurrentQrImage as IDisposable;
        CurrentQrImage = null;
        if (image is not null)
            ImageSourceLifetime.DisposeAfterRender(image);
    }

    private async Task RevokeCloudTransferAsync(RosterCloudTransferInfo transfer)
    {
        try
        {
            await _syncTransferService.RevokeAsync(transfer);
        }
        catch
        {
            // Server-side expiry remains the cleanup fallback.
        }
    }

    private void NotifyTransferStateChanged()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(IsQrImageVisible), nameof(IsSessionCodeVisible), nameof(IsCloudTransferLinkVisible),
                     nameof(IsOfflineQrVisible), nameof(FrameProgress), nameof(FramesValue), nameof(SpeedValue),
                     nameof(PayloadText), nameof(PayloadValue), nameof(SessionValue), nameof(SessionCode)
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
