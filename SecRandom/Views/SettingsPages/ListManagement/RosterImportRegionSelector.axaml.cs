using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using SecRandom.Core.Services.Profiles;
using SecRandom.Langs.SettingsPages.ListManagement.RosterTransfer;

namespace SecRandom.Views.SettingsPages.ListManagement;

/// <summary>
/// 名单导入的「导入区域」选择器：工作表、表头行、数据行范围，以及用于点选表头行的原始行预览。
/// 文件读取留在宿主视图，控件只接收原始网格并交回当前区域，因此两个名单抽屉和 OOBE 共用同一份实现。
/// 行号与列号都是 1 起的原始表格编号，与用户在 Excel 里看到的一致。
/// </summary>
public partial class RosterImportRegionSelector : UserControl, INotifyPropertyChanged
{
    /// <summary>原始行预览的窗口大小；窗口跟着表头行滑动，列表里始终只有这么多行。</summary>
    private const int RawPreviewWindowSize = 5;

    /// <summary>表头行下拉最多列出的行数；更靠后的行仍可在原始行预览里点选。</summary>
    private const int HeaderRowOptionLimit = 200;

    private IReadOnlyList<IReadOnlyList<string?>> _grid = [];
    private bool _isUpdating;
    private bool _hasGrid;
    private bool _hasHeaderRowWarning;
    private int? _headerRow;
    private int _lastContentRow;
    private decimal _rowCountMaximum = 1;
    private decimal _dataFirstRow = 1;
    private decimal _dataLastRow = 1;
    private string? _selectedSheet;
    private RosterImportHeaderRowOption? _selectedHeaderRowOption;
    private RosterImportRawRow? _selectedRawRow;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public RosterImportRegionSelector()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>区域发生变化（工作表内容、表头行或数据行范围），宿主据此重建列映射与预览。</summary>
    public event EventHandler? RegionChanged;

    /// <summary>用户切到了另一个工作表，宿主需要按新工作表名重新读取文件。</summary>
    public event EventHandler<string>? SheetSelectionChanged;

    public ObservableCollection<string> SheetOptions { get; } = [];
    public ObservableCollection<RosterImportHeaderRowOption> HeaderRowOptions { get; } = [];
    public ObservableCollection<RosterImportRawRow> RawRows { get; } = [];

    public bool HasGrid
    {
        get => _hasGrid;
        private set => SetField(ref _hasGrid, value);
    }

    /// <summary>单工作表的表格（含 CSV）不显示工作表选择行。</summary>
    public bool HasSheetSelection => SheetOptions.Count > 1;

    public bool HasHeaderRowWarning
    {
        get => _hasHeaderRowWarning;
        private set => SetField(ref _hasHeaderRowWarning, value);
    }

    public string ImportRegionLabel => Text("C_ImportRegion");
    public string ImportRegionDescription => Text("C_ImportRegion_D");
    public string SheetLabel => Text("C_Sheet");
    public string HeaderRowLabel => Text("C_HeaderRow");
    public string DataRangeLabel => Text("C_DataRange");
    public string DataRangeSeparator => Text("C_DataRangeSeparator");
    public string RegionHint => Text("C_RegionRawHint");
    public string RegionHintDetail => Text("C_RegionRawHint_D");
    public string HeaderRowWarning => Text("M_HeaderRowNotDetected");

    /// <summary>数据行范围两个数字框的上限。</summary>
    public decimal RowCountMaximum => _rowCountMaximum;

    public string? SelectedSheet
    {
        get => _selectedSheet;
        set
        {
            if (!SetField(ref _selectedSheet, value) || _isUpdating || string.IsNullOrEmpty(value))
                return;

            // 换工作表要重新读文件，交给宿主处理
            SheetSelectionChanged?.Invoke(this, value);
        }
    }

    public RosterImportHeaderRowOption? SelectedHeaderRowOption
    {
        get => _selectedHeaderRowOption;
        set
        {
            if (value is null || !SetField(ref _selectedHeaderRowOption, value) || _isUpdating)
                return;

            ApplyHeaderRow(value.Row);
        }
    }

