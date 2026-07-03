using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SR = SecRandom.Langs.MainPages.RollCall.Resources;

namespace SecRandom.ViewModels;

public sealed partial class RollCallPageViewModel : ViewModelBase, IDisposable
{
    private static string AllGroupsOption => SR.O_AllGroups;
    private static string AllGendersOption => SR.O_AllGenders;

    private readonly DrawEngine _drawEngine;
    private readonly IProfileService _profileService;
    private readonly IVoiceAnnouncementService? _voiceAnnouncementService;
    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<RollCallPageViewModel> _logger;
    private readonly FileSystemWatcher _studentListWatcher;
    private bool _isRefreshingLists;
    private bool _isStudentListRefreshQueued;

    [ObservableProperty] private string _selectedStudentListName = string.Empty;
    [ObservableProperty] private string _selectedGroup = AllGroupsOption;
    [ObservableProperty] private string _selectedGender = AllGendersOption;
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private string _statusText = SR.M_Ready;
    [ObservableProperty] private bool _isResultVisible;

    public RollCallPageViewModel(
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        IProfileService profileService,
        ILogger<RollCallPageViewModel> logger,
        IVoiceAnnouncementService? voiceAnnouncementService = null)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _profileService = profileService;
        _logger = logger;
        _voiceAnnouncementService = voiceAnnouncementService;

        ResultText = ReminderSettings.ReminderText;
        _studentListWatcher = CreateStudentListWatcher();

