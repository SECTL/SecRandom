using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Services.Draw;
using SecRandom.Services.Seating;
using SecRandom.Services.Security;
using SecRandom.Services.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.ViewModels.MainPages;

public sealed partial class SeatingChartPageViewModel : ViewModelBase
{
    private readonly IProfileService _profileService;
    private readonly SeatingChartService _seatingCharts;
    private readonly CsisInterchangeService _csis;
    private readonly DrawEngine _drawEngine;
    private readonly IDrawTemporaryRecordService _temporaryRecords;
    private readonly VerificationDrawCoordinator _verification;
    private readonly ISecurityService _security;

    [ObservableProperty] private SeatingChart? _selectedChart;
    [ObservableProperty] private SeatingSeatItem? _selectedSeat;
    [ObservableProperty] private SeatingStudentItem? _selectedStudent;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private string _statusText = "请选择或创建一个座位表。";
    [ObservableProperty] private string _resultText = string.Empty;

    public SeatingChartPageViewModel(
        MainConfigHandler configHandler,
        IProfileService profileService,
        SeatingChartService seatingCharts,
        CsisInterchangeService csis,
        DrawEngine drawEngine,
        IDrawTemporaryRecordService temporaryRecords,
        VerificationDrawCoordinator verification,
        ISecurityService security)
        : base(configHandler)
    {
        _profileService = profileService;
        _seatingCharts = seatingCharts;
        _csis = csis;
        _drawEngine = drawEngine;
        _temporaryRecords = temporaryRecords;
        _verification = verification;
        _security = security;
        Refresh();
    }

    public ObservableCollection<SeatingChart> Charts { get; } = [];
    public ObservableCollection<SeatingSeatItem> Seats { get; } = [];
    public ObservableCollection<SeatingStudentItem> UnassignedStudents { get; } = [];
    public int Columns => Math.Max(1, SelectedChart?.Columns ?? 1);
    public string CurrentListName => _profileService.StudentListConfig?.Name ?? "default";
    public int AssignedCount => Seats.Count(seat => seat.Student is not null && !seat.IsDisabled);
    public int EligibleCount => Seats.Count(seat => seat.Student?.IsCandidate == true && !seat.IsDisabled && !HasReachedRepeatLimit(seat.Student));
    public bool CanStartDraw => !IsEditing && EligibleCount > 0;

    partial void OnSelectedChartChanged(SeatingChart? value)
    {
        RebuildDisplay();
    }

    partial void OnDrawCountChanged(int value)
    {
        var normalized = Math.Clamp(value, 1, Math.Max(1, EligibleCount));
        if (normalized != value)
            DrawCount = normalized;
    }

    [RelayCommand]
    private void Refresh()
    {
        _seatingCharts.Reload();
        var selectedId = SelectedChart?.Id;
        Charts.Clear();
        foreach (var chart in _seatingCharts.Current.Charts)
            Charts.Add(chart);
        SelectedChart = Charts.FirstOrDefault(chart => chart.Id == selectedId) ?? Charts.FirstOrDefault();
        OnPropertyChanged(nameof(CurrentListName));
    }

    [RelayCommand]
    private void CreateChart()
    {
        var chart = new SeatingChart { Name = $"座位表 {Charts.Count + 1}" };
        _seatingCharts.Current.Charts.Add(chart);
        Charts.Add(chart);
        SelectedChart = chart;
        IsEditing = true;
        StatusText = "已创建座位表，请分配学生后保存。";
    }

