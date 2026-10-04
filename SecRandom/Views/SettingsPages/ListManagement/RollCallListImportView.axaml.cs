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
using FluentAvalonia.UI.Controls;
using MiniExcelLibs;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Profiles;
using SecRandom.Langs.SettingsPages.ListManagement.RosterTransfer;
using SecRandom.Services.RosterTransfer;
using SecRandom.Shared.Models.Profile;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.ListManagement.RollCallList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

public partial class RollCallListImportView : UserControl, INotifyPropertyChanged, IDrawerCloseAware
{
    private Action<IReadOnlyList<Student>> _importHandler;
    private IReadOnlyList<IReadOnlyList<string?>> _grid = [];
    private RosterImportTable _table = new([], []);
    private string[] _sheetNames = [];
    private string? _filePath;
    private string? _loadedSheetName;
    private string? _temporaryPath;
    private bool _canImport;
    private string? _genderColumn;
    private string? _groupColumn;
    private string? _idColumn;
    private string? _nameColumn;
    private string _selectedFileName = LR.C_NoFileSelected;
    private string _statusText = LR.M_SelectFileFirst;
    private string? _tagsColumn;
    private string _targetListName = string.Empty;
    private bool _isQrImportMode;
    private RosterImportMode _selectedImportMode = RosterImportMode.ExcelCsv;
    private RosterImportModeOption _selectedImportModeOption = null!;
    private RosterQrCameraOption? _selectedCameraOption;
    private bool _isScanningQr;
    private bool _isDrawerClosed;
    private const int SessionCodeLength = 12;
    private bool _isUpdatingSessionCode;
    private CancellationTokenSource? _sessionCodeVerificationCancellationTokenSource;
    private List<Student>? _qrStudents;
    private readonly RosterTransferService _transferService = IAppHost.GetService<RosterTransferService>();
    private readonly RosterSyncTransferService _syncTransferService = IAppHost.GetService<RosterSyncTransferService>();
    private readonly IRosterQrCameraCaptureFactory _qrCameraCaptureFactory =
        IAppHost.GetService<IRosterQrCameraCaptureFactory>();
    private readonly RosterTransferService.RosterQrImportAccumulator _qrImport;
    private CancellationTokenSource? _qrScanCancellationTokenSource;
    private IRosterQrCameraCapture? _qrCameraCapture;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<RollCallListImportView> _logger =
        IAppHost.GetService<ILogger<RollCallListImportView>>();

    public RollCallListImportView()
        : this(string.Empty, _ => { })
    {
    }

