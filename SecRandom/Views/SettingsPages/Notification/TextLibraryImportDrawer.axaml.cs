using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using MiniExcelLibs;
using SecRandom.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Services.Notification;
using SecRandom.Services.RosterTransfer;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.Notification.Texts.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

/// <summary>
/// Imports fullscreen result templates from a spreadsheet column, pasted JSON, or another SecRandom device
/// through the shared roster transfer channels (quick QR, offline QR, session code).
/// </summary>
public partial class TextLibraryImportDrawer : UserControl, INotifyPropertyChanged, IDrawerCloseAware
{
    private const string TableFileTypeName = "TextLibraryTable";
    private static readonly string[] HeaderCells = ["文案", "文本", "text", "template"];

    private readonly TextLibraryService _textLibraryService;
    private readonly RosterTransferService _transferService = IAppHost.GetService<RosterTransferService>();
    private readonly RosterSyncTransferService _syncTransferService = IAppHost.GetService<RosterSyncTransferService>();
    private readonly IRosterQrCameraCaptureFactory _qrCameraCaptureFactory =
        IAppHost.GetService<IRosterQrCameraCaptureFactory>();
    private readonly SettingsTransferQrService _settingsQrService = new();

    private TextLibraryImportModeOption _selectedImportModeOption;
    private TextLibraryTransferModeOption _selectedTransferModeOption;
    private IReadOnlyList<string> _pendingTemplates = [];
    private SettingsTransferQrService.SettingsTransferQrImportAccumulator? _offlineQrImport;
    private IRosterQrCameraCapture? _qrCameraCapture;
    private CancellationTokenSource? _qrScanCancellationTokenSource;
    private CancellationTokenSource? _sessionCodeCancellationTokenSource;
    private string? _temporaryFilePath;
    private string _statusText = string.Empty;
    private string _jsonText = string.Empty;
    private string _jsonStatusText = string.Empty;
    private string _jsonErrorText = string.Empty;
    private bool _jsonStatusIsError;
    private bool _isScanning;
    private bool _isBusy;
    private bool _isDrawerClosed;

    public TextLibraryImportDrawer() : this(IAppHost.GetService<TextLibraryService>())
    {
    }

    public TextLibraryImportDrawer(TextLibraryService textLibraryService)
    {
        _textLibraryService = textLibraryService;
        ImportModes =
        [
            new TextLibraryImportModeOption(TextLibraryImportMode.Table, Text("M_SourceTable")),
            new TextLibraryImportModeOption(TextLibraryImportMode.Json, Text("M_SourceJson")),
            new TextLibraryImportModeOption(TextLibraryImportMode.Transfer, Text("M_SourceTransfer"))
        ];
        TransferModes =
        [
            new TextLibraryTransferModeOption(TextLibraryTransferMode.QuickQr, Text("M_TransferQuickQr")),
            new TextLibraryTransferModeOption(TextLibraryTransferMode.OfflineQr, Text("M_TransferOfflineQr")),
            new TextLibraryTransferModeOption(TextLibraryTransferMode.SessionCode, Text("M_TransferSessionCode"))
        ];
        _selectedImportModeOption = ImportModes[0];
        _selectedTransferModeOption = TransferModes[0];
        _statusText = Text("M_ImportHint");
        DataContext = this;
        InitializeComponent();
    }

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    public IReadOnlyList<TextLibraryImportModeOption> ImportModes { get; }

    public IReadOnlyList<TextLibraryTransferModeOption> TransferModes { get; }

