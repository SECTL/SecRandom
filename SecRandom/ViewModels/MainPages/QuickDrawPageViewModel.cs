using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Helpers;
using QuickDrawResources = SecRandom.Langs.MainPages.QuickDraw.Resources;
using SecRandom.Services.Draw;
using SecRandom.Services.Linkage;
using SecRandom.Services.Notification;
using SecRandom.Services.Security;
using SecRandom.Services.Verification;
using SecRandom.ViewModels;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.ViewModels.MainPages;

public sealed partial class QuickDrawPageViewModel : ViewModelBase, IDisposable
{
    private readonly DrawEngine _drawEngine;
    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly IDrawCommitService _drawCommitService;
    private readonly MainConfigHandler _configHandler;
    private readonly DrawAudioService _drawAudioService;
    private readonly ILogger<QuickDrawPageViewModel> _logger;
    private readonly ISecurityService _securityService;
    private readonly LinkageDrawCoordinator _linkageDrawCoordinator;
    private readonly VerificationDrawCoordinator _verificationDrawCoordinator;
    private readonly IVoiceAnnouncementService? _voiceAnnouncementService;
    private readonly NotificationService? _notificationService;
    private bool _isDrawCommandRunning;
    private bool _isCoolingDown;
    private CancellationTokenSource? _previewCts;
    private int? _notificationAutoCloseTime;

    [ObservableProperty] private string _selectedStudentListName = string.Empty;
    [ObservableProperty] private string _statusText = QuickDrawResources.M_Ready;
    [ObservableProperty] private bool _isResultVisible;
    [ObservableProperty] private int _previewAnimationRevision;
    [ObservableProperty] private int _resultAnimationRevision;
    [ObservableProperty] private int _notificationDisplayRevision;
    [ObservableProperty] private bool _isDrawing;
    [ObservableProperty] private Student? _lastDrawnStudent;