        StudentListNames.CollectionChanged += StudentListNamesOnCollectionChanged;
        ResultItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ResultText));

        Config.RollCallSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.DefaultDrawSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.MoreSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.Appearance.PropertyChanged += SettingsOnPropertyChanged;

        RefreshLists();
        RefreshFilterOptions();
        RefreshCounts();
    }

    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<string> GroupOptions { get; } = [AllGroupsOption];
    public ObservableCollection<string> GenderOptions { get; } = [AllGendersOption];
    public ObservableCollection<RollCallResultItem> ResultItems { get; } = [];
    public ObservableCollection<RollCallRemainingItem> RemainingItems { get; } = [];

    public MoreSettingsConfig MoreSettings => Config.MoreSettings;
    public bool IsControlPanelOnLeft => MoreSettings.RollCallControlPanelPosition == RollCallControlPanelPosition.Left;
    public bool IsControlPanelOnRight => !IsControlPanelOnLeft;
    public bool CanDecreaseCount => DrawCount > 1;
    public bool CanIncreaseCount => DrawCount < MaximumDrawCount;
    public bool CanStartDraw => TotalCount > 0 && RemainingCount > 0;
    public int TotalCount { get; private set; }
    public int RemainingCount { get; private set; }
    public int MaximumDrawCount => Math.Max(1, Math.Max(TotalCount, RemainingCount));
    public string CountSummary => string.Format(SR.M_CountSummaryFormat, TotalCount, RemainingCount);
    public string ResultText { get; private set; }
    public IBrush ReminderBrush => BuildReminderBrush();
    public double ResultFontSize => DisplaySettings.FontSize;
    public double ReminderFontSize => ReminderSettings.ReminderFontSize;
    public FontFamily ResultFontFamily => BuildResultFontFamily();

    private DrawSettingsConfigBase DisplaySettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.Display);

    private DrawSettingsConfigBase ReminderSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.Reminder);

    private StudentList? CurrentStudentList => _profileService.CurrentStudentList;
    private StudentHistory? CurrentStudentHistory => _profileService.CurrentStudentHistory;

    partial void OnSelectedStudentListNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _profileService.LoadStudentProfile(value);
            if (Config.RollCallSettings.DefaultClass != value)
            {
                Config.RollCallSettings.DefaultClass = value;
                _configHandler.Save();
            }
        }

        RefreshFilterOptions();
        RefreshCounts();
    }

    partial void OnSelectedGroupChanged(string value)
    {
        RefreshCounts();
    }

    partial void OnSelectedGenderChanged(string value)
    {
        RefreshCounts();
    }

    partial void OnDrawCountChanged(int value)
    {
        var normalized = Math.Clamp(value, 1, MaximumDrawCount);
        if (normalized != value)
        {
            DrawCount = normalized;
            return;
        }

        OnPropertyChanged(nameof(CanDecreaseCount));
        OnPropertyChanged(nameof(CanIncreaseCount));
    }

    [RelayCommand]
    private void IncreaseCount()
    {
        DrawCount++;
    }

    [RelayCommand]
    private void DecreaseCount()
    {
        DrawCount--;
    }

    [RelayCommand]
    private void ResetDrawHistory()
    {
        ClearDrawHistory();
        ResultItems.Clear();
        IsResultVisible = false;
        ResultText = ReminderSettings.ReminderText;
        StatusText = SR.M_ResetDone;
        RefreshCounts();
        OnPropertyChanged(nameof(ResultText));
    }

    [RelayCommand]
    private async Task StartDrawAsync()
    {
        RefreshCounts();
        if (!CanStartDraw)
        {
            StatusText = TotalCount == 0 ? SR.M_NoStudents : SR.M_NoRemainingStudents;
            return;
        }

        var candidates = GetEligibleCandidates().ToList();
        if (candidates.Count == 0)
        {
            StatusText = SR.M_NoRemainingStudents;
            return;
        }

        var count = Math.Clamp(DrawCount, 1, candidates.Count);
        var result = _drawEngine.DrawStudent(count, candidates);
        if (!result.IsSuccess)
        {
            StatusText = ToStatusMessage(result.Status);
            return;
        }

        var now = DateTime.Now;
        RecordHistory(result.Result, now, count);
        _profileService.SaveProfile();

        ResultItems.Clear();
        foreach (var student in result.Result.Select(CreateResultItem))
            ResultItems.Add(student);

        IsResultVisible = ResultItems.Count > 0;
        ResultText = string.Join(SR.M_ResultSeparator, ResultItems.Select(item => item.DisplayText));
        StatusText = string.Format(SR.M_DrawnCountFormat, ResultItems.Count);
        RefreshCounts();
        OnPropertyChanged(nameof(ResultText));

        if (_voiceAnnouncementService is not null)
            await _voiceAnnouncementService.SpeakStudentsAsync(result.Result).ConfigureAwait(false);
    }

    [RelayCommand]
    private void OpenRollCallSettings()
    {
        App.ShowSettingsWindow();
        Views.SettingsView.Current?.SelectNavigationItemById("settings.picking.rollCall");
    }

    [RelayCommand]
    private void OpenListSettings()
    {
        App.ShowSettingsWindow();
        Views.SettingsView.Current?.SelectNavigationItemById("settings.listManagement.rollCallList");
    }

    public void RefreshLists()
    {
        if (_isRefreshingLists)
            return;

        _isRefreshingLists = true;
        try
        {
            var previousName = SelectedStudentListName;
            StudentListNames.Clear();

            var directory = Utils.GetDirectoryPath("list", "roll_call_list");
            foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(Path.GetFileName))
                StudentListNames.Add(Path.GetFileNameWithoutExtension(file));

            if (StudentListNames.Count == 0)
            {
                var config = new StudentListConfig("default");
                config.Save();
                StudentListNames.Add(config.Name);
            }

            var defaultClass = Config.RollCallSettings.DefaultClass;
            var currentName = _profileService.StudentListConfig?.Name ?? string.Empty;
            SelectedStudentListName = StudentListNames.Contains(previousName)
                ? previousName
                : StudentListNames.Contains(defaultClass)
                    ? defaultClass
                    : StudentListNames.Contains(currentName)
                        ? currentName
                        : StudentListNames[0];
        }
        finally
        {
            _isRefreshingLists = false;
        }
    }

    public void Dispose()
    {
        StudentListNames.CollectionChanged -= StudentListNamesOnCollectionChanged;
        Config.RollCallSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.DefaultDrawSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.MoreSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.Appearance.PropertyChanged -= SettingsOnPropertyChanged;
        _studentListWatcher.Dispose();
    }

    private void StudentListNamesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StudentListNames));
    }

    private FileSystemWatcher CreateStudentListWatcher()
    {
        var watcher = new FileSystemWatcher(Utils.GetDirectoryPath("list", "roll_call_list"), "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        watcher.Created += StudentListFiles_OnChanged;
        watcher.Deleted += StudentListFiles_OnChanged;
        watcher.Renamed += StudentListFiles_OnChanged;
        watcher.Changed += StudentListFiles_OnChanged;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void StudentListFiles_OnChanged(object? sender, FileSystemEventArgs e)
    {
        QueueStudentListRefresh();
    }

    private void QueueStudentListRefresh()
    {
        if (_isStudentListRefreshQueued)
            return;

        _isStudentListRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isStudentListRefreshQueued = false;
            RefreshLists();
            RefreshFilterOptions();
            RefreshCounts();
        });
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Config.MoreSettings)
        {
            OnPropertyChanged(nameof(IsControlPanelOnLeft));
            OnPropertyChanged(nameof(IsControlPanelOnRight));
        }

        if (!IsResultVisible)
            ResultText = ReminderSettings.ReminderText;

        OnPropertyChanged(nameof(ResultFontSize));
        OnPropertyChanged(nameof(ReminderFontSize));
        OnPropertyChanged(nameof(ReminderBrush));
        OnPropertyChanged(nameof(ResultFontFamily));
        OnPropertyChanged(nameof(ResultText));
        RefreshCounts();
    }

    private void RefreshFilterOptions()
    {
        var students = GetVisibleStudents().ToList();
        ReplaceOptions(GroupOptions, AllGroupsOption, students.Select(s => s.Group));
        ReplaceOptions(GenderOptions, AllGendersOption, students.Select(s => s.Gender));

        if (!GroupOptions.Contains(SelectedGroup))
            SelectedGroup = AllGroupsOption;
        if (!GenderOptions.Contains(SelectedGender))
            SelectedGender = AllGendersOption;
    }

    private void RefreshCounts()
    {
        var candidates = GetCandidates().ToList();
        TotalCount = candidates.Count;
        RemainingCount = GetEligibleCandidates(candidates).Count();
        DrawCount = Math.Clamp(DrawCount, 1, Math.Max(1, RemainingCount));
        RefreshRemainingList(candidates);

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(CountSummary));
        OnPropertyChanged(nameof(MaximumDrawCount));
        OnPropertyChanged(nameof(CanDecreaseCount));
        OnPropertyChanged(nameof(CanIncreaseCount));
        OnPropertyChanged(nameof(CanStartDraw));
    }

    public void RefreshRemainingList()
    {
        RefreshRemainingList(null);
    }

    private void RefreshRemainingList(IEnumerable<Student>? candidates)
    {
        var source = candidates?.ToList() ?? GetCandidates().ToList();
        RemainingItems.Clear();
        foreach (var item in source
                     .Where(student => !HasReachedRepeatLimit(student))
                     .OrderBy(student => student.Group)
                     .ThenBy(student => student.Gender)
                     .ThenBy(student => student.Name)
                     .Select(CreateRemainingItem))
            RemainingItems.Add(item);
    }

    private IEnumerable<Student> GetVisibleStudents()
    {
        return CurrentStudentList?.Students.Where(student => student.Exists) ?? [];
    }

    private IEnumerable<Student> GetCandidates()
    {
        return GetVisibleStudents().Where(MatchesSelection);
    }

    private IEnumerable<Student> GetEligibleCandidates(IEnumerable<Student>? candidates = null)
    {
        var source = candidates ?? GetCandidates();
        return source.Where(student => !HasReachedRepeatLimit(student));
    }

    private bool MatchesSelection(Student student)
    {
        if (!student.Exists)
            return false;

        if (SelectedGroup != AllGroupsOption && student.Group != SelectedGroup)
            return false;

        if (SelectedGender != AllGendersOption && student.Gender != SelectedGender)
            return false;

        return true;
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

        if (threshold <= 0 || CurrentStudentHistory is null)
            return false;

        var history = ProfileRecordIdentity.GetStudentHistory(CurrentStudentHistory, student, _ => true);
        return (history?.TotalCount ?? 0) >= threshold;
    }

    private void RecordHistory(IReadOnlyList<Student> students, DateTime now, int requestedCount)
    {
        var history = CurrentStudentHistory;
        if (history is null)
            return;

        history.TotalRounds++;
        history.TotalStats += students.Count;

        foreach (var student in students)
        {
            var key = ProfileRecordIdentity.EnsureRecordId(student);
            if (!history.Students.TryGetValue(key, out var item))
            {
                item = new History();
                history.Students[key] = item;
            }

            item.TotalCount++;
            item.LastDrawnTime = now;
            item.RoundsMissed = 0;
            item.Histories.Add(new HistoryItem
            {
                DrawTime = now,
                DrawNumbers = requestedCount,
                DrawGroup = SelectedGroup == AllGroupsOption ? string.Empty : SelectedGroup,
                DrawGender = SelectedGender == AllGendersOption ? string.Empty : SelectedGender,
                DrawMethod = (int)Config.RollCallSettings.DrawType,
                Weight = 1
            });

            if (!string.IsNullOrWhiteSpace(student.Group))
                history.GroupStats[student.Group] = history.GroupStats.GetValueOrDefault(student.Group) + 1;
            if (!string.IsNullOrWhiteSpace(student.Gender))
                history.GenderStatus[student.Gender] = history.GenderStatus.GetValueOrDefault(student.Gender) + 1;
        }

        foreach (var student in GetVisibleStudents().Except(students))
        {
            var existing = ProfileRecordIdentity.GetStudentHistory(history, student, _ => true);
            if (existing is not null)
                existing.RoundsMissed++;
        }
    }

    private RollCallResultItem CreateResultItem(Student student)
    {
        return new RollCallResultItem(
            FormatStudent(student),
            student.Name,
            student.Id,
            student.Group,
            student.Gender,
            student.Tags);
    }

    private RollCallRemainingItem CreateRemainingItem(Student student)
    {
        return new RollCallRemainingItem(
            FormatStudent(student),
            student.Group,
            student.Gender,
            student.Tags);
    }

    private string FormatStudent(Student student)
    {
        var id = student.Id.Trim();
        var name = student.Name.Trim();
        return DisplaySettings.DisplayFormat switch
        {
            DisplayFormatMode.Id => string.IsNullOrWhiteSpace(id) ? name : id,
            DisplayFormatMode.Name => string.IsNullOrWhiteSpace(name) ? id : name,
            _ => string.IsNullOrWhiteSpace(id) ? name : $"{id} {name}".Trim()
        };
    }

    private IBrush BuildReminderBrush()
    {
        var color = ReminderSettings.ReminderTextColor;
        color = Color.FromArgb((byte)Math.Clamp(ReminderSettings.ReminderTextOpacity * 255 / 100, 0, 255),
            color.R, color.G, color.B);
        return new SolidColorBrush(color);
    }

    private FontFamily BuildResultFontFamily()
    {
        var font = DisplaySettings.UseGlobalFont == UseGlobalFontMode.Custom
            ? DisplaySettings.CustomFont
            : Config.Appearance.Font;

        if (string.Equals(font, "MiSans", StringComparison.OrdinalIgnoreCase))
            font = "avares://SecRandom/Assets/Fonts/MiSans/#MiSans";

        return new FontFamily(font);
    }

    private static void ReplaceOptions(ObservableCollection<string> target, string firstOption, IEnumerable<string> values)
    {
        target.Clear();
        target.Add(firstOption);
        foreach (var value in values
                     .Select(value => value.Trim())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.CurrentCulture)
                     .OrderBy(value => value, StringComparer.CurrentCulture))
            target.Add(value);
    }

    private string ToStatusMessage(DrawStatus status)
    {
        _logger.LogWarning("Roll-call draw failed with status {Status}.", status);
        return status switch
        {
            DrawStatus.NoCandidates => SR.M_NoCandidates,
            DrawStatus.NoEligibleCandidates => SR.M_NoEligibleCandidates,
            DrawStatus.RepeatLimitExhausted => SR.M_RepeatLimitExhausted,
            DrawStatus.InvalidWeight => SR.M_InvalidWeight,
            _ => SR.M_DrawFailed
        };
    }

    private void ClearDrawHistory()
    {
        if (CurrentStudentHistory is null)
            return;

        CurrentStudentHistory.Students.Clear();
        CurrentStudentHistory.GroupStats.Clear();
        CurrentStudentHistory.GenderStatus.Clear();
        CurrentStudentHistory.TotalRounds = 0;
        CurrentStudentHistory.TotalStats = 0;
        _profileService.SaveProfile();
    }
}

public sealed record RollCallResultItem(
    string DisplayText,
    string Name,
    string Id,
    string Group,
    string Gender,
    string Tags);

public sealed record RollCallRemainingItem(
    string DisplayText,
    string Group,
    string Gender,
    string Tags);