    public TextLibraryImportModeOption SelectedImportModeOption
    {
        get => _selectedImportModeOption;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedImportModeOption) ||
                value.Value == _selectedImportModeOption.Value)
                return;
            _selectedImportModeOption = value;
            OnPropertyChanged(nameof(SelectedImportModeOption));
            NotifyModeChanged();
            _ = StopQrScannerAsync();
        }
    }

    public TextLibraryTransferModeOption SelectedTransferModeOption
    {
        get => _selectedTransferModeOption;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedTransferModeOption) ||
                value.Value == _selectedTransferModeOption.Value)
                return;
            _selectedTransferModeOption = value;
            OnPropertyChanged(nameof(SelectedTransferModeOption));
            NotifyTransferModeChanged();
            _ = StopQrScannerAsync();
        }
    }

    public bool IsTableMode => _selectedImportModeOption.Value == TextLibraryImportMode.Table;

    public bool IsJsonMode => _selectedImportModeOption.Value == TextLibraryImportMode.Json;

    public bool IsTransferMode => _selectedImportModeOption.Value == TextLibraryImportMode.Transfer;

    public bool IsQuickQrMode => _selectedTransferModeOption.Value == TextLibraryTransferMode.QuickQr;

    public bool IsOfflineQrMode => _selectedTransferModeOption.Value == TextLibraryTransferMode.OfflineQr;

    public bool IsSessionCodeMode => _selectedTransferModeOption.Value == TextLibraryTransferMode.SessionCode;

    public bool IsCameraPreviewSupported => _qrCameraCaptureFactory.IsPreviewSupported;

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value)
                return;
            _isScanning = value;
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(ScanButtonText));
        }
    }

    public string ScanButtonText => IsScanning ? Text("C_StopScan") : Text("C_StartScan");

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;
            _isBusy = value;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
                return;
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string JsonText
    {
        get => _jsonText;
        set
        {
            if (_jsonText == value)
                return;
            _jsonText = value ?? string.Empty;
            OnPropertyChanged(nameof(JsonText));
            OnPropertyChanged(nameof(JsonCountText));
            ValidateJson();
        }
    }

    public string JsonCountText => string.Format(Text("C_JsonCountFormat"), _jsonText.Length,
        TextLibraryImporter.MaxJsonImportChars);

    public string JsonStatusText
    {
        get => _jsonStatusText;
        private set
        {
            if (_jsonStatusText == value)
                return;
            _jsonStatusText = value;
            OnPropertyChanged(nameof(JsonStatusText));
        }
    }

    public string JsonErrorText
    {
        get => _jsonErrorText;
        private set
        {
            if (_jsonErrorText == value)
                return;
            _jsonErrorText = value;
            OnPropertyChanged(nameof(JsonErrorText));
        }
    }

    public bool JsonStatusIsError
    {
        get => _jsonStatusIsError;
        private set
        {
            if (_jsonStatusIsError == value)
                return;
            _jsonStatusIsError = value;
            OnPropertyChanged(nameof(JsonStatusIsError));
        }
    }

    public string SessionCode
    {
        get => this.FindControl<TextBox>("SessionCodeInput")?.Text ?? string.Empty;
        set
        {
            var textBox = this.FindControl<TextBox>("SessionCodeInput");
            if (textBox is not null && textBox.Text != value)
                textBox.Text = value;
        }
    }

    public string OfflineQrStatsText { get; private set; } = string.Empty;

    public ObservableCollection<string> PreviewTexts { get; } = [];

    public bool HasPreview => PreviewTexts.Count > 0;

    public string PreviewSummary { get; private set; } = string.Empty;

    public bool CanImport => _pendingTemplates.Count > 0 && !IsBusy;

    internal static string Text(string key) => LR.ResourceManager.GetString(key, LR.Culture) ?? key;

    private void OnPropertyChanged(string propertyName) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(IsTableMode));
        OnPropertyChanged(nameof(IsJsonMode));
        OnPropertyChanged(nameof(IsTransferMode));
    }

    private void NotifyTransferModeChanged()
    {
        OnPropertyChanged(nameof(IsQuickQrMode));
        OnPropertyChanged(nameof(IsOfflineQrMode));
        OnPropertyChanged(nameof(IsSessionCodeMode));
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(CanImport));
    }

    private void SetPendingTemplates(IReadOnlyList<string> templates, string summary)
    {
        _pendingTemplates = templates;
        PreviewTexts.Clear();
        foreach (var template in templates.Take(20))
            PreviewTexts.Add(template);
        PreviewSummary = summary;
        NotifyPreviewChanged();
    }

    private void ClearPendingTemplates()
    {
        _pendingTemplates = [];
        PreviewTexts.Clear();
        PreviewSummary = string.Empty;
        NotifyPreviewChanged();
    }

    private void ValidateJson()
    {
        if (string.IsNullOrWhiteSpace(_jsonText))
        {
            JsonStatusText = Text("M_JsonEmpty");
            JsonStatusIsError = false;
            JsonErrorText = string.Empty;
            ClearPendingTemplates();
            return;
        }

        if (_jsonText.Length > TextLibraryImporter.MaxJsonImportChars)
        {
            JsonStatusText = string.Format(Text("M_JsonTooLong"), TextLibraryImporter.MaxJsonImportChars);
            JsonStatusIsError = true;
            JsonErrorText = string.Empty;
            ClearPendingTemplates();
            return;
        }

        var result = TextLibraryImporter.ParseJson(_jsonText);
        if (!result.Success)
        {
            JsonStatusText = Text("M_JsonInvalid");
            JsonStatusIsError = true;
            JsonErrorText = string.Join(Environment.NewLine, result.Errors.Take(6));
            ClearPendingTemplates();
            return;
        }

        JsonStatusText = string.Format(Text("M_JsonValid"), result.Templates.Count);
        JsonStatusIsError = false;
        JsonErrorText = result.Errors.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, result.Errors.Take(6));
        SetPendingTemplates(result.Templates, string.Format(Text("M_PreviewCount"), result.Templates.Count));
    }

    private async void FileImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Text("C_SelectFile"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(TableFileTypeName)
                    {
                        Patterns = ["*.xlsx", "*.xls", "*.csv"]
                    }
                ]
            });
            if (files.Count == 0)
                return;
            await LoadTableFileAsync(files[0]);
        }
        catch (Exception exception)
        {
            StatusText = Text("M_ImportFailed");
            this.ShowErrorToast(Text("M_ImportFailed"), exception);
        }
    }

    private async Task LoadTableFileAsync(IStorageFile file)
    {
        StatusText = Text("M_TableReading");
        var path = await CopyToTemporaryFileAsync(file);
        if (string.IsNullOrEmpty(path))
        {
            StatusText = Text("M_TableReadFailed");
            return;
        }

        try
        {
            var cells = ReadFirstColumn(path);
            if (cells.Count > 0 && HeaderCells.Contains(cells[0].Trim(), StringComparer.OrdinalIgnoreCase))
                cells = cells.Skip(1).ToList();

            var result = TextLibraryImporter.FromCells(cells);
            if (!result.Success)
            {
                StatusText = Text("M_ImportEmptySelection");
                ClearPendingTemplates();
                return;
            }

            SetPendingTemplates(result.Templates,
                string.Format(Text("M_TableLoaded"), file.Name, result.Templates.Count));
            StatusText = string.Format(Text("M_TableLoaded"), file.Name, result.Templates.Count);
        }
        catch (Exception exception)
        {
            StatusText = Text("M_TableReadFailed");
            this.ShowErrorToast(Text("M_TableReadFailed"), exception);
        }
    }

    private static List<string> ReadFirstColumn(string path)
    {
        var cells = new List<string>();
        string? sheetName = null;
        if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            var sheets = MiniExcel.GetSheetNames(path).ToList();
            sheetName = sheets.Count > 0 ? sheets[0] : null;
        }

        foreach (var row in MiniExcel.Query(path, useHeaderRow: false, sheetName: sheetName))
        {
            if (row is not IDictionary<string, object?> values)
                continue;
            values.TryGetValue("A", out var cell);
            cells.Add(cell?.ToString() ?? string.Empty);
        }

        return cells;
    }

    private async Task<string?> CopyToTemporaryFileAsync(IStorageFile file)
    {
        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            return localPath;

        try
        {
            var temporaryPath = Path.Combine(Path.GetTempPath(),
                $"secrandom-text-library-{Guid.NewGuid():N}{Path.GetExtension(file.Name)}");
            await using var destination = File.Create(temporaryPath);
            await using var source = await file.OpenReadAsync();
            await source.CopyToAsync(destination);
            _temporaryFilePath = temporaryPath;
            return temporaryPath;
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(Text("M_TableReadFailed"), exception);
            return null;
        }
    }

    private void ReleaseTemporaryFile()
    {
        if (_temporaryFilePath is not { } path)
            return;
        _temporaryFilePath = null;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A locked temporary file is removed by the next import attempt.
        }
    }

    private async void ScanButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isDrawerClosed || !IsTransferMode)
            return;
        if (IsScanning)
        {
            await StopQrScannerAsync();
            return;
        }

        try
        {
            _qrScanCancellationTokenSource = new CancellationTokenSource();
            IsScanning = true;
            var cameraCapture = _qrCameraCaptureFactory.Create(CameraControl, null);
            cameraCapture.CameraError += CameraCapture_OnCameraError;
            _qrCameraCapture = cameraCapture;
            var startResult = await cameraCapture.StartAsync(ProcessCameraFrameAsync,
                _qrScanCancellationTokenSource.Token);
            if (_isDrawerClosed)
            {
                await StopQrScannerAsync();
                return;
            }

            if (startResult == RosterQrCameraStartResult.PermissionDenied)
            {
                StatusText = Text("M_CameraPermissionDenied");
                await StopQrScannerAsync(keepStatus: true);
                return;
            }

            StatusText = Text("C_QrImportReady");
        }
        catch (Exception exception)
        {
            StatusText = string.Format(Text("M_CameraStartFailed"), exception.Message);
            await StopQrScannerAsync(keepStatus: true);
        }
    }

    private async Task ProcessCameraFrameAsync(byte[] imageBytes)
    {
        await CameraControl.ShowFrameAsync(imageBytes);
        if (!IsScanning || _qrScanCancellationTokenSource is null)
            return;

        try
        {
            await using var imageStream = new MemoryStream(imageBytes, writable: false);
            var text = await _transferService.DecodeQrTextAsync(imageStream, _qrScanCancellationTokenSource.Token);
            if (!string.IsNullOrWhiteSpace(text))
                await HandleQrTextAsync(text);
        }
        catch (OperationCanceledException)
        {
            // Stopping the scanner cancels the queued decode cycle.
        }
        catch (Exception exception)
        {
            StatusText = string.Format(Text("M_QrTransferFailed"), exception.Message);
            await StopQrScannerAsync(keepStatus: true);
        }
    }

    private async void CameraCapture_OnCameraError(object? sender, string error)
    {
        if (!IsScanning)
            return;
        StatusText = string.Format(Text("M_CameraStartFailed"), error);
        await StopQrScannerAsync(keepStatus: true);
    }

    private async Task HandleQrTextAsync(string text)
    {
        if (IsQuickQrMode)
        {
            await ImportQuickPackageAsync(text);
            return;
        }

        _offlineQrImport ??= _settingsQrService.CreateImportAccumulator();
        var result = _offlineQrImport.Add(text);
        if (result == SettingsTransferQrFrameImportResult.Rejected)
        {
            StatusText = string.Format(Text("M_OfflineFrameStats"), _offlineQrImport.AcceptedFrames,
                _offlineQrImport.TotalFrames);
            NotifyOfflineQrStats();
            return;
        }

        StatusText = Text("C_QrImporting");
        NotifyOfflineQrStats();
        if (!_offlineQrImport.IsComplete)
            return;

        try
        {
            var package = _offlineQrImport.GetCompletedPackage();
            if (await ApplyTransferPackageAsync(package))
                await StopQrScannerAsync(keepStatus: true);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            StatusText = string.Format(Text("M_QrTransferFailed"), exception.Message);
            await StopQrScannerAsync(keepStatus: true);
        }
    }

    private void NotifyOfflineQrStats()
    {
        OfflineQrStatsText = _offlineQrImport is null
            ? string.Empty
            : string.Format(Text("M_OfflineFrameStats"), _offlineQrImport.AcceptedFrames,
                _offlineQrImport.TotalFrames);
        OnPropertyChanged(nameof(OfflineQrStatsText));
    }

    private async Task ImportQuickPackageAsync(string text)
    {
        try
        {
            var result = await _syncTransferService.ImportQuickPackageAsync(text,
                _qrScanCancellationTokenSource?.Token ?? default);
            if (await ApplyTransferPackageAsync(result.Package))
                await StopQrScannerAsync(keepStatus: true);
        }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or IOException)
        {
            StatusText = string.Format(Text("M_QuickQrInvalid"), exception.Message);
        }
    }

    private async Task<bool> ApplyTransferPackageAsync(SyncTransferPackage package)
    {
        if (package.ContentType != SyncTransferContentType.TextLibrary)
        {
            StatusText = Text("M_QrWrongType");
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(package.Content);
            var result = TextLibraryImporter.ParseJson(json);
            if (!result.Success)
            {
                StatusText = Text("M_QrWrongType");
                return false;
            }

            SetPendingTemplates(result.Templates,
                string.Format(Text("M_QrLoaded"), result.Templates.Count));
            StatusText = string.Format(Text("M_QrLoaded"), result.Templates.Count);
            return true;
        }
        catch (Exception exception)
        {
            StatusText = string.Format(Text("M_QrTransferFailed"), exception.Message);
            return false;
        }
    }

    private async void SessionCodeInput_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var normalized = RosterSyncTransferService.NormalizeSessionCode(textBox.Text);
        if (textBox.Text != normalized)
        {
            textBox.Text = normalized;
            textBox.CaretIndex = normalized.Length;
        }

        CancelSessionCodeVerification();
        if (normalized.Length != 12 || _isDrawerClosed)
            return;

        _sessionCodeCancellationTokenSource = new CancellationTokenSource();
        await VerifySessionCodeAsync(normalized, _sessionCodeCancellationTokenSource.Token);
    }

    private async Task VerifySessionCodeAsync(string sessionCode, CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusText = Text("M_SessionCodeWait");
        try
        {
            var result = await _syncTransferService.ImportSessionPackageAsync(sessionCode, cancellationToken);
            if (cancellationToken.IsCancellationRequested || _isDrawerClosed)
                return;
            await ApplyTransferPackageAsync(result.Package);
        }
        catch (OperationCanceledException)
        {
            // A new code or a closing drawer superseded this lookup.
        }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or IOException)
        {
            StatusText = string.Format(Text("M_SessionCodeInvalid"), exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelSessionCodeVerification()
    {
        var cancellation = Interlocked.Exchange(ref _sessionCodeCancellationTokenSource, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task StopQrScannerAsync(bool keepStatus = false)
    {
        var cancellation = Interlocked.Exchange(ref _qrScanCancellationTokenSource, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        var wasScanning = IsScanning;
        IsScanning = false;
        try
        {
            if (_qrCameraCapture is { } cameraCapture)
            {
                _qrCameraCapture = null;
                cameraCapture.CameraError -= CameraCapture_OnCameraError;
                await cameraCapture.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // A provider can already be stopped when the drawer closes.
        }

        if (!keepStatus && wasScanning)
            StatusText = Text("C_QrImportReady");
    }

    private async void ImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanImport)
            return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var existing = _textLibraryService.Entries.Count;
        var result = await new FAContentDialog
        {
            Title = Text("M_ReplaceConfirmTitle"),
            Content = string.Format(Text("M_ReplaceConfirmContent"), existing),
            PrimaryButtonText = Text("C_ReplaceImport"),
            CloseButtonText = Text("C_Cancel"),
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary)
            return;

        try
        {
            _textLibraryService.ReplaceAll(_pendingTemplates);
            this.ShowSuccessToast(string.Format(Text("M_ImportDone"), _pendingTemplates.Count));
            SettingsView.Current?.CloseDrawer();
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(Text("M_ImportFailed"), exception);
        }
    }

    private async void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ((IDrawerCloseAware)this).OnDrawerClosedAsync();
        SettingsView.Current?.CloseDrawer();
    }

    async Task IDrawerCloseAware.OnDrawerClosedAsync()
    {
        if (_isDrawerClosed)
            return;
        _isDrawerClosed = true;
        CancelSessionCodeVerification();
        await StopQrScannerAsync();
        ReleaseTemporaryFile();
    }
}

public enum TextLibraryImportMode
{
    Table,
    Json,
    Transfer
}

public enum TextLibraryTransferMode
{
    QuickQr,
    OfflineQr,
    SessionCode
}

public sealed record TextLibraryImportModeOption(TextLibraryImportMode Value, string Label);

public sealed record TextLibraryTransferModeOption(TextLibraryTransferMode Value, string Label);
