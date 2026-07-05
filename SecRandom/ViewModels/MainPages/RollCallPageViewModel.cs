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
        IVoiceAnnouncementService? voiceAnnouncementService = null,
        DrawAudioService? drawAudioService = null)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _logger = logger;
        _voiceAnnouncementService = voiceAnnouncementService;
        _drawAudioService = drawAudioService;

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
    public bool AnimationEnabled => AnimationSettings.AnimationEnabled;
    public DrawAnimationStyleMode AnimationStyle => AnimationSettings.AnimationStyle;
    public int AnimationDuration => Math.Clamp(AnimationSettings.AnimationDuration, 80, 10000);
    public int PreviewAnimationDuration => Math.Clamp(AnimationSettings.AnimationInterval, 1, 10000);

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

    private StudentList? CurrentStudentList => _profileService.CurrentStudentList;
    private StudentHistory? CurrentStudentHistory => _profileService.CurrentStudentHistory;
    private string CurrentGroupScope => SelectedGroup == AllGroupsOption ? string.Empty : SelectedGroup;
    private string CurrentGenderScope => SelectedGender == AllGendersOption ? string.Empty : SelectedGender;

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

    [RelayCommand]
    private void DecreaseCount()
    {
        DrawCount--;
    }

    [RelayCommand]
    private void ResetDrawHistory()
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
            await ShowPreviewAsync(candidates, count).ConfigureAwait(true);

            var result = _drawEngine.DrawPreparedStudents(count, candidates, DrawSettingsType.RollCall);
            if (!result.IsSuccess)
            {
                StatusText = ToStatusMessage(result.Status);
                return;
            }

            var now = DateTime.Now;
            var drawnStudents = result.Result.ToList();
            var weightSnapshot = BuildWeightSnapshot(drawnStudents);
            _profileService.RecordStudentHistory(
                drawnStudents,
                now,
                count,
                SelectedGroup == AllGroupsOption ? string.Empty : SelectedGroup,
                SelectedGender == AllGendersOption ? string.Empty : SelectedGender,
                (int)Config.RollCallSettings.DrawType,
                weightSnapshot);
            _temporaryRecordService.RecordStudents(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope, drawnStudents);

            ResultItems.Clear();
            _lastResultStudents = drawnStudents;
            RefreshResultItems();

            IsResultVisible = ResultItems.Count > 0;
            TriggerResultAnimation();
            await PlayResultMusicAsync().ConfigureAwait(false);
            StatusText = string.Format(SR.M_DrawnCountFormat, ResultItems.Count);
            RefreshCounts();
            OnPropertyChanged(nameof(ResultText));

            if (_voiceAnnouncementService is not null)
                await _voiceAnnouncementService.SpeakStudentsAsync(result.Result).ConfigureAwait(false);
        }
        finally
        {
            SetDrawCommandRunning(false);
        }
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

    private async Task ShowPreviewAsync(IReadOnlyList<Student> candidates, int count)
    {
        if (AnimationSettings.Animation == AnimationMode.NoAnimation)
            return;

        await PlayAnimationMusicAsync().ConfigureAwait(true);

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

        if (threshold <= 0)
            return false;

        var recordId = ProfileRecordIdentity.EnsureRecordId(student);
        var counts = _temporaryRecordService.GetStudentCounts(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope);
        return counts.GetValueOrDefault(recordId) >= threshold;
    }

    private Dictionary<Student, double> BuildWeightSnapshot(IReadOnlyCollection<Student> drawnStudents)
    {
        if (Config.RollCallSettings.DrawType != DrawType.Fair)
            return drawnStudents.ToDictionary(student => student, _ => 1.0);

        return _drawEngine.CalculateStudentWeight(GetVisibleStudents().ToList())
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

        return _drawEngine.CalculateStudentWeight(GetVisibleStudents().ToList())
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

    private Task PlayAnimationMusicAsync()
    {
        return _drawAudioService?.PlayAsync(
            MusicSettings.AnimationMusic,
            MusicSettings.AnimationMusicVolume,
            MusicSettings.AnimationMusicFadeIn,
            MusicSettings.AnimationMusicFadeOut) ?? Task.CompletedTask;
    }

    private Task PlayResultMusicAsync()
    {
        return _drawAudioService?.PlayAsync(
            MusicSettings.ResultMusic,
            MusicSettings.ResultMusicVolume,
            MusicSettings.ResultMusicFadeIn,
            MusicSettings.ResultMusicFadeOut) ?? Task.CompletedTask;
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
    string Group,
    string Gender,
    string Tags);
