using System;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Helpers;
using SecRandom.Services.Draw;
using SecRandom.Services.Linkage;
using SecRandom.Services.Notification;
using SecRandom.Services.Security;
using SecRandom.Services.Verification;
using SecRandom.ViewModels;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;
using SR = SecRandom.Langs.MainPages.RollCall.Resources;

namespace SecRandom.ViewModels.MainPages;

public sealed partial class RollCallPageViewModel : ViewModelBase, IDisposable
{
    private static string AllGroupsOption => SR.O_AllGroups;
    private static string AllGendersOption => SR.O_AllGenders;

    private readonly DrawEngine _drawEngine;
    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly IVoiceAnnouncementService? _voiceAnnouncementService;
    private readonly DrawAudioService? _drawAudioService;
    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<RollCallPageViewModel> _logger;
    private readonly ISecurityService _securityService;
    private readonly LinkageDrawCoordinator _linkageDrawCoordinator;
    private readonly VerificationDrawCoordinator _verificationDrawCoordinator;
    private readonly NotificationService? _notificationService;
    private readonly FileSystemWatcher _studentListWatcher;
    private List<Student> _lastResultStudents = [];
    private int _studentIdPadWidth;
    private bool _isRefreshingLists;
    private bool _isStudentListRefreshQueued;
    private bool _isDrawCommandRunning;
    private CancellationTokenSource? _previewCts;

    [ObservableProperty] private string _selectedStudentListName = string.Empty;
    [ObservableProperty] private string _selectedGroup = AllGroupsOption;
    [ObservableProperty] private string _selectedGender = AllGendersOption;
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private string _statusText = SR.M_Ready;
    [ObservableProperty] private bool _isResultVisible;
    [ObservableProperty] private int _previewAnimationRevision;
    [ObservableProperty] private int _resultAnimationRevision;
    [ObservableProperty] private bool _isDrawing;

    public RollCallPageViewModel(
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        ILogger<RollCallPageViewModel> logger,
        ISecurityService securityService,
        LinkageDrawCoordinator linkageDrawCoordinator,
        VerificationDrawCoordinator verificationDrawCoordinator,
        IVoiceAnnouncementService? voiceAnnouncementService = null,
        DrawAudioService? drawAudioService = null,
        NotificationService? notificationService = null)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _logger = logger;
        _securityService = securityService;
        _linkageDrawCoordinator = linkageDrawCoordinator;
        _verificationDrawCoordinator = verificationDrawCoordinator;
        _voiceAnnouncementService = voiceAnnouncementService;
        _drawAudioService = drawAudioService;
        _notificationService = notificationService;

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
    public bool CanStartDraw => IsDrawing || (!_isDrawCommandRunning && TotalCount > 0 && RemainingCount > 0);
    public string DrawButtonText => IsDrawing ? SR.C_Stop : SR.C_Start;
    public int TotalCount { get; private set; }
    public int RemainingCount { get; private set; }
    public int MaximumDrawCount => Math.Max(1, Math.Max(TotalCount, RemainingCount));
    public string CountSummary => string.Format(SR.M_CountSummaryFormat, TotalCount, RemainingCount);
    public string ResultText { get; private set; }
    public IBrush ReminderBrush => BuildReminderBrush();
    public double ResultFontSize => DisplaySettings.FontSize;
    public double ReminderFontSize => ReminderSettings.ReminderFontSize;
    public FontFamily ResultFontFamily => BuildResultFontFamily();
    public bool IsResultCardStyle => DisplaySettings.DisplayStyle == DisplayStyleMode.Card;
    public bool AnimationEnabled => AnimationSettings.Animation != AnimationMode.NoAnimation;
    public DrawAnimationStyleMode AnimationStyle => AnimationSettings.AnimationStyle;
    public int AnimationDuration => 250;
    public int PreviewAnimationDuration => AnimationSettings.Animation == AnimationMode.AutoPlay
        ? Math.Clamp(AnimationSettings.AnimationInterval, 1, 10000)
        : 80;