    [RelayCommand]
    private void DeleteChart()
    {
        if (SelectedChart is null)
            return;
        _seatingCharts.Current.Charts.Remove(SelectedChart);
        Charts.Remove(SelectedChart);
        SelectedChart = Charts.FirstOrDefault();
        Save();
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedChart is null)
            return;
        ApplySeatEdits();
        _seatingCharts.Save();
        IsEditing = false;
        StatusText = "座位表已保存。";
    }

    [RelayCommand]
    private void ToggleEditing()
    {
        if (SelectedChart is null)
            CreateChart();
        else
            IsEditing = !IsEditing;
    }

    [RelayCommand]
    private void SelectSeat(SeatingSeatItem? seat)
    {
        if (seat is null || !IsEditing)
            return;
        SelectedSeat = seat;
        if (SelectedStudent is not null && !seat.IsDisabled)
            AssignSelectedStudent();
    }

    [RelayCommand]
    private void SelectStudent(SeatingStudentItem? student)
    {
        if (student is null || !IsEditing)
            return;
        SelectedStudent = student;
        if (SelectedSeat is not null && !SelectedSeat.IsDisabled)
            AssignSelectedStudent();
    }

    [RelayCommand]
    private void ClearSelectedSeat()
    {
        if (!IsEditing || SelectedSeat is null)
            return;
        SelectedSeat.Student = null;
        RebuildUnassignedStudents();
        RefreshSummary();
    }

    [RelayCommand]
    private void ToggleSelectedSeatDisabled()
    {
        if (!IsEditing || SelectedSeat is null)
            return;
        SelectedSeat.IsDisabled = !SelectedSeat.IsDisabled;
        if (SelectedSeat.IsDisabled)
            SelectedSeat.Student = null;
        RebuildUnassignedStudents();
        RefreshSummary();
    }

    [RelayCommand]
    private void AddRow()
    {
        if (SelectedChart is null || !IsEditing)
            return;
        SelectedChart.Rows++;
        RebuildDisplay();
    }

    [RelayCommand]
    private void IncreaseCount()
    {
        DrawCount = Math.Min(DrawCount + 1, Math.Max(1, EligibleCount));
    }

    [RelayCommand]
    private void DecreaseCount()
    {
        DrawCount = Math.Max(1, DrawCount - 1);
    }

    [RelayCommand]
    private void AddColumn()
    {
        if (SelectedChart is null || !IsEditing)
            return;
        SelectedChart.Columns++;
        RebuildDisplay();
    }

    [RelayCommand]
    private async Task StartDrawAsync()
    {
        if (!await _security.AuthorizeAsync(SecurityOperation.RollCallStart, () => Task.CompletedTask))
            return;
        if (IsEditing)
        {
            StatusText = "请先保存或退出编辑模式。";
            return;
        }

        var candidates = Seats.Where(seat => !seat.IsDisabled && seat.Student?.IsCandidate == true)
            .Select(seat => seat.Student!)
            .Where(student => !HasReachedRepeatLimit(student))
            .ToList();
        if (candidates.Count == 0)
        {
            StatusText = "没有可抽取的已入座学生。";
            return;
        }

        var count = Math.Clamp(DrawCount, 1, candidates.Count);
        try
        {
            var outcome = await _verification.DrawStudentsAsync(count, candidates, DrawSettingsType.RollCall);
            var winners = outcome.Winners.ToList();
            var weights = Config.RollCallSettings.DrawType == DrawType.Fair
                ? _drawEngine.CalculateStudentWeight((_profileService.CurrentStudentList?.Students ?? []).ToList())
                    .Where(item => winners.Contains(item.Candidate))
                    .ToDictionary(item => item.Candidate, item => item.Weight)
                : winners.ToDictionary(student => student, _ => 1d);
            _profileService.RecordStudentHistory(winners, DateTime.Now, count, weights: weights);
            _temporaryRecords.RecordStudents(CurrentListName, string.Empty, string.Empty, winners);
            ResultText = string.Join("、", winners.Select(student => string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name));
            StatusText = $"已抽取 {winners.Count} 人。";
            RefreshSummary();
        }
        catch (Exception)
        {
            StatusText = "抽取失败，请检查候选学生和验证服务。";
        }
    }

    [RelayCommand]
    private async Task ResetDrawAsync()
    {
        if (!await _security.AuthorizeAsync(SecurityOperation.RollCallReset, () => Task.CompletedTask))
            return;
        _temporaryRecords.ClearStudentScope(CurrentListName, string.Empty, string.Empty);
        ResultText = string.Empty;
        StatusText = "已清除本名单的临时抽取记录。";
        RefreshSummary();
    }

    public void ImportCsps(string json)
    {
        var result = _csis.ReadSeatingChart(json, _profileService.CurrentStudentList?.Students ?? []);
        if (result.Issues.Count > 0)
            throw new InvalidDataException($"有 {result.Issues.Count} 个学生无法唯一匹配，未导入座位表。");
        result.Chart.Name = $"导入座位表 {Charts.Count + 1}";
        _seatingCharts.Current.Charts.Add(result.Chart);
        Charts.Add(result.Chart);
        SelectedChart = result.Chart;
        Save();
    }

    public string ExportCsps()
    {
        if (SelectedChart is null)
            throw new InvalidOperationException("请先选择座位表。");
        return _csis.WriteSeatingChart(SelectedChart, _profileService.CurrentStudentList?.Students ?? []);
    }

    public void ImportCsls(string json)
    {
        var result = _csis.ReadStudentLists(json);
        foreach (var item in result.Classes)
        {
            var config = new StudentListConfig(item.Name);
            config.Data.Students = new ObservableCollection<Student>(item.Students);
            config.Save();
        }
        StatusText = $"已导入 {result.Classes.Count} 个名单。";
        Refresh();
    }

    public string ExportCsls()
    {
        var students = _profileService.CurrentStudentList?.Students ?? [];
        return _csis.WriteStudentLists([(CurrentListName, students)]);
    }

    private void AssignSelectedStudent()
    {
        if (SelectedSeat is null || SelectedStudent is null)
            return;
        foreach (var seat in Seats.Where(item => item.Student?.RecordId == SelectedStudent.Student.RecordId))
            seat.Student = null;
        SelectedSeat.Student = SelectedStudent.Student;
        SelectedStudent = null;
        RebuildUnassignedStudents();
        RefreshSummary();
    }

    private void RebuildDisplay()
    {
        Seats.Clear();
        if (SelectedChart is null)
        {
            RebuildUnassignedStudents();
            RefreshSummary();
            return;
        }

        var studentLookup = (_profileService.CurrentStudentList?.Students ?? [])
            .ToDictionary(student => ProfileRecordIdentity.EnsureRecordId(student).ToString());
        foreach (var row in Enumerable.Range(0, SelectedChart.Rows))
        foreach (var column in Enumerable.Range(0, SelectedChart.Columns))
        {
            var persisted = SelectedChart.Seats.FirstOrDefault(seat => seat.Row == row && seat.Column == column);
            studentLookup.TryGetValue(persisted?.StudentRecordId ?? string.Empty, out var student);
            Seats.Add(new SeatingSeatItem(row, column, student, persisted?.IsDisabled ?? false));
        }
        RebuildUnassignedStudents();
        RefreshSummary();
    }

    private void ApplySeatEdits()
    {
        if (SelectedChart is null)
            return;
        SelectedChart.Seats = Seats.Where(seat => seat.IsDisabled || seat.Student is not null)
            .Select(seat => new SeatingChartSeat
            {
                Row = seat.Row,
                Column = seat.Column,
                IsDisabled = seat.IsDisabled,
                StudentRecordId = seat.Student is null ? null : ProfileRecordIdentity.EnsureRecordId(seat.Student),
                DeskmatePosition = SelectedChart.IsDeskmateLayout ? seat.Column % 2 == 0 ? "left" : "right" : null
            }).ToList();
    }

    private void RebuildUnassignedStudents()
    {
        var assigned = Seats.Where(seat => seat.Student is not null).Select(seat => seat.Student!.RecordId).ToHashSet();
        UnassignedStudents.Clear();
        foreach (var student in (_profileService.CurrentStudentList?.Students ?? []).Where(student => student.IsCandidate && !assigned.Contains(student.RecordId)))
            UnassignedStudents.Add(new SeatingStudentItem(student));
    }

    private bool HasReachedRepeatLimit(Student student)
    {
        var threshold = Config.RollCallSettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, Config.RollCallSettings.HalfRepeat),
            _ => 1
        };
        if (threshold <= 0)
            return false;
        var counts = _temporaryRecords.GetStudentCounts(CurrentListName, string.Empty, string.Empty);
        return counts.TryGetValue(ProfileRecordIdentity.EnsureRecordId(student), out var count) && count >= threshold;
    }

    private void RefreshSummary()
    {
        DrawCount = Math.Clamp(DrawCount, 1, Math.Max(1, EligibleCount));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(AssignedCount));
        OnPropertyChanged(nameof(EligibleCount));
        OnPropertyChanged(nameof(CanStartDraw));
    }
}

public sealed partial class SeatingSeatItem(int row, int column, Student? student, bool isDisabled) : ObservableObject
{
    public int Row { get; } = row;
    public int Column { get; } = column;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private Student? _student = student;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool _isDisabled = isDisabled;
    public string DisplayText => IsDisabled ? "禁用" : Student is null ? "空座位" : string.IsNullOrWhiteSpace(Student.Name) ? Student.Id : Student.Name;
}

public sealed record SeatingStudentItem(Student Student)
{
    public string DisplayText => string.IsNullOrWhiteSpace(Student.Id) ? Student.Name : $"{Student.Id} {Student.Name}".Trim();
}