    public RosterImportRawRow? SelectedRawRow
    {
        get => _selectedRawRow;
        set
        {
            if (value is null || !SetField(ref _selectedRawRow, value) || _isUpdating || value.RowNumber == _headerRow)
                return;

            // 在预览里点一行即把该行设为表头行
            ApplyHeaderRow(value.RowNumber);
        }
    }

    public decimal? DataFirstRow
    {
        get => _dataFirstRow;
        set
        {
            // 清空输入框时保留原值，不把 null 写进区域状态
            if (value is null)
            {
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataFirstRow)));
                return;
            }

            var clamped = ClampRow(value.Value);
            if (!SetField(ref _dataFirstRow, clamped) || _isUpdating)
                return;

            if (_dataLastRow < clamped)
            {
                _dataLastRow = clamped;
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataLastRow)));
            }

            OnRegionChanged();
        }
    }

    public decimal? DataLastRow
    {
        get => _dataLastRow;
        set
        {
            if (value is null)
            {
                NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataLastRow)));
                return;
            }

            var clamped = Math.Clamp(value.Value, _dataFirstRow, _rowCountMaximum);
            if (!SetField(ref _dataLastRow, clamped) || _isUpdating)
                return;

            OnRegionChanged();
        }
    }

    /// <summary>当前区域；列范围固定为全部列，列取舍仍由列映射决定。</summary>
    public RosterImportRegion CurrentRegion => new(_headerRow, (int)_dataFirstRow, (int)_dataLastRow, 1, null);

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    /// <summary>载入一张工作表的原始网格。不会触发 <see cref="RegionChanged"/>，宿主在返回后自行应用区域。</summary>
    public void Load(
        IReadOnlyList<string> sheetNames,
        string? sheetName,
        IReadOnlyList<IReadOnlyList<string?>> grid,
        IReadOnlyList<string> headerKeywords)
    {
        _isUpdating = true;
        try
        {
            _grid = grid;
            SheetOptions.Clear();
            foreach (var name in sheetNames)
                SheetOptions.Add(name);

            _selectedSheet = sheetName;
            _hasGrid = grid.Count > 0;
            // 表格尾部可能存在只有格式、没有内容的空行，行数上限按最后一个有内容的行算
            _lastContentRow = RosterImportRegionBuilder.ResolveLastContentRow(grid);
            _rowCountMaximum = Math.Max(1, _lastContentRow);

            var headerRow = RosterImportRegionBuilder.DetectHeaderRow(grid, headerKeywords);
            // 一行都认不出来时 DetectHeaderRow 会退回第 1 行，此时提示用户手动确认
            _hasHeaderRowWarning = grid.Count > 1 && RosterImportRegionBuilder.ScoreRow(grid[0], headerKeywords) == 0;

            _headerRow = headerRow;
            _dataFirstRow = Math.Clamp(headerRow + 1, 1, _rowCountMaximum);
            _dataLastRow = _rowCountMaximum;

            RebuildHeaderRowOptions(headerRow);
            RebuildRawRows(headerRow);
            _selectedHeaderRowOption = HeaderRowOptions.FirstOrDefault(option => option.Row == headerRow);
            _selectedRawRow = RawRows.FirstOrDefault(row => row.RowNumber == headerRow);
        }
        finally
        {
            _isUpdating = false;
        }

        NotifyStateChanged();
    }

    /// <summary>清空载入状态，用于切换导入方式或关闭预览。</summary>
    public void Clear()
    {
        _isUpdating = true;
        try
        {
            _grid = [];
            SheetOptions.Clear();
            HeaderRowOptions.Clear();
            RawRows.Clear();
            _selectedSheet = null;
            _selectedHeaderRowOption = null;
            _selectedRawRow = null;
            _headerRow = null;
            _hasGrid = false;
            _hasHeaderRowWarning = false;
            _lastContentRow = 0;
            _rowCountMaximum = 1;
            _dataFirstRow = 1;
            _dataLastRow = 1;
        }
        finally
        {
            _isUpdating = false;
        }

        NotifyStateChanged();
    }

    private void ApplyHeaderRow(int? row)
    {
        _isUpdating = true;
        try
        {
            _headerRow = row;
            _selectedHeaderRowOption = HeaderRowOptions.FirstOrDefault(option => option.Row == row);
            // 预览窗口跟着表头行滑动，始终只有 5 行
            RebuildRawRows(row);
            _selectedRawRow = row is null ? null : RawRows.FirstOrDefault(item => item.RowNumber == row);
            // 表头行一变列名就全变，数据行范围跟着回到表头行之后
            _dataFirstRow = row is null ? 1 : Math.Clamp(row.Value + 1, 1, _rowCountMaximum);
            if (_dataLastRow < _dataFirstRow)
                _dataLastRow = _rowCountMaximum;
        }
        finally
        {
            _isUpdating = false;
        }

        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedHeaderRowOption)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRawRow)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataFirstRow)));
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataLastRow)));
        OnRegionChanged();
    }

    private void RebuildHeaderRowOptions(int detectedRow)
    {
        HeaderRowOptions.Clear();
        // 「无表头」放在最前面：行列表最多有 200 项，放到最后用户得滚很久才点得到
        HeaderRowOptions.Add(new RosterImportHeaderRowOption(null, Text("C_HeaderRowNone")));
        // 只列到最后一个有内容的行，识别结果落在更后面时也要能被选中
        var limit = Math.Max(Math.Min(_lastContentRow, HeaderRowOptionLimit), detectedRow);
        for (var row = 1; row <= limit; row++)
            HeaderRowOptions.Add(new RosterImportHeaderRowOption(row, string.Format(Text("C_HeaderRowFormat"), row)));
    }

    /// <summary>
    /// 重建 5 行预览窗口：以选中的表头行为中心，随选择向下滑动，窗口大小始终固定。
    /// 尾部只有格式没有内容的空行不参与，行号对齐表格里真正有内容的范围。
    /// </summary>
    private void RebuildRawRows(int? headerRow)
    {
        RawRows.Clear();
        if (_lastContentRow <= 0)
            return;

        var size = Math.Min(RawPreviewWindowSize, _lastContentRow);
        var start = headerRow is { } selectedRow
            ? Math.Clamp(selectedRow - (size - 1) / 2, 1, _lastContentRow - size + 1)
            : 1;

        for (var row = start; row < start + size; row++)
        {
            RawRows.Add(new RosterImportRawRow(
                row,
                string.Format(Text("C_RowFormat"), row),
                BuildRowText(_grid[row - 1])));
        }
    }

    private string BuildRowText(IReadOnlyList<string?> cells)
    {
        // 整张表的列数是统一的，右侧空列直接截掉，只保留行内有效内容
        var last = -1;
        for (var index = 0; index < cells.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(cells[index]))
                last = index;
        }

        if (last < 0)
            return Text("C_RegionBlankRow");

        return string.Join(" | ", Enumerable.Range(0, last + 1).Select(index => cells[index]?.Trim() ?? string.Empty));
    }

    private decimal ClampRow(decimal value) => Math.Clamp(value, 1, _rowCountMaximum);

    private void OnRegionChanged() => RegionChanged?.Invoke(this, EventArgs.Empty);

    private void NotifyStateChanged()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(HasGrid), nameof(HasSheetSelection), nameof(HasHeaderRowWarning), nameof(RowCountMaximum),
                     nameof(SelectedSheet), nameof(SelectedHeaderRowOption), nameof(SelectedRawRow),
                     nameof(DataFirstRow), nameof(DataLastRow)
                 })
        {
            NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private static string Text(string name) => RosterTransferText.Get(name);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>表头行选项；<see cref="Row"/> 为 null 表示整张表没有表头行。</summary>
public sealed record RosterImportHeaderRowOption(int? Row, string Label);

/// <summary>原始表格预览中的一行。</summary>
public sealed record RosterImportRawRow(int RowNumber, string RowLabel, string Text);