    public QuickDrawPageViewModel(
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        IDrawCommitService drawCommitService,
        DrawAudioService drawAudioService,
        ILogger<QuickDrawPageViewModel> logger,
        ISecurityService securityService,
        LinkageDrawCoordinator linkageDrawCoordinator,
        VerificationDrawCoordinator verificationDrawCoordinator,
        IVoiceAnnouncementService? voiceAnnouncementService = null,
        NotificationService? notificationService = null)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _drawCommitService = drawCommitService;
        _drawAudioService = drawAudioService;
        _logger = logger;
        _securityService = securityService;
        _linkageDrawCoordinator = linkageDrawCoordinator;
        _verificationDrawCoordinator = verificationDrawCoordinator;
        _voiceAnnouncementService = voiceAnnouncementService;
        _notificationService = notificationService;
        ResultItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ResultFontSize));
        RefreshStudentLists();
    }

    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<QuickDrawResultItem> ResultItems { get; } = [];
    public bool CanStartDraw => IsDrawing || (!_isDrawCommandRunning && !_isCoolingDown && GetEligibleCandidates().Any());
    public string DrawButtonText => IsDrawing ? QuickDrawResources.C_Stop : QuickDrawResources.C_Start;
    public double ResultFontSize
    {
        get
        {
            var scale = ResultItems.Count switch
            {
                <= 1 => 1.24,
                2 => 1.18,
                3 => 1.12,
                4 => 1.06,
                _ => 1
            };
            return Math.Clamp(DisplaySettings.FontSize * scale, 1, 300);
        }
    }
    public FontFamily ResultFontFamily => BuildResultFontFamily();
    public bool AnimationEnabled => AnimationSettings.Animation != AnimationMode.NoAnimation;
    public DrawAnimationStyleMode AnimationStyle => AnimationSettings.AnimationStyle;
    public int AnimationDuration => 250;
    public int PreviewAnimationDuration => AnimationSettings.Animation == AnimationMode.AutoPlay
        ? Math.Clamp(AnimationSettings.AnimationInterval, 1, 10000)
        : 80;
    public int ResultAutoCloseTime => _notificationAutoCloseTime ?? 0;

    private DrawSettingsConfigBase DisplaySettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.QuickDraw, OverridableDrawSettingsType.Display);

    private DrawSettingsConfigBase AnimationSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.QuickDraw, OverridableDrawSettingsType.Animation);

    private DrawSettingsConfigBase ColorSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.QuickDraw, OverridableDrawSettingsType.Color);

    private DrawSettingsConfigBase StudentImageSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.QuickDraw, OverridableDrawSettingsType.StudentImage);

    private DrawSettingsConfigBase MusicSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.QuickDraw, OverridableDrawSettingsType.Music);

    private DrawSettingsConfigBase VoiceAnnouncementSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.QuickDraw, OverridableDrawSettingsType.VoiceAnnouncement);

    partial void OnSelectedStudentListNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _profileService.LoadStudentProfile(value);
        EnsureRestartTemporaryRecordsCleared(value);
        OnPropertyChanged(nameof(CanStartDraw));
    }

    partial void OnIsDrawingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartDraw));
        OnPropertyChanged(nameof(DrawButtonText));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task StartDrawAsync()
    {
        if (IsDrawing)
        {
            StopPreview();
            return;
        }

        if (_isCoolingDown || _isDrawCommandRunning)
            return;

        if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.QuickDrawStart, () => Task.CompletedTask))
            return;
        await StartAuthorizedTriggeredDrawAsync();
    }

    public Task<bool> AuthorizeTriggeredDrawAsync() => _linkageDrawCoordinator.AuthorizeAsync(
        SecurityOperation.QuickDrawStart,
        () => Task.CompletedTask);

    public Task StartAuthorizedTriggeredDrawAsync()
    {
        var showBuiltInNotificationAnimation = _notificationService?.UsesBuiltInNotificationService(
            NotificationSettingsType.QuickDraw) == true;
        return StartDrawCoreAsync(
            skipPreview: !showBuiltInNotificationAnimation,
            showBuiltInNotificationAnimation: showBuiltInNotificationAnimation);
    }

    private async Task StartDrawCoreAsync(
        bool skipPreview = false,
        bool showBuiltInNotificationAnimation = false)
    {
        if (IsDrawing || _isDrawCommandRunning)
            return;

        if (_isCoolingDown)
            return;

        ClearNotificationPresentation();
        if (!TryLoadDefaultStudentList())
            return;

        ResetForNewRoundIfExhausted();

        var candidates = GetEligibleCandidates().ToList();
        if (candidates.Count == 0)
        {
            StatusText = QuickDrawResources.M_NoMembers;
            return;
        }

        const int count = 1;
        ResultItems.Clear();
        IsResultVisible = false;
        StatusText = QuickDrawResources.M_Drawing;
        _notificationAutoCloseTime = null;
        SetDrawCommandRunning(true);
        try
        {
            if (showBuiltInNotificationAnimation)
            {
                showBuiltInNotificationAnimation = await (_notificationService?.BeginBuiltInNotificationPresentationAsync(
                    NotificationSettingsType.QuickDraw) ?? Task.FromResult(false));
            }

            if (_notificationService is not null)
                await _notificationService.BeginClassIslandAnimationAsync(
                    NotificationSettingsType.QuickDraw,
                    SelectedStudentListName,
                    candidates,
                    count);

            skipPreview = !showBuiltInNotificationAnimation;
            var courseName = _linkageDrawCoordinator.GetCourseName();
            var verificationDrawTask = _verificationDrawCoordinator.DrawStudentsAsync(
                count,
                candidates,
                DrawSettingsType.QuickDraw,
                DrawProofExportContext.ForStudents(SelectedStudentListName, courseName: courseName),
                courseName: courseName,
                cancellationToken: default);
            var previewTask = skipPreview
                ? Task.CompletedTask
                : ShowPreviewAsync(candidates, count, MusicSettings.AnimationMusic);
            List<Student> drawn;
            VerificationDrawOutcome<Student> drawOutcome;
            try
            {
                var drawCompletedFirst = await Task.WhenAny(verificationDrawTask, previewTask).ConfigureAwait(true) == verificationDrawTask;
                drawOutcome = await verificationDrawTask.ConfigureAwait(true);
                drawn = drawOutcome.Winners.ToList();
                if (drawCompletedFirst && !previewTask.IsCompleted)
                    await _drawAudioService.StartAnimationMusicAsync(
                        DrawMusicAttachedSettingsResolver.GetAnimationMusic(drawn.FirstOrDefault(), MusicSettings.AnimationMusic),
                        MusicSettings.AnimationMusicVolume,
                        MusicSettings.AnimationMusicFadeIn,
                        MusicSettings.AnimationMusicLoop).ConfigureAwait(true);

                await previewTask.ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "可验证闪抽失败。");
                StopPreview();
                if (showBuiltInNotificationAnimation)
                    _notificationService?.CancelBuiltInNotificationPresentation(NotificationSettingsType.QuickDraw);
                ResultItems.Clear();
                LastDrawnStudent = null;
                IsResultVisible = false;
                StatusText = QuickDrawResources.M_DrawFailed;
                return;
            }

            var weights = BuildWeightSnapshot(drawn, drawOutcome.FrozenWeights);
            _drawCommitService.CommitStudentDraw(new StudentDrawCommit(
                drawn,
                DateTime.Now,
                count,
                SelectedStudentListName,
                DrawMethod: (int)Config.QuickDrawSettings.DrawType,
                Weights: weights,
                CourseName: courseName));
            LastDrawnStudent = drawn[0];
            _notificationAutoCloseTime = showBuiltInNotificationAnimation
                ? ResolveNotificationAutoCloseTime()
                : null;
            ReplaceResults(drawn);
            IsResultVisible = true;
            StatusText = string.Format(QuickDrawResources.M_DrawnCountFormat, ResultItems.Count);
            if (_notificationService is not null)
                _notificationService.QueueStudents(
                    NotificationSettingsType.QuickDraw,
                    SelectedStudentListName,
                    drawn);
            await _drawAudioService.TransitionToResultMusicAsync(
                DrawMusicAttachedSettingsResolver.GetResultMusic(drawn.FirstOrDefault(), MusicSettings.ResultMusic),
                MusicSettings.ResultMusicVolume, MusicSettings.ResultMusicFadeIn, MusicSettings.ResultMusicFadeOut,
                MusicSettings.AnimationMusicFadeOut).ConfigureAwait(false);
            if (_voiceAnnouncementService is not null && VoiceAnnouncementSettings.VoiceAnnouncementEnabled)
                await _voiceAnnouncementService.SpeakStudentsAsync(drawn).ConfigureAwait(false);

            StartCooldown();
            ResetForNewRoundIfExhausted();
        }
        finally
        {
            SetDrawCommandRunning(false);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.QuickDrawReset, () => Task.CompletedTask))
            return;
        ClearHistoryCore();
    }

    private void ClearHistoryCore()
    {
        _notificationAutoCloseTime = null;
        _temporaryRecordService.ClearStudentScope(SelectedStudentListName, string.Empty, string.Empty);
        ResultItems.Clear();
        LastDrawnStudent = null;
        IsResultVisible = false;
        StatusText = QuickDrawResources.M_ResetDone;
        OnPropertyChanged(nameof(CanStartDraw));
    }

    private int? ResolveNotificationAutoCloseTime()
    {
        var basicSettings = Config.GetOverrideNotificationSettings(
            NotificationSettingsType.QuickDraw,
            OverridableNotificationSettingsType.Basic);
        if (!basicSettings.Enabled)
            return null;

        return Math.Clamp(Config.GetOverrideNotificationSettings(
                NotificationSettingsType.QuickDraw,
                OverridableNotificationSettingsType.Service).DisplayDuration,
            1,
            60);
    }

    public Task<bool> StartProtocolDrawAsync(bool protectLinkage = false) => _linkageDrawCoordinator.AuthorizeAsync(
        protectLinkage ? [SecurityOperation.QuickDrawStart, SecurityOperation.LinkageAction] : [SecurityOperation.QuickDrawStart],
        async () =>
        {
            LastDrawnStudent = null;
            await StartAuthorizedTriggeredDrawAsync();
        });

    public Task<bool> ResetProtocolDrawAsync(bool protectLinkage = false) => _linkageDrawCoordinator.AuthorizeAsync(
        protectLinkage ? [SecurityOperation.QuickDrawReset, SecurityOperation.LinkageAction] : [SecurityOperation.QuickDrawReset],
        () =>
        {
            ClearHistoryCore();
            return Task.CompletedTask;
        });

    private void RefreshStudentLists()
    {
        StudentListNames.Clear();
        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "roll_call_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            StudentListNames.Add(Path.GetFileNameWithoutExtension(file));

        var defaultClass = Config.QuickDrawSettings.DefaultClass;
        SelectedStudentListName = StudentListNames.Contains(defaultClass)
            ? defaultClass
            : string.Empty;
    }

    private bool TryLoadDefaultStudentList()
    {
        RefreshStudentLists();
        var defaultClass = Config.QuickDrawSettings.DefaultClass.Trim();
        if (!StudentListNames.Contains(defaultClass))
        {
            defaultClass = StudentListNames.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(defaultClass))
            {
                StatusText = QuickDrawResources.M_DefaultListRequired;
                return false;
            }

            Config.QuickDrawSettings.DefaultClass = defaultClass;
            _configHandler.Save();
        }

        if (SelectedStudentListName != defaultClass)
        {
            SelectedStudentListName = defaultClass;
        }
        else
        {
            // 选择属性未变化不代表共享档案就是该名单：点名页等其它会话可能已把共享档案切到别的名单，
            // 闪抽必须以解析出的默认名单为准，否则候选池会取自错误名单而证明文件名仍显示本名单。
            _profileService.LoadStudentProfile(defaultClass);
        }

        return true;
    }

    private void EnsureRestartTemporaryRecordsCleared(string listName)
    {
        if (Config.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(listName);
    }

    private IEnumerable<Student> GetEligibleCandidates()
    {
        var threshold = DrawRepeatPolicy.ResolveThreshold(Config.QuickDrawSettings.DrawMode, Config.QuickDrawSettings.HalfRepeat);
        var counts = _temporaryRecordService.GetStudentCounts(SelectedStudentListName, string.Empty, string.Empty);
        return DrawCandidateFilter.FilterEligibleStudents(
            _profileService.CurrentStudentList?.Students ?? [],
            string.Empty,
            string.Empty,
            counts,
            threshold);
    }

    /// <summary>
    ///     名单里还有成员但当前轮次已无可抽成员时（NoRepeat/HalfRepeat 抽空），自动重置临时记录
    ///     并继续抽取，与点名/抽奖的抽空自动重置保持一致，避免闪抽停在「没有可抽取的成员」。
    /// </summary>
    private bool ResetForNewRoundIfExhausted()
    {
        var students = (_profileService.CurrentStudentList?.Students ?? [])
            .Where(student => student.IsCandidate)
            .ToList();
        if (students.Count == 0)
            return false;

        var threshold = DrawRepeatPolicy.ResolveThreshold(Config.QuickDrawSettings.DrawMode, Config.QuickDrawSettings.HalfRepeat);
        var counts = _temporaryRecordService.GetStudentCounts(SelectedStudentListName, string.Empty, string.Empty);
        if (DrawCandidateFilter.FilterEligibleStudents(students, string.Empty, string.Empty, counts, threshold).Any())
            return false;

        _profileService.ClearCurrentStudentHistory();
        _temporaryRecordService.ResetStudentList(SelectedStudentListName);
        return true;
    }

    private async Task ShowPreviewAsync(IReadOnlyList<Student> candidates, int count, string animationMusic)
    {
        if (AnimationSettings.Animation == AnimationMode.NoAnimation)
            return;

        await _drawAudioService.StartAnimationMusicAsync(
            animationMusic,
            MusicSettings.AnimationMusicVolume,
            MusicSettings.AnimationMusicFadeIn, MusicSettings.AnimationMusicLoop).ConfigureAwait(true);

        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        var token = previewCts.Token;

        var manualStop = AnimationSettings.Animation == AnimationMode.ManualStop;
        var iterations = manualStop
            ? int.MaxValue
            : Math.Clamp(AnimationSettings.AutoplayCount, 1, 999);
        var delay = PreviewAnimationDuration;

        IsDrawing = true;
        try
        {
            for (var i = 0; i < iterations && !token.IsCancellationRequested; i++)
            {
                ReplaceResults(candidates.OrderBy(_ => Random.Shared.Next()).Take(count).ToList());
                IsResultVisible = true;
                TriggerPreviewAnimation();
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
        _ = _drawAudioService.StopAnimationMusicAsync(0, immediate: true);
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

    private void ReplaceResults(IReadOnlyList<Student> students)
    {
        ResultItems.Clear();
        foreach (var student in students)
            ResultItems.Add(CreateResultItem(student));
    }

    private void TriggerResultAnimation()
    {
        ResultAnimationRevision++;
    }

    public void ShowNotificationResult(
        IReadOnlyCollection<string> items,
        int autoCloseTime,
        bool animate,
        bool preserveQuickDrawResult)
    {
        _notificationAutoCloseTime = Math.Clamp(autoCloseTime, 0, 60);
        NotificationAnimationEnabled = animate;
        if (!preserveQuickDrawResult || ResultItems.Count == 0)
        {
            ResultItems.Clear();
            foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
                ResultItems.Add(CreateNotificationResultItem(item));
        }

        if (ResultItems.Count == 0)
            return;

        IsResultVisible = true;
        OnPropertyChanged(nameof(ResultAutoCloseTime));
        NotificationDisplayRevision++;
    }

    public void ShowNotificationPreview(
        IReadOnlyCollection<string> items,
        bool preserveQuickDrawResult)
    {
        _notificationAutoCloseTime = null;
        if (!preserveQuickDrawResult || ResultItems.Count == 0)
        {
            ResultItems.Clear();
            foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
                ResultItems.Add(CreateNotificationResultItem(item));
        }

        if (ResultItems.Count == 0)
            return;

        IsResultVisible = true;
        OnPropertyChanged(nameof(ResultAutoCloseTime));
        TriggerPreviewAnimation();
    }

    public bool NotificationAnimationEnabled { get; private set; }

    [ObservableProperty] private double? _notificationOpacity;

    public void ClearNotificationPresentation()
    {
        NotificationOpacity = null;
    }

    public void ResetForCourseLinkage()
    {
        ClearHistoryCore();
    }

    private static Dictionary<Student, double> BuildWeightSnapshot(
        IReadOnlyCollection<Student> drawnStudents,
        IReadOnlyDictionary<Guid, double> frozenWeights)
    {
        // 权重快照取自 proof 冻结输入，避免提交时重算与证明分叉。
        Dictionary<Student, double> snapshot = [];
        foreach (var student in drawnStudents)
        {
            ProfileRecordIdentity.EnsureRecordId(student);
            snapshot[student] = frozenWeights.GetValueOrDefault(student.RecordId, 1.0);
        }

        return snapshot;
    }

    private QuickDrawResultItem CreateResultItem(Student student)
    {
        var weight = BuildDisplayWeight(student);
        var accentBrush = ResolveAccentBrush();
        return new QuickDrawResultItem(
            FormatStudent(student),
            student.Tags,
            DisplaySettings.ShowTags && !string.IsNullOrWhiteSpace(student.Tags),
            DisplaySettings.DisplayStyle == DisplayStyleMode.Card,
            accentBrush,
            DrawColorHelper.ResolveTextBrush(accentBrush, Config.Appearance.Theme),
            DisplaySettings.ShowWeightTransparency,
            $"权重 {weight:0.##}",
            BuildResultOpacity(weight),
            BuildImage(student),
            StudentImageSettings.StudentImage,
            StudentImageSettings.StudentImagePosition,
            AvatarInitialResolver.Resolve(student.Name, student.Id));
    }

    private QuickDrawResultItem CreateNotificationResultItem(string item)
    {
        var accentBrush = ResolveAccentBrush();
        return new QuickDrawResultItem(
            item,
            string.Empty,
            false,
            DisplaySettings.DisplayStyle == DisplayStyleMode.Card,
            accentBrush,
            DrawColorHelper.ResolveTextBrush(accentBrush, Config.Appearance.Theme),
            false,
            string.Empty,
            1,
            null,
            false,
            StudentImagePositionMode.Top,
            item.Trim()[0].ToString());
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

    private double BuildDisplayWeight(Student student)
    {
        if (Config.QuickDrawSettings.DrawType != DrawType.Fair)
            return 1;

        return _drawEngine.CalculateStudentWeight((_profileService.CurrentStudentList?.Students ?? []).Where(s => s.IsCandidate).ToList(), courseName: _linkageDrawCoordinator.GetCourseName())
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Candidate, student))?.Weight ?? 1;
    }

    private double BuildResultOpacity(double weight)
    {
        return DisplaySettings.ShowWeightTransparency
            ? Math.Clamp(0.42 + Math.Min(weight, 3) / 3 * 0.58, 0.42, 1)
            : 1;
    }

    private IBrush? ResolveAccentBrush()
    {
        return DrawColorHelper.ResolveAccentBrush(
            ColorSettings.AnimationColorTheme,
            ColorSettings.AnimationFixedColor,
            Config.Appearance.Theme);
    }

    private FontFamily BuildResultFontFamily()
    {
        var font = DisplaySettings.UseGlobalFont == UseGlobalFontMode.Custom
            ? DisplaySettings.CustomFont
            : Config.Appearance.Font;
        return new FontFamily(string.Equals(font, "MiSans", StringComparison.OrdinalIgnoreCase)
            ? "avares://SecRandom/Assets/Fonts/MiSans/#MiSans"
            : font);
    }

    private static Bitmap? BuildImage(Student student)
    {
        var settings = student.GetAttachedObject<DrawImageAttachedSettings>(Guid.Parse(GlobalConstants.DrawImageAttachedSettings));
        if (settings is not { IsAttachSettingsEnabled: true } || string.IsNullOrWhiteSpace(settings.ImagePath))
            return null;

        try { return File.Exists(settings.ImagePath) ? new Bitmap(settings.ImagePath) : null; }
        catch { return null; }
    }

    private async void StartCooldown()
    {
        _isCoolingDown = true;
        OnPropertyChanged(nameof(CanStartDraw));
        await Task.Delay(Math.Clamp(Config.QuickDrawSettings.DisableAfterClick, 0, 60) * 1000).ConfigureAwait(true);
        _isCoolingDown = false;
        OnPropertyChanged(nameof(CanStartDraw));
    }

    public void Dispose()
    {
        StopPreview();
    }
}

public sealed record QuickDrawResultItem(
    string DisplayText,
    string Tags,
    bool IsTagsVisible,
    bool IsCardStyle,
    IBrush? AccentBrush,
    IBrush TextBrush,
    bool IsWeightVisible,
    string WeightText,
    double Opacity,
    Bitmap? Image,
    bool IsImageEnabled,
    StudentImagePositionMode ImagePosition,
    string Initial)
{
    public bool IsImageVisible => IsImageEnabled && Image is not null;
    public bool IsPlaceholderVisible => IsImageEnabled && Image is null;
    public Orientation ImageLayoutOrientation => ImagePosition is StudentImagePositionMode.Left or StudentImagePositionMode.Right
        ? Orientation.Horizontal
        : Orientation.Vertical;
    public bool IsImageBeforeText => IsImageEnabled && ImagePosition is StudentImagePositionMode.Left or StudentImagePositionMode.Top;
    public bool IsImageAfterText => IsImageEnabled && ImagePosition is StudentImagePositionMode.Right or StudentImagePositionMode.Bottom;
}