    public RollCallListImportView(string targetListName, Action<IReadOnlyList<Student>> importHandler)
    {
        TargetListName = targetListName;
        _importHandler = importHandler;
        _qrImport = _transferService.CreateImportAccumulator();
        ImportModes =
        [
            new(RosterImportMode.ExcelCsv, FileImportModeLabel),
            new(RosterImportMode.QuickQr, QuickQrImportModeLabel),
            new(RosterImportMode.OfflineQr, OfflineQrImportModeLabel),
            new(RosterImportMode.SessionCode, SessionCodeImportModeLabel)
        ];
        _selectedImportModeOption = ImportModes[0];
        DataContext = this;
        InitializeComponent();
        RegionSelector.RegionChanged += OnRegionChanged;
        RegionSelector.SheetSelectionChanged += OnSheetSelectionChanged;
        Loaded += (_, _) => _ = LoadCameraOptionsAsync();
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

    public Action<IReadOnlyList<Student>> ImportHandler
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
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQuickQrImportMode)));
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOfflineQrImportMode)));
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSessionCodeImportMode)));
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQrTransferStatsVisible)));
        }
    }

    public bool IsFileImportMode => _selectedImportMode == RosterImportMode.ExcelCsv;
    public bool IsQuickQrImportMode => _selectedImportMode == RosterImportMode.QuickQr;
    public bool IsOfflineQrImportMode => _selectedImportMode == RosterImportMode.OfflineQr;
    public bool IsSessionCodeImportMode => _selectedImportMode == RosterImportMode.SessionCode;
    public bool IsQrTransferStatsVisible => IsOfflineQrImportMode;
    public bool HasPreview => PreviewRows.Count > 0;
    public bool CanScanQr => true;
    public bool IsQrScanning => _isScanningQr;
    public bool IsCameraPreviewSupported => _qrCameraCaptureFactory.IsPreviewSupported;
    public bool HasCameraSelection => CameraOptions.Count > 1;
    public string FileImportModeLabel => Text("C_ImportExcelCsv");
    public string QuickQrImportModeLabel => Text("C_ImportQuickQr");
    public string OfflineQrImportModeLabel => Text("C_ImportOfflineQr");
    public string SessionCodeImportModeLabel => Text("C_ImportSessionCode");
    public string ImportSourceLabel => Text("C_SelectImportSource");
    public IReadOnlyList<RosterImportModeOption> ImportModes { get; }
    public ObservableCollection<RosterQrCameraOption> CameraOptions { get; } = [];
    public RosterImportModeOption SelectedImportModeOption
    {
        get => _selectedImportModeOption;
        set
        {
            if (value is null || ReferenceEquals(_selectedImportModeOption, value))
                return;

            if (!SetField(ref _selectedImportModeOption, value))
                return;

            _ = SelectImportModeAsync(value.Mode);
        }
    }
    public RosterQrCameraOption? SelectedCameraOption
    {
        get => _selectedCameraOption;
        set
        {
            if (value is null || ReferenceEquals(_selectedCameraOption, value) || !SetField(ref _selectedCameraOption, value))
                return;

            _ = RestartQrScannerForCameraChangeAsync();
        }
    }
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
    public string SessionCodeHint => Text("C_SessionCodeHint");
    public string SessionCodeVerifyingText => Text("C_SessionCodeVerifying");

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

    public string? GenderColumn
    {
        get => _genderColumn;
        set
        {
            if (SetField(ref _genderColumn, value)) RefreshPreview();
        }
    }

    public string? GroupColumn
    {
        get => _groupColumn;
        set
        {
            if (SetField(ref _groupColumn, value)) RefreshPreview();
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
            _logger.LogWarning(ex, "加载点名名单导入文件失败：文件={FileName}。", file.Name);
            StatusText = string.Format(LR.M_LoadFailed, ex.Message);
            CanImport = false;
        }
    }

    private async Task LoadFileAsync(IStorageFile file)
    {
        var path = file.TryGetLocalPath();
        var temporaryPath = false;
        if (path == null)
        {
            path = await CopyToTemporaryFileAsync(file);
            temporaryPath = true;
        }

        // 换工作表要按新工作表重新读取，所以非本地路径的临时副本保留到抽屉关闭
        ReleaseTemporaryFile();
        _temporaryPath = temporaryPath ? path : null;
        _filePath = path;
        _sheetNames = ReadSheetNames(path);
        try
        {
            LoadSheet(_sheetNames.FirstOrDefault());
        }
        catch
        {
            ReleaseTemporaryFile();
            throw;
        }

        SelectedFileName = file.Name;
        _logger.LogInformation("已加载点名名单导入文件：文件={FileName}，工作表={SheetName}，行数={RowCount}。",
            file.Name, _loadedSheetName ?? "-", _grid.Count);
    }

    /// <summary>CSV 不是 zip 包也没有工作表概念，老式 .xls 可能枚举不出工作表名，两种情况都按单表处理。</summary>
    private string[] ReadSheetNames(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            return [];

        try
        {
            return MiniExcel.GetSheetNames(path).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取工作表名失败，按默认工作表继续：文件={FileName}。", Path.GetFileName(path));
            return [];
        }
    }

    private void LoadSheet(string? sheetName)
    {
        if (string.IsNullOrEmpty(_filePath))
            return;

        _grid = MiniExcel.Query(_filePath, useHeaderRow: false, sheetName: sheetName)
            .Cast<IDictionary<string, object?>>()
            .Select(ToCells)
            .ToList();
        _loadedSheetName = sheetName;
        RegionSelector.Load(_sheetNames, sheetName, _grid, HeaderKeywords);
        ApplyRegion();
    }

    /// <summary>区域是列名与数据行的唯一来源，区域一变就重建列映射并刷新预览。</summary>
    private void ApplyRegion()
    {
        _table = RosterImportRegionBuilder.Build(_grid, RegionSelector.CurrentRegion);
        RebuildColumnOptions(_table.Columns);
        AutoMapColumns();
        RefreshPreview();
    }

    private void OnRegionChanged(object? sender, EventArgs e) => ApplyRegion();

    private void OnSheetSelectionChanged(object? sender, string sheetName)
    {
        if (_isDrawerClosed || string.IsNullOrEmpty(_filePath) || _loadedSheetName == sheetName)
            return;

        try
        {
            LoadSheet(sheetName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "切换点名名单导入工作表失败：文件={FileName}，工作表={SheetName}。",
                Path.GetFileName(_filePath), sheetName);
            StatusText = string.Format(LR.M_LoadFailed, ex.Message);
            // 读取失败时退回上一个工作表，避免下拉显示与实际数据不一致
            if (_loadedSheetName is { } previous && previous != sheetName)
            {
                try
                {
                    LoadSheet(previous);
                }
                catch (Exception restoreFailure)
                {
                    _logger.LogWarning(restoreFailure, "恢复到上一个工作表同样失败：工作表={SheetName}。", previous);
                }
            }
        }
    }

    private void ReleaseTemporaryFile()
    {
        var path = _temporaryPath;
        _temporaryPath = null;
        if (path is null)
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 关闭抽屉时的临时文件清理失败不值得打断流程
        }
    }

    /// <summary>原始网格按列字母落位，行序与 Excel 行号一致（MiniExcel 会补齐中间空行）。</summary>
    private static string?[] ToCells(IDictionary<string, object?> row)
    {
        var cells = new string?[row.Count];
        foreach (var pair in row)
        {
            var index = RosterImportRegionBuilder.ColumnIndex(pair.Key);
            if (index < 1 || index > cells.Length)
                continue;

            cells[index - 1] = ConvertCell(pair.Value);
        }

        return cells;
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
        GenderColumn = LR.C_NoneColumn;
        GroupColumn = LR.C_NoneColumn;
        TagsColumn = LR.C_NoneColumn;
    }

    private void AutoMapColumns()
    {
        IdColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_IdColumns)) ?? LR.C_NoneColumn;
        NameColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_NameColumns)) ?? NameColumn;
        GenderColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_GenderColumns))
                       ?? LR.C_NoneColumn;
        GroupColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_GroupColumns))
                      ?? LR.C_NoneColumn;
        TagsColumn = RosterImportParser.FindBestColumn(RequiredColumnOptions, RosterImportParser.SplitKeywords(LR.K_TagsColumns))
                     ?? LR.C_NoneColumn;
    }

    /// <summary>自动识别表头行与自动映射列共用同一套关键字。</summary>
    private static IReadOnlyList<string> HeaderKeywords =>
    [
        .. RosterImportParser.SplitKeywords(LR.K_IdColumns),
        .. RosterImportParser.SplitKeywords(LR.K_NameColumns),
        .. RosterImportParser.SplitKeywords(LR.K_GenderColumns),
        .. RosterImportParser.SplitKeywords(LR.K_GroupColumns),
        .. RosterImportParser.SplitKeywords(LR.K_TagsColumns)
    ];

    private StudentRosterColumnMapping CurrentMapping => new(
        IsSelectedColumn(IdColumn) ? IdColumn : null,
        IsSelectedColumn(NameColumn) ? NameColumn : null,
        IsSelectedColumn(GenderColumn) ? GenderColumn : null,
        IsSelectedColumn(GroupColumn) ? GroupColumn : null,
        IsSelectedColumn(TagsColumn) ? TagsColumn : null);

    private void RefreshPreview()
    {
        PreviewRows.Clear();
        foreach (var row in _table.Rows.Take(3))
            PreviewRows.Add(CreatePreviewRow(row));

        CanImport = _table.Rows.Count > 0 && (IsSelectedColumn(IdColumn) || IsSelectedColumn(NameColumn));
        StatusText = _grid.Count == 0
            ? LR.M_SelectFileFirst
            : _table.Rows.Count == 0
                ? Text("M_RegionEmpty")
                : CanImport
                    ? string.Format(LR.M_FileLoaded, _table.Rows.Count)
                    : LR.M_SelectRequiredColumns;
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
    }

    private ImportPreviewRow CreatePreviewRow(IReadOnlyDictionary<string, string> row)
    {
        var mapping = CurrentMapping;
        return new ImportPreviewRow(
            RosterImportParser.GetValue(row, mapping.Id),
            RosterImportParser.GetValue(row, mapping.Name),
            RosterImportParser.GetValue(row, mapping.Gender),
            RosterImportParser.GetValue(row, mapping.Group),
            string.Join(' ', RosterImportParser.SplitTags(RosterImportParser.GetValue(row, mapping.Tags))));
    }

    private void ImportButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!IsFileImportMode)
        {
            if (_qrStudents is not null)
                SubmitStudents(_qrStudents);
            return;
        }

        var parseResult = RosterImportParser.ParseStudents(_table.Rows, CurrentMapping);
        SubmitStudents(parseResult.Items, parseResult.DuplicatedNames);
    }

    private void SubmitStudents(List<Student> students, IReadOnlyList<string>? duplicatedNames = null)
    {
        duplicatedNames ??= students
            .Where(student => !string.IsNullOrWhiteSpace(student.Name))
            .GroupBy(student => student.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray();

        if (duplicatedNames.Count > 0)
        {
            _logger.LogWarning("点名名单导入发现重复姓名：重复姓名数量={DuplicateNameCount}，有效行数={Count}。",
                duplicatedNames.Count, students.Count);
            _ = ConfirmDuplicateNamesAsync(students, duplicatedNames);
        }
        else
        {
            _logger.LogInformation("提交点名名单导入：有效行数={Count}。", students.Count);
            _importHandler(students);
        }
    }

    private async void FileImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await OpenFileImportAsync();
    }

    private async Task SelectImportModeAsync(RosterImportMode mode)
    {
        await StopQrScannerAsync();
        if (_isDrawerClosed || SelectedImportModeOption.Mode != mode)
            return;

        switch (mode)
        {
            case RosterImportMode.ExcelCsv:
                SelectFileImportSource();
                break;
            case RosterImportMode.QuickQr:
            case RosterImportMode.OfflineQr:
                SelectQrImportSource(mode);
                await ToggleQrScannerAsync();
                break;
            case RosterImportMode.SessionCode:
                SelectSessionCodeImportSource();
                break;
        }
    }

    private async Task OpenFileImportAsync()
    {
        await StopQrScannerAsync();
        SelectFileImportSource();
        await SelectFileAsync();
        // 用户取消选择时保持「请选择文件」；读取失败时保留失败原因
        if (_grid.Count == 0 && _filePath is null)
            StatusText = LR.M_SelectFileFirst;
    }

    private void SelectFileImportSource()
    {
        _selectedImportMode = RosterImportMode.ExcelCsv;
        IsQrImportMode = false;
        ResetPreviewAndImportState();
        StatusText = LR.M_SelectFileFirst;
        NotifyImportModeChanged();
    }

    private void SelectQrImportSource(RosterImportMode mode)
    {
        _selectedImportMode = mode;
        _qrImport.Reset();
        _qrStudents = null;
        PreviewRows.Clear();
        CancelSessionCodeVerification();
        IsQrImportMode = true;
        StatusText = Text("C_QrImportReady");
        CanImport = false;
        NotifyImportModeChanged();
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
        NotifyQrStatsChanged();
    }

    private void SelectSessionCodeImportSource()
    {
        _selectedImportMode = RosterImportMode.SessionCode;
        IsQrImportMode = false;
        _qrStudents = null;
        PreviewRows.Clear();
        ResetSessionCodeInput();
        CancelSessionCodeVerification();
        CanImport = false;
        StatusText = string.Empty;
        NotifyImportModeChanged();
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
    }

    private void ResetPreviewAndImportState()
    {
        ReleaseTemporaryFile();
        _filePath = null;
        _loadedSheetName = null;
        _sheetNames = [];
        _grid = [];
        _table = new RosterImportTable([], []);
        RegionSelector.Clear();
        _qrStudents = null;
        PreviewRows.Clear();
        SelectedFileName = LR.C_NoFileSelected;
        CancelSessionCodeVerification();
        CanImport = false;
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
    }

    private void NotifyImportModeChanged()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(IsFileImportMode), nameof(IsQuickQrImportMode), nameof(IsOfflineQrImportMode),
                     nameof(IsSessionCodeImportMode), nameof(IsQrTransferStatsVisible)
                 })
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private async Task ToggleQrScannerAsync()
    {
        if (_isDrawerClosed)
            return;
        if (_isScanningQr)
        {
            await StopQrScannerAsync();
            return;
        }

        try
        {
            await LoadCameraOptionsAsync();
            _qrScanCancellationTokenSource = new CancellationTokenSource();
            _isScanningQr = true;
            NotifyQrScannerStateChanged();
            var cameraCapture = _qrCameraCaptureFactory.Create(CameraControl, SelectedCameraOption?.Device);
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

        }
        catch (OperationCanceledException)
        {
            // Stopping the scanner cancels the queued decode/capture cycle.
        }
        catch (Exception exception)
        {
            StatusText = string.Format(Text("M_QrTransferFailed"), exception.Message);
            await StopQrScannerAsync(keepStatus: true);
        }
    }

    private async Task ProcessCameraFrameAsync(byte[] imageBytes)
    {
        await CameraControl.ShowFrameAsync(imageBytes);
        await ProcessCapturedQrImageAsync(imageBytes);
    }

    private async void CameraCapture_OnCameraError(object? sender, string error)
    {
        if (!_isScanningQr)
            return;
        StatusText = string.Format(Text("M_CameraStartFailed"), error);
        await StopQrScannerAsync(keepStatus: true);
    }

    private async Task HandleQrTextAsync(string text)
    {
        if (IsQuickQrImportMode)
        {
            try
            {
                var syncResult = await _syncTransferService.ImportQuickAsync(text, _qrScanCancellationTokenSource?.Token ?? default);
                if (syncResult.Document.Version != 1 || syncResult.Document.Kind != RosterTransferKind.Students)
                {
                    StatusText = Text("M_QrWrongType");
                    return;
                }
                LoadQrStudents(syncResult.Document);
                CanImport = true;
                await StopQrScannerAsync(keepStatus: true);
                StatusText = string.Format(Text("M_QrLoaded"), _qrStudents?.Count ?? 0);
            }
            catch (Exception exception) when (exception is InvalidDataException or HttpRequestException)
            {
                StatusText = string.Format(Text("M_QuickQrInvalid"), exception.Message);
            }
            return;
        }

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
        if (document.Version != 1 || document.Kind != RosterTransferKind.Students)
        {
            StatusText = Text("M_QrWrongType");
            await StopQrScannerAsync(keepStatus: true);
            return;
        }

        LoadQrStudents(document);
        CanImport = true;
        await StopQrScannerAsync(keepStatus: true);
        StatusText = string.Format(Text("M_QrLoaded"), _qrStudents?.Count ?? 0);
    }

    private void LoadQrStudents(RosterTransferDocument document)
    {
        _qrStudents = document.Rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Id) || !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => new Student
            {
                RecordId = Guid.NewGuid(), Exists = row.Exists, Id = row.Id, Name = row.Name,
                Gender = row.DetailOne ?? string.Empty, Group = row.DetailTwo ?? string.Empty, Tags = row.Tags ?? string.Empty
            }).ToList();
        PreviewRows.Clear();
        foreach (var student in _qrStudents.Take(3))
            PreviewRows.Add(new ImportPreviewRow(student.Id, student.Name, student.Gender, student.Group, student.Tags));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
    }

    private async void SessionCodeInput_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSessionCode || sender is not TextBox textBox)
            return;

        var normalized = RosterSyncTransferService.NormalizeSessionCode(textBox.Text);
        if (!string.Equals(textBox.Text, normalized, StringComparison.Ordinal))
        {
            _isUpdatingSessionCode = true;
            textBox.Text = normalized;
            _isUpdatingSessionCode = false;
        }

        if (normalized.Length == SessionCodeLength)
            await VerifySessionCodeAsync();
        else
            InvalidateSessionCodeImport();
    }

    private async Task VerifySessionCodeAsync()
    {
        var code = GetSessionCode();
        if (code.Length != SessionCodeLength)
            return;

        CancelSessionCodeVerification();
        var cancellation = new CancellationTokenSource();
        _sessionCodeVerificationCancellationTokenSource = cancellation;
        CanImport = false;
        StatusText = SessionCodeVerifyingText;
        try
        {
            var result = await _syncTransferService.ImportSessionAsync(code, cancellation.Token);
            if (!ReferenceEquals(_sessionCodeVerificationCancellationTokenSource, cancellation))
                return;
            if (result.Document.Version != 1 || result.Document.Kind != RosterTransferKind.Students)
                throw new InvalidDataException(Text("M_QrWrongType"));

            LoadQrStudents(result.Document);
            CanImport = true;
            StatusText = string.Format(Text("C_SessionCodeLoaded"), _qrStudents?.Count ?? 0);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer keystroke superseded this verification request.
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_sessionCodeVerificationCancellationTokenSource, cancellation))
                return;
            PreviewRows.Clear();
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
            CanImport = false;
            StatusText = string.Format(Text("M_SessionCodeInvalid"), exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_sessionCodeVerificationCancellationTokenSource, cancellation))
                _sessionCodeVerificationCancellationTokenSource = null;
            cancellation.Dispose();
        }
    }

    private void ResetSessionCodeInput()
    {
        _isUpdatingSessionCode = true;
        var textBox = this.FindControl<TextBox>("SessionCodeInput");
        if (textBox is not null)
            textBox.Text = string.Empty;
        _isUpdatingSessionCode = false;
        textBox?.Focus();
    }

    private void InvalidateSessionCodeImport()
    {
        CancelSessionCodeVerification();
        _qrStudents = null;
        PreviewRows.Clear();
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
        CanImport = false;
        if (IsSessionCodeImportMode)
            StatusText = string.Empty;
    }

    private void CancelSessionCodeVerification()
    {
        var cancellation = _sessionCodeVerificationCancellationTokenSource;
        _sessionCodeVerificationCancellationTokenSource = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private string GetSessionCode() =>
        RosterSyncTransferService.NormalizeSessionCode(this.FindControl<TextBox>("SessionCodeInput")?.Text);

    private async Task StopQrScannerAsync(bool keepStatus = false)
    {
        var cancellation = Interlocked.Exchange(ref _qrScanCancellationTokenSource, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        var wasScanning = _isScanningQr;
        _isScanningQr = false;
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
        if (wasScanning)
            NotifyQrScannerStateChanged();
    }

    private async Task RestartQrScannerForCameraChangeAsync()
    {
        if (!IsQrImportMode || _isDrawerClosed)
            return;

        await StopQrScannerAsync(keepStatus: true);
        await ToggleQrScannerAsync();
    }

    private async Task LoadCameraOptionsAsync()
    {
        try
        {
            var selectedId = _selectedCameraOption?.Device.Id;
            var options = await _qrCameraCaptureFactory.GetAvailableOptionsAsync(CancellationToken.None);
            if (_isDrawerClosed)
                return;

            CameraOptions.Clear();
            foreach (var option in options)
                CameraOptions.Add(option);

            _selectedCameraOption = CameraOptions.FirstOrDefault(option => option.Device.Id == selectedId) ??
                                    CameraOptions.FirstOrDefault();
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCameraOption)));
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCameraSelection)));
        }
        catch (OperationCanceledException)
        {
            // Closing the drawer can cancel a platform device query.
        }
        catch (Exception)
        {
            CameraOptions.Clear();
            _selectedCameraOption = null;
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCameraOption)));
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCameraSelection)));
        }
    }

    private void NotifyQrScannerStateChanged()
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanScanQr)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQrScanning)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScanQrLabel)));
    }

    private async Task ConfirmDuplicateNamesAsync(List<Student> students, IReadOnlyList<string> duplicatedNames)
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
            _logger.LogInformation("点名名单导入保留重复姓名：有效行数={Count}。", students.Count);
            _importHandler(students);
            return;
        }

        if (result == FAContentDialogResult.Secondary)
        {
            RosterImportParser.RenameDuplicatedStudents(students);
            _logger.LogInformation("点名名单导入已自动处理重复姓名：有效行数={Count}。", students.Count);
            _importHandler(students);
        }
    }

    private async void CancelButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ((IDrawerCloseAware)this).OnDrawerClosedAsync();
        if (CloseHandler is not null)
            CloseHandler();
        else
            SettingsView.Current?.CloseDrawer();
    }

    async Task IDrawerCloseAware.OnDrawerClosedAsync()
    {
        _isDrawerClosed = true;
        CancelSessionCodeVerification();
        await StopQrScannerAsync();
        ReleaseTemporaryFile();
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

    public record ImportPreviewRow(string Id, string Name, string Gender, string Group, string Tags);
}
