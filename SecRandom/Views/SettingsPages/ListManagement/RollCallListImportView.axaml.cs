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
using Avalonia.Input;
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
using LR = SecRandom.Langs.SettingsPages.ListManagement.RollCallList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

public partial class RollCallListImportView : UserControl, INotifyPropertyChanged
{
    private Action<IReadOnlyList<Student>> _importHandler;
    private readonly List<Dictionary<string, string>> _rows = [];
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
    private bool _isScanningQr;
    private bool _isUpdatingSessionCode;
    private readonly string[] _sessionCode = new string[12];
    private CancellationTokenSource? _sessionCodeVerificationCancellationTokenSource;
    private List<Student>? _qrStudents;
    private readonly RosterTransferService _transferService = IAppHost.GetService<RosterTransferService>();
    private readonly RosterSyncTransferService _syncTransferService = IAppHost.GetService<RosterSyncTransferService>();
    private readonly RosterTransferService.RosterQrImportAccumulator _qrImport;
    private CancellationTokenSource? _qrScanCancellationTokenSource;
    private LinuxRosterQrCameraCapture? _linuxQrCameraCapture;
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
        DataContext = this;
        InitializeComponent();
        FileImportMenuItem.Header = FileImportModeLabel;
        QuickQrImportMenuItem.Header = QuickQrImportModeLabel;
        OfflineQrImportMenuItem.Header = OfflineQrImportModeLabel;
        SessionCodeImportMenuItem.Header = SessionCodeImportModeLabel;
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
    public bool IsCameraPreviewSupported => !OperatingSystem.IsLinux();
    public string FileImportModeLabel => Text("C_ImportExcelCsv");
    public string QuickQrImportModeLabel => Text("C_ImportQuickQr");
    public string OfflineQrImportModeLabel => Text("C_ImportOfflineQr");
    public string SessionCodeImportModeLabel => Text("C_ImportSessionCode");
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
        _logger.LogInformation("已加载点名名单导入文件：文件={FileName}，行数={RowCount}。", file.Name, rows.Count);
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

    private StudentRosterColumnMapping CurrentMapping => new(
        IsSelectedColumn(IdColumn) ? IdColumn : null,
        IsSelectedColumn(NameColumn) ? NameColumn : null,
        IsSelectedColumn(GenderColumn) ? GenderColumn : null,
        IsSelectedColumn(GroupColumn) ? GroupColumn : null,
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

        var parseResult = RosterImportParser.ParseStudents(_rows, CurrentMapping);
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

    private async void ImportSplitButton_OnClick(object? sender, RoutedEventArgs e) =>
        await OpenFileImportAsync();

    private async void FileImportMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        await OpenFileImportAsync();
    }

    private async void QuickQrImportMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        await StopQrScannerAsync();
        SelectQrImportSource(RosterImportMode.QuickQr);
        await ToggleQrScannerAsync();
    }

    private async void OfflineQrImportMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        await StopQrScannerAsync();
        SelectQrImportSource(RosterImportMode.OfflineQr);
        await ToggleQrScannerAsync();
    }

    private async void SessionCodeImportMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        await StopQrScannerAsync();
        SelectSessionCodeImportSource();
    }

    private async Task OpenFileImportAsync()
    {
        _selectedImportMode = RosterImportMode.ExcelCsv;
        IsQrImportMode = false;
        NotifyImportModeChanged();
        ResetPreviewAndImportState();
        await StopQrScannerAsync();
        await SelectFileAsync();
        if (_rows.Count == 0)
            StatusText = LR.M_SelectFileFirst;
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
        ResetSessionCodeBoxes();
        CancelSessionCodeVerification();
        CanImport = false;
        StatusText = SessionCodeHint;
        NotifyImportModeChanged();
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
    }

    private void ResetPreviewAndImportState()
    {
        _rows.Clear();
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

    private async void CameraControl_OnCameraError(object? sender, string error)
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

    private async void SessionCodeBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSessionCode || sender is not TextBox textBox || !TryGetSessionCodeIndex(textBox, out var index))
            return;

        var normalized = RosterSyncTransferService.NormalizeSessionCode(textBox.Text);
        if (normalized.Length > 1)
        {
            SetSessionCodeFrom(index, normalized);
        }
        else
        {
            _sessionCode[index] = normalized;
            if (!string.Equals(textBox.Text, normalized, StringComparison.Ordinal))
            {
                _isUpdatingSessionCode = true;
                textBox.Text = normalized;
                _isUpdatingSessionCode = false;
            }
            if (normalized.Length == 1 && index < _sessionCode.Length - 1)
                GetSessionCodeBox(index + 1)?.Focus();
        }

        if (GetSessionCode().Length == _sessionCode.Length)
            await VerifySessionCodeAsync();
        else
            InvalidateSessionCodeImport();
    }

    private void SessionCodeBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back || sender is not TextBox textBox || !TryGetSessionCodeIndex(textBox, out var index) || index == 0)
            return;
        if (!string.IsNullOrEmpty(textBox.Text) && textBox.CaretIndex > 0)
            return;

        var previous = GetSessionCodeBox(index - 1);
        if (previous is null)
            return;
        previous.Focus();
        previous.Text = string.Empty;
        e.Handled = true;
    }

    private async Task VerifySessionCodeAsync()
    {
        var code = GetSessionCode();
        if (code.Length != _sessionCode.Length)
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

    private void SetSessionCodeFrom(int startIndex, string value)
    {
        var normalized = RosterSyncTransferService.NormalizeSessionCode(value);
        _isUpdatingSessionCode = true;
        for (var index = startIndex; index < _sessionCode.Length; index++)
        {
            var offset = index - startIndex;
            var character = offset < normalized.Length ? normalized[offset].ToString() : string.Empty;
            _sessionCode[index] = character;
            var textBox = GetSessionCodeBox(index);
            if (textBox is not null && !string.Equals(textBox.Text, character, StringComparison.Ordinal))
                textBox.Text = character;
        }
        _isUpdatingSessionCode = false;
        GetSessionCodeBox(Math.Min(startIndex + normalized.Length, _sessionCode.Length - 1))?.Focus();
    }

    private void ResetSessionCodeBoxes()
    {
        _isUpdatingSessionCode = true;
        for (var index = 0; index < _sessionCode.Length; index++)
        {
            _sessionCode[index] = string.Empty;
            var textBox = GetSessionCodeBox(index);
            if (textBox is not null)
                textBox.Text = string.Empty;
        }
        _isUpdatingSessionCode = false;
        GetSessionCodeBox(0)?.Focus();
    }

    private void InvalidateSessionCodeImport()
    {
        CancelSessionCodeVerification();
        _qrStudents = null;
        PreviewRows.Clear();
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreview)));
        CanImport = false;
        if (IsSessionCodeImportMode)
            StatusText = SessionCodeHint;
    }

    private void CancelSessionCodeVerification()
    {
        var cancellation = _sessionCodeVerificationCancellationTokenSource;
        _sessionCodeVerificationCancellationTokenSource = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private string GetSessionCode() => string.Concat(_sessionCode);

    private TextBox? GetSessionCodeBox(int index) => this.FindControl<TextBox>($"SessionCodeBox{index}");

    private static bool TryGetSessionCodeIndex(TextBox textBox, out int index) =>
        int.TryParse(textBox.Tag?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
        index is >= 0 and < 12;

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
        catch (Exception)
        {
            // A provider can already be stopped when the drawer closes.
        }
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
        await StopQrScannerAsync();
        CancelSessionCodeVerification();
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

    public record ImportPreviewRow(string Id, string Name, string Gender, string Group, string Tags);
}
