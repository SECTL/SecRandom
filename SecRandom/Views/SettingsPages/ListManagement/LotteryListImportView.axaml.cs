using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CameraView;
using FluentAvalonia.UI.Controls;
using MiniExcelLibs;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Profiles;
using SecRandom.Langs.SettingsPages.ListManagement.RosterTransfer;
using SecRandom.Services.RosterTransfer;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.ListManagement.LotteryList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

public partial class LotteryListImportView : UserControl, INotifyPropertyChanged
{
    private Action<IReadOnlyList<Prize>> _importHandler;
    private readonly List<Dictionary<string, string>> _rows = [];
    private bool _canImport;
    private string? _countColumn;
    private string? _idColumn;
    private string? _nameColumn;
    private string _selectedFileName = LR.C_NoFileSelected;
    private string _statusText = LR.M_SelectFileFirst;
    private string? _tagsColumn;
    private string? _weightColumn;
    private string _targetListName = string.Empty;
    private bool _isQrImportMode;
    private bool _isScanningQr;
    private List<Prize>? _qrPrizes;
    private readonly RosterTransferService _transferService = IAppHost.GetService<RosterTransferService>();
    private readonly RosterTransferService.RosterQrImportAccumulator _qrImport;
    private CancellationTokenSource? _qrScanCancellationTokenSource;
    private LinuxRosterQrCameraCapture? _linuxQrCameraCapture;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<LotteryListImportView> _logger =
        IAppHost.GetService<ILogger<LotteryListImportView>>();

    public LotteryListImportView()
        : this(string.Empty, _ => { })
    {
    }

    public LotteryListImportView(string targetListName, Action<IReadOnlyList<Prize>> importHandler)
    {
        TargetListName = targetListName;
        _importHandler = importHandler;
        _qrImport = _transferService.CreateImportAccumulator();
        DataContext = this;
        InitializeComponent();
        FileImportMenuItem.Header = FileImportModeLabel;
        QrImportMenuItem.Header = QrImportModeLabel;
    }

    public ObservableCollection<string> RequiredColumnOptions { get; } = [];
    public ObservableCollection<string> OptionalColumnOptions { get; } = [LR.C_NoneColumn];
    public ObservableCollection<ImportPreviewRow> PreviewRows { get; } = [];