    private DrawSettingsConfigBase DisplaySettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.Display);

    private DrawSettingsConfigBase AnimationSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.Animation);

    private DrawSettingsConfigBase ColorSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.Color);

    private DrawSettingsConfigBase StudentImageSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.StudentImage);

    private DrawSettingsConfigBase ReminderSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.Reminder);

    private DrawSettingsConfigBase MusicSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.Music);

    private DrawSettingsConfigBase VoiceAnnouncementSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.VoiceAnnouncement);

    private StudentList? CurrentStudentList => _profileService.CurrentStudentList;
    private StudentHistory? CurrentStudentHistory => _profileService.CurrentStudentHistory;
    private string CurrentGroupScope => SelectedGroup == AllGroupsOption ? string.Empty : SelectedGroup;
    private string CurrentGenderScope => SelectedGender == AllGendersOption ? string.Empty : SelectedGender;

    partial void OnSelectedStudentListNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _profileService.LoadStudentProfile(value);
            EnsureRestartTemporaryRecordsCleared(value);
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

    partial void OnIsDrawingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartDraw));
        OnPropertyChanged(nameof(DrawButtonText));
    }

    [RelayCommand]
    private void IncreaseCount()
    {
        DrawCount++;
    }

    public void IncreaseCountFromShortcut()
    {
        IncreaseCount();
    }

    [RelayCommand]
    private void DecreaseCount()
    {
        DrawCount--;
    }

    public void DecreaseCountFromShortcut()
    {
        DecreaseCount();
    }

    [RelayCommand]
    private async Task ResetDrawHistoryAsync()
    {
        if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.RollCallReset, () => Task.CompletedTask))
            return;
        ResetDrawHistoryCore();
    }

    private void ResetDrawHistoryCore()
    {
        _lastResultStudents.Clear();
        ResultItems.Clear();
        _temporaryRecordService.ClearStudentScope(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope);
        IsResultVisible = false;
        ResultText = ReminderSettings.ReminderText;
        StatusText = SR.M_ResetDone;
        RefreshCounts();
        OnPropertyChanged(nameof(ResultText));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task StartDrawAsync()
    {
        if (IsDrawing)
        {
            StopPreview();
            return;
        }

        if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.RollCallStart, () => Task.CompletedTask))
            return;
        await StartDrawCoreAsync();
    }

    private async Task StartDrawCoreAsync()
    {
        if (IsDrawing)
            return;

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
        SetDrawCommandRunning(true);
        try
        {
            if (_notificationService is not null)
                await _notificationService.BeginClassIslandAnimationAsync(
                    NotificationSettingsType.RollCall,
                    SelectedStudentListName,
                    candidates,
                    count);

            var courseName = _linkageDrawCoordinator.GetCourseName();
            var now = DateTime.Now;
            var verificationDrawTask = _verificationDrawCoordinator.DrawStudentsAsync(
                count,
                candidates,
                DrawSettingsType.RollCall,
                DrawProofExportContext.ForStudents(SelectedStudentListName, CurrentGroupScope, CurrentGenderScope, courseName),
                courseName: courseName,
                cancellationToken: default);
            var previewTask = ShowPreviewAsync(candidates, count, MusicSettings.AnimationMusic);
            List<Student> drawnStudents;
            try
            {
                var drawCompletedFirst = await Task.WhenAny(verificationDrawTask, previewTask).ConfigureAwait(true) == verificationDrawTask;
                drawnStudents = (await verificationDrawTask.ConfigureAwait(true)).Winners.ToList();
                if (drawCompletedFirst && !previewTask.IsCompleted)
                    await PlayAnimationMusicAsync(DrawMusicAttachedSettingsResolver.GetAnimationMusic(
                        drawnStudents.FirstOrDefault(), MusicSettings.AnimationMusic)).ConfigureAwait(true);

                await previewTask.ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "可验证点名抽取失败。");
                _previewCts?.Cancel();
                await (_drawAudioService?.StopAnimationMusicAsync(0, immediate: true) ?? Task.CompletedTask)
                    .ConfigureAwait(false);
                ClearUncommittedPreview();
                StatusText = SR.M_DrawFailed;
                return;
            }

            var weightSnapshot = BuildWeightSnapshot(drawnStudents, courseName);
            _profileService.RecordStudentHistory(
                drawnStudents,
                now,
                count,
                SelectedGroup == AllGroupsOption ? string.Empty : SelectedGroup,
                SelectedGender == AllGendersOption ? string.Empty : SelectedGender,
                (int)Config.RollCallSettings.DrawType,
                weightSnapshot,
                courseName);
            _temporaryRecordService.RecordStudents(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope, drawnStudents);

            ResultItems.Clear();
            _lastResultStudents = drawnStudents;
            RefreshResultItems();

            IsResultVisible = ResultItems.Count > 0;
            TriggerResultAnimation();
            await PlayResultMusicAsync(drawnStudents.FirstOrDefault()).ConfigureAwait(false);
            StatusText = string.Format(SR.M_DrawnCountFormat, ResultItems.Count);
            RefreshCounts();
            OnPropertyChanged(nameof(ResultText));

            if (_notificationService is not null)
                _notificationService.QueueStudents(
                    NotificationSettingsType.RollCall,
                    SelectedStudentListName,
                    drawnStudents);

            if (_voiceAnnouncementService is not null && VoiceAnnouncementSettings.VoiceAnnouncementEnabled)
                await _voiceAnnouncementService.SpeakStudentsAsync(drawnStudents).ConfigureAwait(false);
        }
        finally
        {
            SetDrawCommandRunning(false);
        }
    }

    public Task<bool> StartProtocolDrawAsync(bool protectLinkage = false) => _linkageDrawCoordinator.AuthorizeAsync(
        protectLinkage ? [SecurityOperation.RollCallStart, SecurityOperation.LinkageAction] : [SecurityOperation.RollCallStart],
        () =>
        {
            _ = StartDrawCoreAsync();
            return Task.CompletedTask;
        });

    public Task ToggleDrawFromShortcutAsync() => StartDrawAsync();

    public Task<bool> ResetProtocolDrawAsync(bool protectLinkage = false) => _linkageDrawCoordinator.AuthorizeAsync(
        protectLinkage ? [SecurityOperation.RollCallReset, SecurityOperation.LinkageAction] : [SecurityOperation.RollCallReset],
        () =>
        {
            ResetDrawHistoryCore();
            return Task.CompletedTask;
        });
    public void StopProtocolDraw() => StopPreview();

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

            UpdateStudentIdPadWidth();
        }
        finally
        {
            _isRefreshingLists = false;
        }
    }

    public void Dispose()
    {
        StopPreview();
        _ = _drawAudioService?.StopAnimationMusicAsync(0, immediate: true);
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

        if (IsResultVisible)
            RefreshResultItems();

        if (!IsResultVisible)
            ResultText = ReminderSettings.ReminderText;

        OnPropertyChanged(nameof(ResultFontSize));
        OnPropertyChanged(nameof(ReminderFontSize));
        OnPropertyChanged(nameof(ReminderBrush));
        OnPropertyChanged(nameof(ResultFontFamily));
        OnPropertyChanged(nameof(AnimationEnabled));
        OnPropertyChanged(nameof(AnimationStyle));
        OnPropertyChanged(nameof(AnimationDuration));
        OnPropertyChanged(nameof(ResultText));
        RefreshCounts();
    }

    private void TriggerResultAnimation()
    {
        ResultAnimationRevision++;
    }

    private void RefreshResultItems()
    {
        ResultItems.Clear();
        foreach (var student in _lastResultStudents.Select(CreateResultItem))
            ResultItems.Add(student);

        ResultText = string.Join(SR.M_ResultSeparator, ResultItems.Select(item => item.DisplayText));
    }

    private async Task ShowPreviewAsync(IReadOnlyList<Student> candidates, int count, string animationMusic)
    {
        if (AnimationSettings.Animation == AnimationMode.NoAnimation)
            return;

        await PlayAnimationMusicAsync(animationMusic).ConfigureAwait(true);

        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        var token = previewCts.Token;
        var isManualStop = AnimationSettings.Animation == AnimationMode.ManualStop;
        var iterations = isManualStop
            ? int.MaxValue
            : Math.Clamp(AnimationSettings.AutoplayCount, 1, 999);
        var delay = PreviewAnimationDuration;

        IsDrawing = true;
        try
        {
            for (var i = 0; i < iterations && !token.IsCancellationRequested; i++)
            {
                _lastResultStudents = candidates
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(count)
                    .ToList();
                RefreshResultItems();
                IsResultVisible = true;
                TriggerPreviewAnimation();
                StatusText = isManualStop ? "抽取中..." : SR.M_Ready;

                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCts, previewCts))
                _previewCts = null;

            previewCts.Dispose();
            IsDrawing = false;
        }
    }

    private void StopPreview()
    {
        _previewCts?.Cancel();
        _ = _drawAudioService?.StopAnimationMusicAsync(0, immediate: true);
    }

    private void ClearUncommittedPreview()
    {
        _lastResultStudents.Clear();
        ResultItems.Clear();
        IsResultVisible = false;
        ResultText = ReminderSettings.ReminderText;
        OnPropertyChanged(nameof(ResultText));
    }

    private void SetDrawCommandRunning(bool value)
    {
        if (_isDrawCommandRunning == value)
            return;

        _isDrawCommandRunning = value;
        OnPropertyChanged(nameof(CanStartDraw));
    }

    private void TriggerPreviewAnimation()
    {
        PreviewAnimationRevision++;
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
        UpdateStudentIdPadWidth();

        var candidates = GetCandidates().ToList();
        TotalCount = candidates.Count;
        RemainingCount = GetEligibleCandidates(candidates).Count();
        DrawCount = Math.Clamp(DrawCount, 1, Math.Max(1, TotalCount));
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
                       .OrderForList()
                       .Select(CreateRemainingItem))
            RemainingItems.Add(item);
    }

    private void EnsureRestartTemporaryRecordsCleared(string listName)
    {
        if (Config.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(listName);
    }

    private IEnumerable<Student> GetVisibleStudents()
    {
        return CurrentStudentList?.Students.Where(student => student.IsCandidate) ?? [];
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
        if (!student.IsCandidate)
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

        if (threshold <= 0)
            return false;

        var recordId = ProfileRecordIdentity.EnsureRecordId(student);
        var counts = _temporaryRecordService.GetStudentCounts(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope);
        return counts.GetValueOrDefault(recordId) >= threshold;
    }

    public void ResetForCourseLinkage()
    {
        ResetDrawHistoryCore();
    }

    private Dictionary<Student, double> BuildWeightSnapshot(IReadOnlyCollection<Student> drawnStudents, string courseName = "")
    {
        if (Config.RollCallSettings.DrawType != DrawType.Fair)
            return drawnStudents.ToDictionary(student => student, _ => 1.0);

        return _drawEngine.CalculateStudentWeight(GetVisibleStudents().ToList(), courseName: courseName)
            .Where(candidate => drawnStudents.Contains(candidate.Candidate))
            .ToDictionary(candidate => candidate.Candidate, candidate => candidate.Weight);
    }

    private RollCallResultItem CreateResultItem(Student student)
    {
        var weight = BuildDisplayWeight(student);
        var image = BuildImage(student);
        var accentBrush = ResolveAccentBrush();
        return new RollCallResultItem(
            FormatStudent(student),
            student.Name,
            student.Id,
            student.Group,
            student.Gender,
            student.Tags,
            DisplaySettings.ShowTags && !string.IsNullOrWhiteSpace(student.Tags),
            IsResultCardStyle,
            accentBrush,
            DrawColorHelper.ResolveTextBrush(accentBrush, Config.Appearance.Theme),
            BuildResultOpacity(weight),
            DisplaySettings.ShowWeightTransparency,
            $"权重 {weight:0.##}",
            image,
            StudentImageSettings.StudentImage,
            BuildInitial(student));
    }

    private RollCallRemainingItem CreateRemainingItem(Student student)
    {
        return new RollCallRemainingItem(
            FormatStudent(student),
            student.Id,
            student.Name,
            student.Group,
            student.Gender,
            student.Tags);
    }

    private string FormatStudent(Student student)
    {
        var id = FormatNumericId(student.Id, _studentIdPadWidth);
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

    private double BuildDisplayWeight(Student student)
    {
        if (Config.RollCallSettings.DrawType != DrawType.Fair)
            return 1;

        return _drawEngine.CalculateStudentWeight(GetVisibleStudents().ToList(), courseName: _linkageDrawCoordinator.GetCourseName())
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Candidate, student))
            ?.Weight ?? 1;
    }

    private double BuildResultOpacity(double weight)
    {
        if (!DisplaySettings.ShowWeightTransparency || Config.RollCallSettings.DrawType != DrawType.Fair)
            return 1;

        return Math.Clamp(0.42 + Math.Min(weight, 3) / 3 * 0.58, 0.42, 1);
    }

    private IBrush? ResolveAccentBrush()
    {
        return DrawColorHelper.ResolveAccentBrush(
            ColorSettings.AnimationColorTheme,
            ColorSettings.AnimationFixedColor,
            Config.Appearance.Theme);
    }

    private static string BuildInitial(Student student)
    {
        var text = string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name;
        return string.IsNullOrWhiteSpace(text) ? "?" : text.Trim()[0].ToString();
    }

    private void UpdateStudentIdPadWidth()
    {
        _studentIdPadWidth = CalculateNumericPadWidth(CurrentStudentList?.Students.Select(student => student.Id) ?? []);
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

    private static Bitmap? BuildImage(Student student)
    {
        var settings = student.GetAttachedObject<DrawImageAttachedSettings>(
            Guid.Parse(GlobalConstants.DrawImageAttachedSettings));
        if (settings is not { IsAttachSettingsEnabled: true } || string.IsNullOrWhiteSpace(settings.ImagePath))
            return null;

        try
        {
            return File.Exists(settings.ImagePath) ? new Bitmap(settings.ImagePath) : null;
        }
        catch
        {
            return null;
        }
    }

    private Task PlayAnimationMusicAsync(string animationMusic)
    {
        return _drawAudioService?.StartAnimationMusicAsync(
            animationMusic,
            MusicSettings.AnimationMusicVolume,
            MusicSettings.AnimationMusicFadeIn,
            MusicSettings.AnimationMusicLoop) ?? Task.CompletedTask;
    }

    private async Task PlayResultMusicAsync(Student? musicTarget)
    {
        if (_drawAudioService is null)
            return;

        await _drawAudioService.TransitionToResultMusicAsync(
            DrawMusicAttachedSettingsResolver.GetResultMusic(musicTarget, MusicSettings.ResultMusic),
            MusicSettings.ResultMusicVolume,
            MusicSettings.ResultMusicFadeIn,
            MusicSettings.ResultMusicFadeOut,
            MusicSettings.AnimationMusicFadeOut).ConfigureAwait(false);
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

}

public sealed record RollCallResultItem(
    string DisplayText,
    string Name,
    string Id,
    string Group,
    string Gender,
    string Tags,
    bool IsTagsVisible,
    bool IsCardStyle,
    IBrush? AccentBrush,
    IBrush TextBrush,
    double Opacity,
    bool IsWeightVisible,
    string WeightText,
    Bitmap? Image,
    bool IsImageEnabled,
    string Initial)
{
    public bool IsImageVisible => IsImageEnabled && Image is not null;
    public bool IsPlaceholderVisible => IsImageEnabled && Image is null;
}

public sealed record RollCallRemainingItem(
    string DisplayText,
    string Id,
    string Name,
    string Group,
    string Gender,
    string Tags)
{
    public bool IsTagsVisible => !string.IsNullOrWhiteSpace(Tags);
}
