using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Models;
using SecRandom.ViewModels;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using History = SecRandom.Shared.Models.Profile.History;
using SR = SecRandom.Langs.MainPages.History.Resources;
using RollCallSR = SecRandom.Langs.MainPages.RollCall.Resources;
using ProfileHistory = SecRandom.Shared.Models.Profile.History;

namespace SecRandom.ViewModels.SettingsPages.History;

public sealed partial class RollCallHistoryViewModel : ViewModelBase
{
    private readonly DrawEngine _drawEngine = IAppHost.GetService<DrawEngine>();
    private readonly IHistoryQueryService _historyQueryService = IAppHost.GetService<IHistoryQueryService>();
    private readonly IProfileCatalogManager _catalogManager = IAppHost.GetService<IProfileCatalogManager>();

    private StudentHistory? _history;
    private StudentList? _studentList;
    private Dictionary<string, Student> _studentByKey = [];
    private Dictionary<string, StudentInfo> _studentInfoByKey = [];
    private HashSet<string> _uniqueLegacyKeys = [];
    private int _studentIdPadWidth;

    [ObservableProperty] private string? _selectedClassName;
    [ObservableProperty] private string _selectedMode = HistoryMode.Overview;
    [ObservableProperty] private string _selectedSubject = string.Empty;

    public RollCallHistoryViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        RefreshCommand = new RelayCommand(Refresh);
        RefreshClassNames();
        SelectedClassName = ResolveInitial(ClassNames, Config.HistoryManagementSettings.SelectedClassName);
    }

    public ObservableCollection<string> ClassNames { get; } = [];
    public ObservableCollection<HistoryModeOption> ModeOptions { get; } = [];
    public ObservableCollection<HistoryModeOption> SubjectOptions { get; } = [];
    public ObservableCollection<HistoryDisplayRow> Rows { get; } = [];
    public bool HasWeightRows => Rows.Any(row => !string.IsNullOrWhiteSpace(row.Weight));
    public bool HasSubjectRows => Rows.Any(row => !string.IsNullOrWhiteSpace(row.Subject));
    public bool IsSubjectFilteringAvailable => Config.LinkageSettings.DataSource != LinkageDataSource.Off
        && Config.LinkageSettings.SubjectHistoryFilterEnabled;
    public bool ShouldShowSubjectColumn => HasSubjectRows
        && SelectedMode != HistoryMode.Overview;

    public IRelayCommand RefreshCommand { get; }

    private static string? ResolveInitial(IReadOnlyList<string> names, string preferred)
    {
        if (names.Count == 0) return null;
        return names.Contains(preferred) ? preferred : names[0];
    }

    public void Refresh()
    {
        var selectedClassName = SelectedClassName;
        var selectedMode = SelectedMode;
        var selectedSubject = SelectedSubject;
        RefreshClassNames();

        SelectedClassName = ResolveRefreshSelection(ClassNames, selectedClassName);
        Load(selectedMode, selectedSubject);
    }

    private void RefreshClassNames()
    {
        ClassNames.Clear();
        foreach (var name in _catalogManager.GetStudentListNames()
                     .Concat(_historyQueryService.GetStudentHistoryNames())
                     .Distinct()
                     .OrderBy(name => name, StringComparer.Ordinal))
            ClassNames.Add(name);
    }

    partial void OnSelectedClassNameChanged(string? value) => Load();
    partial void OnSelectedModeChanged(string value) => BuildRows();
    partial void OnSelectedSubjectChanged(string value) => BuildRows();

    public void RefreshSubjectFiltering()
    {
        OnPropertyChanged(nameof(IsSubjectFilteringAvailable));
        OnPropertyChanged(nameof(ShouldShowSubjectColumn));
        BuildRows();
    }

    private void Load(string? preferredMode = null, string? preferredSubject = null)
    {
        Rows.Clear();
        _history = null;
        _studentList = null;
        _studentByKey = [];
        _studentInfoByKey = [];
        _uniqueLegacyKeys = [];
        _studentIdPadWidth = 0;

        if (string.IsNullOrWhiteSpace(SelectedClassName))
        {
            RebuildModeOptions(preferredMode);
            RebuildSubjectOptions(preferredSubject);
            return;
        }

        // 只读快照：缺失的历史按空历史处理（保留既有“无历史文件仍显示名单行”的行为），
        // 缺失的名单按空名单处理；读取失败由查询/目录服务内部记录警告。
        _history = _historyQueryService.LoadStudentHistory(SelectedClassName) ?? new StudentHistory(SelectedClassName);

        _studentList = _catalogManager.LoadStudentList(SelectedClassName);
        if (_studentList is not null)
        {
            _uniqueLegacyKeys = ProfileRecordIdentity.BuildUniqueStudentLegacyKeySet(_studentList.Students);
            _studentByKey = BuildStudentMap(_studentList.Students, _uniqueLegacyKeys);
            _studentInfoByKey = BuildStudentInfoMap(_studentList.Students, _uniqueLegacyKeys);
            _studentIdPadWidth = CalculateNumericPadWidth(_studentList.Students.Select(student => student.Id));
        }

        RebuildModeOptions(preferredMode);
        RebuildSubjectOptions(preferredSubject);
        BuildRows();
    }

    private void RebuildModeOptions(string? preferredMode = null)
    {
        var current = preferredMode ?? SelectedMode;
        ModeOptions.Clear();
        ModeOptions.Add(new HistoryModeOption { Key = HistoryMode.Overview, DisplayName = SR.C_ModeOverview });
        ModeOptions.Add(new HistoryModeOption { Key = HistoryMode.Records, DisplayName = SR.C_ModeRecords });

        foreach (var student in GetVisibleStudents())
        {
            var key = ProfileRecordIdentity.EnsureRecordId(student);
            ModeOptions.Add(new HistoryModeOption { Key = key, DisplayName = FormatStudentName(student) });
        }

        if (_history != null)
            foreach (var key in _history.Students.Keys.Where(key => !ModeOptions.Any(option => option.Key == key)))
                ModeOptions.Add(new HistoryModeOption { Key = key, DisplayName = ResolveStudentInfo(key).Name });

        if (!ModeOptions.Any(option => option.Key == current))
            current = HistoryMode.Overview;

        SelectedMode = current;
    }

    private void RebuildSubjectOptions(string? preferredSubject = null)
    {
        var current = preferredSubject ?? SelectedSubject;
        SubjectOptions.Clear();
        SubjectOptions.Add(new HistoryModeOption { Key = string.Empty, DisplayName = SR.C_AllSubjects });

        if (_history is not null)
        {
            foreach (var subject in _history.Students.Values
                         .SelectMany(history => history.Histories)
                         .Select(item => item.CourseName)
                         .Where(subject => !string.IsNullOrWhiteSpace(subject))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(subject => subject, StringComparer.CurrentCulture))
            {
                SubjectOptions.Add(new HistoryModeOption
                {
                    Key = subject,
                    DisplayName = FormatSubject(subject)
                });
            }
        }

        if (!SubjectOptions.Any(option => option.Key == current))
            current = string.Empty;

        SelectedSubject = current;
    }

    private void BuildRows()
    {
        Rows.Clear();
        try
        {
            if (_history is null)
                return;

            var mode = SelectedMode;
            if (mode == HistoryMode.Overview)
            {
                var predictedWeights = BuildPredictedWeightMap();
                foreach (var student in GetVisibleStudents())
                    Rows.Add(BuildOverviewRow(student, predictedWeights));

                AddOrphanOverviewRows();
            }
            else if (mode == HistoryMode.Records)
            {
                foreach (var student in GetVisibleStudents())
                    AddHistoryRows(student);

                AddOrphanHistoryRows();
                SortByTimeDesc(Rows);
            }
            else if (_studentByKey.TryGetValue(mode, out var student))
            {
                var history = ResolveHistory(student);
                if (history is null)
                    return;

                var info = StudentInfo.From(student);
                foreach (var item in GetFilteredHistoryItems(history))
                    Rows.Add(BuildEventRow(info, item, _studentIdPadWidth));
                SortByTimeDesc(Rows);
            }
            else if (_history.Students.TryGetValue(mode, out var target))
            {
                var info = ResolveStudentInfo(mode);
                foreach (var item in GetFilteredHistoryItems(target))
                    Rows.Add(BuildEventRow(info, item, _studentIdPadWidth));
                SortByTimeDesc(Rows);
            }
        }
        finally
        {
            OnPropertyChanged(nameof(HasWeightRows));
            OnPropertyChanged(nameof(HasSubjectRows));
            OnPropertyChanged(nameof(ShouldShowSubjectColumn));
        }
    }

    private IEnumerable<Student> GetVisibleStudents()
    {
        return _studentList?.Students.Where(student => student.IsCandidate) ?? [];
    }

    private Dictionary<Student, double> BuildPredictedWeightMap()
    {
        var visibleStudents = GetVisibleStudents().ToList();
        if (Config.RollCallSettings.DrawType != DrawType.Fair)
            return [];

        return _drawEngine.CalculateStudentWeight(visibleStudents, courseName: GetSelectedSubjectFilter())
            .ToDictionary(candidate => candidate.Candidate, candidate => candidate.Weight);
    }

    private HistoryDisplayRow BuildOverviewRow(Student student, IReadOnlyDictionary<Student, double> predictedWeights)
    {
        var history = ResolveHistory(student);
        return new HistoryDisplayRow
        {
            Id = FormatNumericId(student.Id, _studentIdPadWidth),
            Name = student.Name,
            Gender = student.Gender,
            Group = student.Group,
            TotalCount = GetSubjectHistoryCount(history),
            Weight = predictedWeights.TryGetValue(student, out var weight)
                ? FormatWeight(weight)
                : string.Empty
        };
    }

    private void AddHistoryRows(Student student)
    {
        var history = ResolveHistory(student);
        if (history is null)
            return;

        var info = StudentInfo.From(student);
        foreach (var item in GetFilteredHistoryItems(history))
            Rows.Add(BuildEventRow(info, item, _studentIdPadWidth));
    }

    private ProfileHistory? ResolveHistory(Student student)
    {
        if (_history is null)
            return null;

        return ProfileRecordIdentity.GetStudentHistory(_history, student, _uniqueLegacyKeys.Contains);
    }

    private void AddOrphanOverviewRows()
    {
        if (_history is null)
            return;

        var knownKeys = BuildKnownStudentHistoryKeys();
        foreach (var (key, history) in _history.Students.Where(pair => !knownKeys.Contains(pair.Key)))
        {
            var info = ResolveStudentInfo(key);
            Rows.Add(new HistoryDisplayRow
            {
                Id = FormatNumericId(info.Id, _studentIdPadWidth),
                Name = info.Name,
                Gender = info.Gender,
                Group = info.Group,
                TotalCount = GetSubjectHistoryCount(history),
                Weight = FormatLatestWeight(history)
            });
        }
    }

    private void AddOrphanHistoryRows()
    {
        if (_history is null)
            return;

        var knownKeys = BuildKnownStudentHistoryKeys();
        foreach (var (key, history) in _history.Students.Where(pair => !knownKeys.Contains(pair.Key)))
        {
            var info = ResolveStudentInfo(key);
            foreach (var item in GetFilteredHistoryItems(history))
                Rows.Add(BuildEventRow(info, item, _studentIdPadWidth));
        }
    }

    private HashSet<string> BuildKnownStudentHistoryKeys()
    {
        HashSet<string> keys = [];
        foreach (var student in GetVisibleStudents())
        {
            keys.Add(ProfileRecordIdentity.EnsureRecordId(student));
            foreach (var key in ProfileRecordIdentity.GetLegacyStudentHistoryKeys(student).Where(_uniqueLegacyKeys.Contains))
                keys.Add(key);
        }

        return keys;
    }

    private int GetSubjectHistoryCount(ProfileHistory? history)
    {
        if (history is null)
            return 0;

        return string.IsNullOrWhiteSpace(GetSelectedSubjectFilter())
            ? history.TotalCount
            : GetFilteredHistoryItems(history).Count();
    }

    private IEnumerable<HistoryItem> GetFilteredHistoryItems(ProfileHistory history)
    {
        var subject = GetSelectedSubjectFilter();
        return string.IsNullOrWhiteSpace(subject)
            ? history.Histories
            : history.Histories.Where(item => string.Equals(item.CourseName, subject, StringComparison.Ordinal));
    }

    private string GetSelectedSubjectFilter() => IsSubjectFilteringAvailable ? SelectedSubject : string.Empty;

    private static string? ResolveRefreshSelection(IReadOnlyList<string> names, string? current) =>
        !string.IsNullOrWhiteSpace(current) && names.Contains(current) ? current : ResolveInitial(names, string.Empty);

    private StudentInfo ResolveStudentInfo(string historyKey)
    {
        return _studentInfoByKey.GetValueOrDefault(historyKey) ?? StudentInfo.Unknown(historyKey);
    }

    private static Dictionary<string, StudentInfo> BuildStudentInfoMap(
        IEnumerable<Student> students,
        ISet<string> uniqueLegacyKeys)
    {
        Dictionary<string, StudentInfo> result = [];
        foreach (var student in students)
        {
            var info = StudentInfo.From(student);
            result[ProfileRecordIdentity.EnsureRecordId(student)] = info;
            foreach (var key in ProfileRecordIdentity.GetLegacyStudentHistoryKeys(student).Where(uniqueLegacyKeys.Contains))
                result.TryAdd(key, info);
        }

        return result;
    }

    private static Dictionary<string, Student> BuildStudentMap(
        IEnumerable<Student> students,
        ISet<string> uniqueLegacyKeys)
    {
        Dictionary<string, Student> result = [];
        foreach (var student in students)
        {
            result[ProfileRecordIdentity.EnsureRecordId(student)] = student;
            foreach (var key in ProfileRecordIdentity.GetLegacyStudentHistoryKeys(student).Where(uniqueLegacyKeys.Contains))
                result.TryAdd(key, student);
        }

        return result;
    }

    private static HistoryDisplayRow BuildEventRow(StudentInfo info, HistoryItem item, int idPadWidth) =>
        new()
        {
            Id = FormatNumericId(string.IsNullOrWhiteSpace(item.RecordNumber) ? info.Id : item.RecordNumber, idPadWidth),
            Name = string.IsNullOrWhiteSpace(item.RecordName) ? info.Name : item.RecordName,
            Gender = string.IsNullOrWhiteSpace(item.RecordGender) ? info.Gender : item.RecordGender,
            Group = string.IsNullOrWhiteSpace(item.RecordGroup) ? info.Group : item.RecordGroup,
            DrawGender = FormatDrawGender(item.DrawGender),
            DrawGroup = FormatDrawGroup(item.DrawGroup),
            Subject = FormatSubject(item.CourseName),
            DrawTime = item.DrawTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            DrawMethod = item.DrawMethod == 0 ? SR.C_MethodRandom : SR.C_MethodWeight,
            DrawNumbers = item.DrawNumbers,
            Weight = item.DrawMethod == (int)DrawType.Fair
                ? FormatWeight(item.Weight)
                : string.Empty,
            SortTime = item.DrawTime
        };

    private static string FormatStudentName(Student student)
    {
        return string.IsNullOrWhiteSpace(student.Id) ? student.Name : $"{student.Id} {student.Name}";
    }

    private static int CalculateNumericPadWidth(IEnumerable<string> values)
    {
        return values
            .Where(value => int.TryParse(value, out _))
            .Select(value => value.Trim().Length)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static string FormatNumericId(string value, int width)
    {
        var trimmed = value.Trim();
        return width > 0 && int.TryParse(trimmed, out var number)
            ? number.ToString($"D{width}", CultureInfo.CurrentCulture)
            : trimmed;
    }

    private static string FormatWeight(double weight)
    {
        return weight.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private static string FormatDrawGender(string gender)
    {
        return string.IsNullOrWhiteSpace(gender) ? RollCallSR.O_AllGenders : gender;
    }

    private static string FormatDrawGroup(string group)
    {
        return string.IsNullOrWhiteSpace(group) ? RollCallSR.O_AllGroups : group;
    }

    private static string FormatSubject(string subject)
    {
        return subject == "__break__" ? SR.C_Break : subject;
    }

    private string FormatLatestWeight(ProfileHistory h) =>
        GetFilteredHistoryItems(h).LastOrDefault(item => item.DrawMethod == (int)DrawType.Fair) is { } last
            ? FormatWeight(last.Weight)
            : string.Empty;

    private static void SortByTimeDesc(ObservableCollection<HistoryDisplayRow> rows)
    {
        var sorted = rows.OrderByDescending(r => r.SortTime).ToList();
        rows.Clear();
        foreach (var row in sorted) rows.Add(row);
    }

    private sealed record StudentInfo(string Name, string Id, string Gender, string Group)
    {
        public static StudentInfo From(Student student) => new(student.Name, student.Id, student.Gender, student.Group);
        public static StudentInfo Unknown(string key) => new(key, string.Empty, string.Empty, string.Empty);
    }
}