    public string TargetListName
    {
        get => _targetListName;
        set
        {
            if (SetField(ref _targetListName, value))
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetListDescription)));
        }
    }

    public string TargetListDescription => string.Format(LR.C_TargetList, TargetListName);

    public Action<IReadOnlyList<Prize>> ImportHandler
    {
        get => _importHandler;
        set => _importHandler = value;
    }

    public Action? CloseHandler { get; set; }

    public string SelectedFileName
    {
        get => _selectedFileName;
        set => SetField(ref _selectedFileName, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool CanImport
    {
        get => _canImport;
        set => SetField(ref _canImport, value);
    }

    public bool IsQrImportMode
    {
        get => _isQrImportMode;
        private set
        {
            if (!SetField(ref _isQrImportMode, value))
                return;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFileImportMode)));
        }
    }

    public bool IsFileImportMode => !IsQrImportMode;
    public bool CanScanQr => true;
    public bool IsQrScanning => _isScanningQr;
    public bool IsCameraPreviewSupported => !OperatingSystem.IsLinux();
    public string FileImportModeLabel => Text("C_ImportExcelCsv");
    public string QrImportModeLabel => Text("C_ImportQrCode");
    public string ImportSourceLabel => Text("C_SelectImportSource");
    public string QrImportLabel => Text("C_QrImport");
    public string ScanQrLabel => IsQrScanning ? Text("C_StopQrScanner") : Text("C_StartQrScanner");
    public string TransferProgressLabel => Text("C_TransferProgress");
    public string TransferSpeedLabel => Text("C_TransferSpeed");
    public string TransferReceivedLabel => Text("C_TransferReceived");
    public string TransferFramesDetailLabel => Text("C_TransferFramesDetail");
    public string TransferSessionLabel => Text("C_TransferSession");
    public string TransferElapsedLabel => Text("C_TransferElapsed");
    public double QrProgress => _qrImport.TotalFrames == 0 ? 0 : (double)_qrImport.AcceptedFrames / _qrImport.TotalFrames;
    public string QrProgressText => _qrImport.TotalFrames == 0 ? "-" : $"{_qrImport.AcceptedFrames} / {_qrImport.TotalFrames}";
    public string QrDecodeSpeedText => _qrImport.StartedAt == default ? "-" :
        string.Format(Text("C_TransferFramesPerSecond"),
            _qrImport.AcceptedFrames / Math.Max(0.1, (DateTimeOffset.UtcNow - _qrImport.StartedAt).TotalSeconds));
    public string QrPayloadText => _qrImport.PayloadLength == 0 ? "-" :
        $"{(_qrImport.PayloadLength * QrProgress):F0} B / {_qrImport.PayloadLength} B";
    public string QrFramesText => $"{_qrImport.AcceptedFrames}/{_qrImport.DuplicateFrames}/{_qrImport.RejectedFrames}";
    public string QrSessionText => _qrImport.TotalFrames == 0 ? "-" : _qrImport.SessionId[..8];
    public string QrElapsedText => _qrImport.StartedAt == default ? "-" :
        $"{Math.Max(0, (DateTimeOffset.UtcNow - _qrImport.StartedAt).TotalSeconds):F1} s";

    public string? IdColumn
    {
        get => _idColumn;
        set
        {
            if (SetField(ref _idColumn, value)) RefreshPreview();
        }
    }

    public string? NameColumn
    {
        get => _nameColumn;
        set
        {
            if (SetField(ref _nameColumn, value)) RefreshPreview();
        }
    }

    public string? WeightColumn
    {
        get => _weightColumn;
        set
        {
            if (SetField(ref _weightColumn, value)) RefreshPreview();
        }
    }

    public string? CountColumn
    {
        get => _countColumn;
        set
        {
            if (SetField(ref _countColumn, value)) RefreshPreview();
        }
    }

    public string? TagsColumn
    {
        get => _tagsColumn;
        set
        {
            if (SetField(ref _tagsColumn, value)) RefreshPreview();
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private async Task SelectFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LR.C_FilePickerTitle,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LR.C_FilePickerTypeName)
                {
                    Patterns = ["*.xlsx", "*.xls", "*.csv"],
                    MimeTypes =
                    [
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "application/vnd.ms-excel",
                        "text/csv"
                    ]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file == null)
            return;

        try
        {
            await LoadFileAsync(file);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载奖品池导入文件失败：文件={FileName}。", file.Name);
            StatusText = string.Format(LR.M_LoadFailed, ex.Message);
            CanImport = false;
        }
    }

    private async Task LoadFileAsync(IStorageFile file)
    {
        var path = file.TryGetLocalPath();
        var temporaryPath = false;
        if (path is null)
        {
            path = await CopyToTemporaryFileAsync(file);
            temporaryPath = true;
        }

        List<Dictionary<string, string>> rows;
        try
        {
            rows = MiniExcel.Query(path, useHeaderRow: true)
                .Cast<IDictionary<string, object?>>()
                .Select(row => row.ToDictionary(pair => pair.Key, pair => ConvertCell(pair.Value)))
                .ToList();
        }
        finally
        {
            if (temporaryPath && File.Exists(path))
                File.Delete(path);
        }

        _rows.Clear();
        _rows.AddRange(rows);
        SelectedFileName = file.Name;
        RebuildColumnOptions(rows.FirstOrDefault()?.Keys ?? Enumerable.Empty<string>());
        AutoMapColumns();
        RefreshPreview();
        _logger.LogInformation("已加载奖品池导入文件：文件={FileName}，行数={RowCount}。", file.Name, rows.Count);
    }

    private static async Task<string> CopyToTemporaryFileAsync(IStorageFile file)
    {
        var extension = Path.GetExtension(file.Name);
        var path = Path.Combine(Path.GetTempPath(), $"SecRandom-{Guid.NewGuid():N}{extension}");
        await using var source = await file.OpenReadAsync();
        await using var target = File.Create(path);
        await source.CopyToAsync(target);
        return path;
    }

    private void RebuildColumnOptions(IEnumerable<string> columns)
    {
        RequiredColumnOptions.Clear();
        OptionalColumnOptions.Clear();
        OptionalColumnOptions.Add(LR.C_NoneColumn);

        foreach (var column in columns.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            RequiredColumnOptions.Add(column);
            OptionalColumnOptions.Add(column);
        }

        IdColumn = LR.C_NoneColumn;
        NameColumn = LR.C_NoneColumn;
        WeightColumn = LR.C_NoneColumn;
        CountColumn = LR.C_NoneColumn;
        TagsColumn = LR.C_NoneColumn;
    }

    private void AutoMapColumns()
    {
        IdColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_IdColumns)) ?? LR.C_NoneColumn;
        NameColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_NameColumns)) ?? NameColumn;
        WeightColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_WeightColumns)) ?? LR.C_NoneColumn;
        CountColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_CountColumns)) ?? LR.C_NoneColumn;
        TagsColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_TagsColumns)) ?? LR.C_NoneColumn;
    }

    private PrizeRosterColumnMapping CurrentMapping => new(
        IsSelectedColumn(IdColumn) ? IdColumn : null,
        IsSelectedColumn(NameColumn) ? NameColumn : null,
        IsSelectedColumn(WeightColumn) ? WeightColumn : null,
        IsSelectedColumn(CountColumn) ? CountColumn : null,
        IsSelectedColumn(TagsColumn) ? TagsColumn : null);

    private void RefreshPreview()
    {
        PreviewRows.Clear();
        foreach (var row in _rows.Take(3))
            PreviewRows.Add(CreatePreviewRow(row));

        CanImport = _rows.Count > 0 && (IsSelectedColumn(IdColumn) || IsSelectedColumn(NameColumn));
        StatusText = _rows.Count == 0
            ? LR.M_SelectFileFirst
            : CanImport
                ? string.Format(LR.M_FileLoaded, _rows.Count)
                : LR.M_SelectRequiredColumns;
    }

    private ImportPreviewRow CreatePreviewRow(IReadOnlyDictionary<string, string> row)
    {
        var mapping = CurrentMapping;
        return new ImportPreviewRow(
            RosterImportParser.GetValue(row, mapping.Id),
            RosterImportParser.GetValue(row, mapping.Name),
            RosterImportParser.GetValue(row, mapping.Weight),
            RosterImportParser.GetValue(row, mapping.Count),
            string.Join(' ', RosterImportParser.SplitTags(RosterImportParser.GetValue(row, mapping.Tags))));
    }

    private void ImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IsQrImportMode)
        {
            if (_qrPrizes is not null)
                SubmitPrizes(_qrPrizes);
            return;
        }

        var parseResult = RosterImportParser.ParsePrizes(_rows, CurrentMapping);
        SubmitPrizes(parseResult.Items, parseResult.DuplicatedNames);
    }

    private void SubmitPrizes(List<Prize> prizes, IReadOnlyList<string>? duplicatedNames = null)
    {
        duplicatedNames ??= prizes
            .Where(prize => !string.IsNullOrWhiteSpace(prize.Name))
            .GroupBy(prize => prize.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray();

        if (duplicatedNames.Count > 0)
        {
            _logger.LogWarning("奖品池导入发现重复名称：重复名称数量={DuplicateNameCount}，有效行数={Count}。",
                duplicatedNames.Count, prizes.Count);
            _ = ConfirmDuplicateNamesAsync(prizes, duplicatedNames);
        }
        else
        {
            _logger.LogInformation("提交奖品池导入：有效行数={Count}。", prizes.Count);
            _importHandler(prizes);
        }
    }

    private async void ImportSplitButton_OnClick(object? sender, RoutedEventArgs e) =>
        await OpenFileImportAsync();

    private async void FileImportMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        await OpenFileImportAsync();
    }

    private async void QrImportMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        await StopQrScannerAsync();
        SelectQrImportSource();
        await ToggleQrScannerAsync();
    }

    private async Task OpenFileImportAsync()
    {
        IsQrImportMode = false;
        await StopQrScannerAsync();
        await SelectFileAsync();
        if (_rows.Count == 0)
            StatusText = LR.M_SelectFileFirst;
    }

    private void SelectQrImportSource()
    {
        _qrImport.Reset();
        _qrPrizes = null;
        PreviewRows.Clear();
        IsQrImportMode = true;
        StatusText = Text("C_QrImportReady");
        CanImport = false;
        NotifyQrStatsChanged();
    }

    private async Task ToggleQrScannerAsync()
    {
        if (_isScanningQr)
        {
            await StopQrScannerAsync();
            return;
        }

        try
        {
            _qrScanCancellationTokenSource = new CancellationTokenSource();
            _isScanningQr = true;
            NotifyQrScannerStateChanged();
            if (OperatingSystem.IsLinux())
            {
                _linuxQrCameraCapture = new LinuxRosterQrCameraCapture();
                await _linuxQrCameraCapture.StartAsync(ProcessCapturedQrImageAsync,
                    _qrScanCancellationTokenSource.Token);
                StatusText = Text("C_QrImportReady");
                return;
            }
            var provider = CameraProviderFactory.Create();
            var permissions = CameraProviderFactory.CreatePermissions(provider);
            if (!await permissions.CheckPermissionAsync() && !await permissions.RequestPermissionAsync())
            {
                StatusText = Text("M_CameraPermissionDenied");
                await StopQrScannerAsync(keepStatus: true);
                return;
            }
            CameraControl.CameraProvider = provider;
            await CameraControl.InitializeCameraAsync(provider);
            await CameraControl.StartCameraAsync();
            StatusText = Text("C_QrImportReady");
            await CaptureNextQrFrameAsync(_qrScanCancellationTokenSource.Token);
        }
        catch (Exception exception)
        {
            StatusText = string.Format(Text("M_CameraStartFailed"), exception.Message);
            await StopQrScannerAsync(keepStatus: true);
        }
    }

    private async void CameraControl_OnPhotoCaptured(object? sender, byte[] imageBytes)
    {
        await ProcessCapturedQrImageAsync(imageBytes);
    }

    private async Task ProcessCapturedQrImageAsync(byte[] imageBytes)
    {
        if (!_isScanningQr || _qrScanCancellationTokenSource is null)
            return;
        try
        {
            await using var imageStream = new MemoryStream(imageBytes, writable: false);
            var text = await _transferService.DecodeQrTextAsync(imageStream, _qrScanCancellationTokenSource.Token);
            if (!string.IsNullOrWhiteSpace(text))
                await HandleQrTextAsync(text);
            if (_isScanningQr && !_qrImport.IsComplete && !OperatingSystem.IsLinux())
                await CaptureNextQrFrameAsync(_qrScanCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            StatusText = string.Format(Text("M_QrTransferFailed"), exception.Message);
            await StopQrScannerAsync(keepStatus: true);
        }
    }

    private async void CameraControl_OnCameraError(object? sender, string error)
    {
        if (!_isScanningQr)
            return;
        StatusText = string.Format(Text("M_CameraStartFailed"), error);
        await StopQrScannerAsync(keepStatus: true);
    }

    private async Task HandleQrTextAsync(string text)
    {
        var result = _qrImport.Add(text);
        if (result == RosterQrFrameImportResult.Rejected)
        {
            StatusText = Text("M_QrNotFound");
            NotifyQrStatsChanged();
            return;
        }

        StatusText = Text("C_QrImporting");
        NotifyQrStatsChanged();
        if (!_qrImport.IsComplete)
            return;

        var document = _qrImport.GetCompletedDocument();
        if (document.Version != 1 || document.Kind != RosterTransferKind.Prizes)
        {
            StatusText = Text("M_QrWrongType");
            await StopQrScannerAsync(keepStatus: true);
            return;
        }

        _qrPrizes = document.Rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Id) || !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new Prize
            {
                RecordId = Guid.NewGuid(), Exists = row.Exists, Id = row.Id, Name = row.Name,
                Weight = double.TryParse(row.DetailOne, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) ? weight : 1,
                Count = int.TryParse(row.DetailTwo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 1,
                Tags = row.Tags ?? string.Empty
            }).ToList();
        PreviewRows.Clear();
        foreach (var prize in _qrPrizes.Take(3))
            PreviewRows.Add(new ImportPreviewRow(prize.Id, prize.Name,
                prize.Weight.ToString(CultureInfo.CurrentCulture), prize.Count.ToString(CultureInfo.CurrentCulture), prize.Tags));
        CanImport = true;
        await StopQrScannerAsync(keepStatus: true);
        StatusText = string.Format(Text("M_QrLoaded"), _qrPrizes.Count);
    }

    private async Task CaptureNextQrFrameAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(120, cancellationToken);
        if (_isScanningQr && !cancellationToken.IsCancellationRequested)
            await CameraControl.TakePhotoAsync();
    }

    private async Task StopQrScannerAsync(bool keepStatus = false)
    {
        var cancellation = Interlocked.Exchange(ref _qrScanCancellationTokenSource, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (!_isScanningQr)
            return;
        _isScanningQr = false;
        try
        {
            if (_linuxQrCameraCapture is { } linuxCapture)
            {
                _linuxQrCameraCapture = null;
                await linuxCapture.DisposeAsync();
            }
            else
            {
                await CameraControl.StopCameraAsync();
            }
        }
        catch (Exception) { }
        if (!keepStatus)
            StatusText = Text("C_QrImportReady");
        NotifyQrScannerStateChanged();
    }

    private void NotifyQrScannerStateChanged()
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanScanQr)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQrScanning)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScanQrLabel)));
    }

    private async Task ConfirmDuplicateNamesAsync(List<Prize> prizes, IReadOnlyList<string> duplicatedNames)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_DuplicateTitle,
            Content = string.Format(LR.M_DuplicateContent, duplicatedNames.Count,
                string.Join('\n', duplicatedNames.Take(10))),
            PrimaryButtonText = LR.M_DuplicatePrimary,
            SecondaryButtonText = LR.M_DuplicateSecondary,
            CloseButtonText = LR.M_DuplicateClose,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        if (result == FAContentDialogResult.Primary)
        {
            _logger.LogInformation("奖品池导入保留重复名称：有效行数={Count}。", prizes.Count);
            _importHandler(prizes);
            return;
        }

        if (result == FAContentDialogResult.Secondary)
        {
            RosterImportParser.RenameDuplicatedPrizes(prizes);
            _logger.LogInformation("奖品池导入已自动处理重复名称：有效行数={Count}。", prizes.Count);
            _importHandler(prizes);
        }
    }

    private async void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await StopQrScannerAsync();
        if (CloseHandler is not null)
            CloseHandler();
        else
            SettingsView.Current?.CloseDrawer();
    }

    private static bool IsSelectedColumn(string? column)
    {
        return !string.IsNullOrWhiteSpace(column) && column != LR.C_NoneColumn;
    }

    private static string ConvertCell(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString(CultureInfo.CurrentCulture),
            _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
        };
    }

    private static string Text(string name) => RosterTransferText.Get(name);

    private void NotifyQrStatsChanged()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(QrProgress), nameof(QrProgressText), nameof(QrDecodeSpeedText), nameof(QrPayloadText),
                     nameof(QrFramesText), nameof(QrSessionText), nameof(QrElapsedText)
                 })
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public record ImportPreviewRow(string Id, string Name, string Weight, string Count, string Tags);
}
