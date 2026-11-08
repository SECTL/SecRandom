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
using Avalonia.Layout;
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
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Profiles;
using SecRandom.Helpers;
using SecRandom.Services.ControlNode;
using SecRandom.Services.Draw;
using SecRandom.Services.Linkage;
using SecRandom.Services.Notification;
using SecRandom.Services.Security;
using SecRandom.Services.Verification;
using SecRandom.ViewModels;
using SecRandom.Views;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;
using SR = SecRandom.Langs.MainPages.RollCall.Resources;

namespace SecRandom.ViewModels.MainPages;

public sealed partial class RollCallPageViewModel : ViewModelBase, IDisposable
{
    private static string AllGroupsOption => SR.O_AllGroups;
    private static string AllGendersOption => SR.O_AllGenders;
    private static string DefaultHistoryGroupOption => SR.O_HistoryGroupDefault;

    /// <summary>「不使用名单」下拉项：选中它即进入数字范围抽取模式。</summary>
    public static string NoListOption => SR.O_NoList;

    private readonly DrawEngine _drawEngine;
    private readonly IProfileService _profileService;
    private readonly IProfileCatalogManager _profileCatalogManager;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly IDrawCommitService _drawCommitService;
    private readonly IVoiceAnnouncementService? _voiceAnnouncementService;
    private readonly DrawAudioService? _drawAudioService;
    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<RollCallPageViewModel> _logger;
    private readonly ISecurityService _securityService;
    private readonly LinkageDrawCoordinator _linkageDrawCoordinator;
    private readonly VerificationDrawCoordinator _verificationDrawCoordinator;
    private readonly RollCallDrawService _rollCallDrawService;
    private readonly NotificationService? _notificationService;
    private readonly IHistoryGroupCatalog _historyGroupCatalog;
    private readonly FileSystemWatcher? _studentListWatcher;
    private readonly FileWatcherDebouncer? _studentListDebouncer;
    private List<Student> _lastResultStudents = [];
    private int _studentIdPadWidth;
    private bool _isRefreshingLists;
    private bool _isApplyingSelectionRefresh;
    private bool _isStudentListRefreshQueued;
    private bool _isDrawCommandRunning;
    private bool _historyGroupDefaultApplied;
    private CancellationTokenSource? _previewCts;

    [ObservableProperty] private string _selectedStudentListName = string.Empty;
    [ObservableProperty] private int _numberRangeStart = 1;
    [ObservableProperty] private int _numberRangeEnd = 50;
    [ObservableProperty] private string _selectedGroup = AllGroupsOption;
    [ObservableProperty] private string _selectedGender = AllGendersOption;
    [ObservableProperty] private string _selectedHistoryGroup = DefaultHistoryGroupOption;
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
        IProfileCatalogManager profileCatalogManager,
        IDrawTemporaryRecordService temporaryRecordService,
        IDrawCommitService drawCommitService,
        ILogger<RollCallPageViewModel> logger,
        ISecurityService securityService,
        LinkageDrawCoordinator linkageDrawCoordinator,
        VerificationDrawCoordinator verificationDrawCoordinator,
        RollCallDrawService rollCallDrawService,
        IHistoryGroupCatalog historyGroupCatalog,
        IVoiceAnnouncementService? voiceAnnouncementService = null,
        DrawAudioService? drawAudioService = null,
        NotificationService? notificationService = null)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _profileService = profileService;
        _profileCatalogManager = profileCatalogManager;
        _temporaryRecordService = temporaryRecordService;
        _drawCommitService = drawCommitService;
        _logger = logger;
        _securityService = securityService;
        _linkageDrawCoordinator = linkageDrawCoordinator;
        _verificationDrawCoordinator = verificationDrawCoordinator;
        _rollCallDrawService = rollCallDrawService;
        _historyGroupCatalog = historyGroupCatalog;
        _voiceAnnouncementService = voiceAnnouncementService;
        _drawAudioService = drawAudioService;
        _notificationService = notificationService;

        ResultText = ReminderSettings.ReminderText;
        if (App.IsDesktop && !OperatingSystem.IsIOS())
        {
            _studentListWatcher = CreateStudentListWatcher();
            _studentListDebouncer = new FileWatcherDebouncer(
                TimeSpan.FromMilliseconds(400),
                RefreshAfterListFileChange);
        }

        StudentListNames.CollectionChanged += StudentListNamesOnCollectionChanged;
        ResultItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ResultText));

        Config.EffectiveRollCallSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.EffectiveDefaultDrawSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.EffectiveMoreSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.EffectiveHistoryGroups.PropertyChanged += SettingsOnPropertyChanged;
        Config.Appearance.PropertyChanged += SettingsOnPropertyChanged;

        RefreshLists();
        RefreshFilterOptions();
        RefreshCounts();
    }

    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<string> GroupOptions { get; } = [AllGroupsOption];
    public ObservableCollection<string> GenderOptions { get; } = [AllGendersOption];
    public ObservableCollection<string> HistoryGroupOptions { get; } = [DefaultHistoryGroupOption];
    public ObservableCollection<RollCallResultItem> ResultItems { get; } = [];
    public ObservableCollection<RollCallRemainingItem> RemainingItems { get; } = [];

    /// <summary>Students of the most recent draw, exposed so plugins can present the result themselves.</summary>
    public IReadOnlyList<Student> LastResultStudents => _lastResultStudents;

    /// <summary>
    ///     上一次结果的展示文本（按「显示格式」把编号和名称拼好），暴露给插件，
    ///     让插件牌面上的字和主程序内置结果卡完全一致。
    /// </summary>
    public IReadOnlyList<string> LastResultDisplayTitles =>
        _lastResultStudents.Select(FormatStudent).ToArray();

    public MoreSettingsConfig MoreSettings => Config.EffectiveMoreSettings;
    public bool IsControlPanelOnLeft => MoreSettings.RollCallControlPanelPosition == RollCallControlPanelPosition.Left;
    public bool IsControlPanelOnRight => !IsControlPanelOnLeft;

    /// <summary>
    ///     面板内容列（固定 144）朝哪一侧对齐：靠左的面板左对齐、靠右的面板右对齐，
    ///     使上方的按钮/下拉框与数字范围输入框共用同一条外边，起止标签落在旁边 36px 的留白里。
    /// </summary>
    public HorizontalAlignment ControlPanelContentAlignment =>
        IsControlPanelOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;

    /// <summary>
    ///     记录组选择器是否出现：既要功能开关打开，也要这一页的显隐开关打开。
    /// </summary>
    /// <remarks>
    ///     功能开关落在 <c>General.HistoryGroups.Enabled</c>（设备功能总闸），显隐开关落在
    ///     <c>MoreSettings</c>（与「范围/性别选择器」同一族，属于主页面布局偏好）。用派生属性而不是
    ///     手写 <c>IsVisible</c> 转换器，是为了让移动端复用同一份 VM 时自动一致。
    /// </remarks>
    public bool IsHistoryGroupSelectorVisible =>
        Config.EffectiveHistoryGroups.Enabled && MoreSettings.RollCallHistoryGroupSelector && !IsNumberMode;

    /// <summary>
    ///     是否处于「不使用名单」的数字范围抽取模式：此时候选池是一段整数，与共享档案无关。
    /// </summary>
    public bool IsNumberMode => string.Equals(SelectedStudentListName, NoListOption, StringComparison.Ordinal);

    /// <summary>数字模式下没有性别维度，性别选择器随隐藏。</summary>
    public bool IsGenderSelectorVisible => MoreSettings.RollCallGenderSelector && !IsNumberMode;

    /// <summary>数字模式下没有分组维度，分组选择器随隐藏。</summary>
    public bool IsGroupSelectorVisible => MoreSettings.RollCallRangeSelector && !IsNumberMode;

    /// <summary>数字模式没有「剩余名单」可看，按钮随隐藏。</summary>
    public bool IsRemainingListButtonVisible => MoreSettings.RollCallRemainingButton && !IsNumberMode;

    public bool CanDecreaseCount => DrawCount > 1;
    public bool CanIncreaseCount => DrawCount < MaximumDrawCount;
    public bool CanStartDraw => IsDrawing || (!_isDrawCommandRunning && TotalCount > 0);
    public string DrawButtonText => IsDrawing ? SR.C_Stop : SR.C_Start;
    public int TotalCount { get; private set; }
    public int RemainingCount { get; private set; }
    public int MaximumDrawCount => Math.Max(1, Math.Max(TotalCount, RemainingCount));
    public string CountSummary => IsNumberMode
        ? string.Format(SR.M_NumberCountSummary, RemainingCount, TotalCount)
        : string.Format(SR.M_CountSummaryFormat, TotalCount, RemainingCount);
    public string ResultText { get; private set; }
    public IBrush ReminderBrush => BuildReminderBrush();
    public double ResultFontSize => DisplaySettings.FontSize;
    public double ReminderFontSize => ReminderSettings.ReminderFontSize;
    public FontFamily ResultFontFamily => BuildResultFontFamily();
    public bool IsResultCardStyle => DisplaySettings.DisplayStyle == DisplayStyleMode.Card;
    public bool AnimationEnabled => AnimationSettings.Animation != AnimationMode.NoAnimation;
    public DrawAnimationStyleMode AnimationStyle => AnimationSettings.AnimationStyle;
    public string PluginAnimationId => AnimationSettings.PluginAnimationId;
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

    /// <summary>
    ///     本次抽取要读写的记录组；<see langword="null" /> 表示不做组隔离（看全部历史）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         三态语义：<see langword="null" /> = 不按组过滤（功能关闭或本页选择器隐藏时的旧行为）；
    ///         空串 = 「默认记录组」，即 <c>HistoryItem.HistoryGroup</c> 为空串的未分组记录；
    ///         其余 = 该组的记录。空串与 <see langword="null" /> 必须分开，否则「默认记录组」这个
    ///         选项会把已分组记录也算进来，标签就成了假话。
    ///     </para>
    ///     <para>
    ///         这里刻意把「选择器当前不可见」也算成 <see langword="null" />：功能总闸被关掉、或本页
    ///         显隐开关被关掉之后，隐藏起来的选择器不可能再被用户看见，这时若继续沿用内存里残留的
    ///         旧选择，抽取就会静默落进一个用户以为已经失效的组——这是这个功能最容易出的
    ///         「静默写错地方」，所以在唯一取值点上兜住。
    ///     </para>
    /// </remarks>
    private string? CurrentHistoryGroup =>
        IsHistoryGroupSelectorVisible
            ? SelectedHistoryGroup == DefaultHistoryGroupOption ? string.Empty : SelectedHistoryGroup
            : null;

    partial void OnSelectedStudentListNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !_isApplyingSelectionRefresh)
        {
            // 自动刷新解析出的选择（名单被删、学生暂时全被禁用等）不算用户决策：
            // 依然按该选择加载档案/计数，但不得覆盖用户配置的默认名单。
            var persistSelection = !_isRefreshingLists;
            if (IsNumberMode)
            {
                // 数字模式不读任何名单：不切换共享档案（否则会把别的会话的档案改成"数字名单"），
                // 只把「默认抽取名单」记成哨兵值，下次进页面仍然是数字模式。
                if (persistSelection && !RosterSelection.IsNone(Config.RollCallSettings.DefaultClass))
                {
                    Config.RollCallSettings.DefaultClass = RosterSelection.None;
                    _configHandler.Save();
                }
            }
            else
            {
                _profileService.LoadStudentProfile(value);
                EnsureRestartTemporaryRecordsCleared(value);
                if (persistSelection && Config.RollCallSettings.DefaultClass != value)
                {
                    Config.RollCallSettings.DefaultClass = value;
                    _configHandler.Save();
                }
            }
        }

        // 模式切换会同时改变筛选器显隐与计数口径，派生属性必须一并重算。
        OnPropertyChanged(nameof(IsNumberMode));
        OnPropertyChanged(nameof(IsGenderSelectorVisible));
        OnPropertyChanged(nameof(IsGroupSelectorVisible));
        OnPropertyChanged(nameof(IsHistoryGroupSelectorVisible));
        OnPropertyChanged(nameof(IsRemainingListButtonVisible));
        RefreshFilterOptions();
        RefreshCounts();
    }

    partial void OnNumberRangeStartChanged(int value)
    {
        NormalizeNumberRange(startChanged: true, value);
    }

    partial void OnNumberRangeEndChanged(int value)
    {
        NormalizeNumberRange(startChanged: false, value);
    }

    /// <summary>
    ///     把起始/终止数字夹到 <see cref="RosterSelection" /> 允许的区间：起始不小于 0、终止不小于起始、
    ///     跨度不超过 <see cref="RosterSelection.MaxNumberRangeSpan" />。
    /// </summary>
    /// <remarks>
    ///     夹紧后的值再写回属性会重新进入这里；用「值确实变了才回写」收敛，避免自激。
    /// </remarks>
    private void NormalizeNumberRange(bool startChanged, int value)
    {
        var start = Math.Max(0, startChanged ? value : NumberRangeStart);
        var end = RosterSelection.ClampEnd(start, startChanged ? NumberRangeEnd : value);
        if (startChanged && start != value)
        {
            NumberRangeStart = start;
            return;
        }

        if (!startChanged && end != value)
        {
            NumberRangeEnd = end;
            return;
        }

        if (end != NumberRangeEnd)
        {
            NumberRangeEnd = end;
            return;
        }

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

    partial void OnSelectedHistoryGroupChanged(string value)
    {
        // 组选择是纯界面状态：不写配置、不触发验证、只刷新计数与剩余名单。
        // 「换组即改权重」全靠这一条路径，所以这里绝不能顺手 Save()。
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
        ResetDrawHistoryCore(showToast: true);
    }

    /// <summary>远程重置的非交互闸门：需要本机验证时返回拒绝，由调用方回给控制台/手机。</summary>
    public RemoteDrawOutcome? EvaluateRemoteReset() =>
        ControlDrawGateRejections.From(_linkageDrawCoordinator.EvaluateGate(SecurityOperation.RollCallReset));

    /// <summary>
    ///     集控远程重置的**展示态**清理：等价于在本地点一次"重置"，但不弹任何验证框。
    /// </summary>
    /// <remarks>
    ///     复用 <see cref="ResetDrawHistoryCore" /> 这条既有路径（清结果区、隐藏结果、恢复提示语、
    ///     刷新剩余/候选计数），而不是另写一套"只清数据"的逻辑——只清数据的话，教室里看到的
    ///     还是上一轮抽到的那个人，看起来就像重置没生效。闸门由调用方在此之前判定。
    /// </remarks>
    public void ResetRemotePresentation() => ResetDrawHistoryCore(showToast: false);

    private void ResetDrawHistoryCore(bool showToast = false)
    {
        _lastResultStudents.Clear();
        ResultItems.Clear();
        if (IsNumberMode)
            _rollCallDrawService.ResetNumbers();
        else
            _rollCallDrawService.Reset(CurrentGroupScope, CurrentGenderScope);
        IsResultVisible = false;
        ResultText = ReminderSettings.ReminderText;
        StatusText = SR.M_ResetDone;
        if (showToast)
            MainView.ShowSuccessToast(SR.M_ResetDone);
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

        if (IsNumberMode)
        {
            await StartNumberDrawCoreAsync();
            return;
        }

        EnsureActiveProfileMatchesSelection();

        RefreshCounts();
        ResetForNewRoundIfExhausted();
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
        // 记录组与候选池同一刻冻结：候选用的是这一刻的组，记录也要落进同一个组，
        // 否则中途切组会让「按 A 组权重抽出来的人写进 B 组记录」。
        var historyGroup = CurrentHistoryGroup;
        SetDrawCommandRunning(true);
        IReadOnlyList<FullscreenResultItem> fullscreenPool = [];
        try
        {
            if (_notificationService is not null
                && _notificationService.UsesFullscreenNotificationService(NotificationSettingsType.RollCall))
            {
                fullscreenPool = FullscreenNotificationComposer.MapStudents(candidates);
                _notificationService.BeginFullscreenPresentation(
                    NotificationSettingsType.RollCall,
                    FullscreenNotificationComposer.CreateSeed(
                        NotificationSettingsType.RollCall,
                        fullscreenPool,
                        TotalCount,
                        RemainingCount,
                        SelectedStudentListName));
            }

            if (_notificationService is not null)
                await _notificationService.BeginClassIslandAnimationAsync(
                    NotificationSettingsType.RollCall,
                    SelectedStudentListName,
                    candidates,
                    count);

            var courseName = _linkageDrawCoordinator.GetCourseName();
            var drawTask = _rollCallDrawService.DrawAsync(new RollCallDrawRequest(
                SelectedStudentListName, CurrentGroupScope, CurrentGenderScope, count, courseName, historyGroup));
            var previewTask = ShowPreviewAsync(candidates, count, MusicSettings.AnimationMusic);
            List<Student> drawnStudents;
            try
            {
                var drawCompletedFirst = await Task.WhenAny(drawTask, previewTask).ConfigureAwait(true) == drawTask;
                var drawResult = await drawTask.ConfigureAwait(true);
                if (drawResult is null)
                {
                    // The list still has eligible members but every one of them is temporarily excluded
                    // (post-draw shield), so this is a drawable-member state, not a failed draw.
                    _previewCts?.Cancel();
                    await (_drawAudioService?.StopAnimationMusicAsync(0, immediate: true) ?? Task.CompletedTask)
                        .ConfigureAwait(false);
                    ClearUncommittedPreview();
                    _notificationService?.CancelFullscreenPresentation(NotificationSettingsType.RollCall);
                    StatusText = SR.M_NoCandidates;
                    return;
                }

                drawnStudents = drawResult.Students.ToList();
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
                _notificationService?.CancelFullscreenPresentation(NotificationSettingsType.RollCall);
                StatusText = SR.M_DrawFailed;
                return;
            }

            ResultItems.Clear();
            _lastResultStudents = drawnStudents;
            RefreshResultItems();

            IsResultVisible = ResultItems.Count > 0;
            TriggerResultAnimation();
            await PlayResultMusicAsync(drawnStudents.FirstOrDefault()).ConfigureAwait(false);
            StatusText = string.Format(SR.M_DrawnCountFormat, ResultItems.Count);
            RefreshCounts();
            ResetForNewRoundIfExhausted();
            OnPropertyChanged(nameof(ResultText));

            if (_notificationService is not null)
            {
                _notificationService.QueueStudents(
                    NotificationSettingsType.RollCall,
                    SelectedStudentListName,
                    drawnStudents);

                if (_notificationService.UsesFullscreenNotificationService(NotificationSettingsType.RollCall))
                    _notificationService.CompleteFullscreenPresentation(
                        NotificationSettingsType.RollCall,
                        FullscreenNotificationComposer.CreateSnapshot(
                            NotificationSettingsType.RollCall,
                            FullscreenNotificationComposer.MapStudents(drawnStudents),
                            fullscreenPool,
                            TotalCount,
                            RemainingCount,
                            courseName,
                            historyGroup,
                            SelectedStudentListName));
            }

            // 全屏渠道自己会播报时，跳过全局语音，避免同一句结果被读两遍。
            var fullscreenVoiceActive = _notificationService?.UsesFullscreenNotificationService(NotificationSettingsType.RollCall) == true
                                        && Config.NotificationSettings.Fullscreen.ShowVoiceAnnouncement;
            if (!fullscreenVoiceActive && _voiceAnnouncementService is not null && VoiceAnnouncementSettings.VoiceAnnouncementEnabled)
                await _voiceAnnouncementService.SpeakStudentsAsync(drawnStudents).ConfigureAwait(false);
        }
        finally
        {
            SetDrawCommandRunning(false);
        }
    }

    /// <summary>
    ///     数字范围抽取：候选池是一段连续整数（每个整数一张"票"），与任何名单/共享档案无关。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         预览候选在开始抽取**之前**就冻结：动画期间区间值可能被改动，而提交用的区间来自
    ///         同一刻的 <see cref="NumberRangeStart" />/<see cref="NumberRangeEnd" />，冻结预览只是为了让
    ///         舞台上滚动的数字和真正参与抽取的数字是同一批。
    ///     </para>
    ///     <para>
    ///         公平性与可验证证明由 <see cref="RollCallDrawService.DrawNumbersAsync" /> 内部完成（重复规则、
    ///         权重、证明发布、事务提交），这里只做界面编排，结果处理与名单抽取保持一致。
    ///     </para>
    /// </remarks>
    private async Task StartNumberDrawCoreAsync()
    {
        RefreshCounts();
        ResetForNewRoundIfExhausted();
        if (RemainingCount <= 0)
        {
            StatusText = TotalCount == 0 ? SR.M_NoStudents : SR.M_NoRemainingStudents;
            return;
        }

        // 预览用整段区间而不是"剩余票"：动画只是示意，受重复规则约束的结果由服务返回。
        var frozenCandidates = NumberTickets.CreateStudents(NumberRangeStart, NumberRangeEnd);
        if (frozenCandidates.Count == 0)
        {
            StatusText = SR.M_NoCandidates;
            return;
        }

        var count = Math.Clamp(DrawCount, 1, Math.Min(RemainingCount, frozenCandidates.Count));
        var historyGroup = CurrentHistoryGroup;
        SetDrawCommandRunning(true);
        IReadOnlyList<FullscreenResultItem> fullscreenPool = [];
        try
        {
            if (_notificationService is not null
                && _notificationService.UsesFullscreenNotificationService(NotificationSettingsType.RollCall))
            {
                fullscreenPool = FullscreenNotificationComposer.MapStudents(frozenCandidates);
                _notificationService.BeginFullscreenPresentation(
                    NotificationSettingsType.RollCall,
                    FullscreenNotificationComposer.CreateSeed(
                        NotificationSettingsType.RollCall,
                        fullscreenPool,
                        TotalCount,
                        RemainingCount,
                        NoListOption));
            }

            if (_notificationService is not null)
                await _notificationService.BeginClassIslandAnimationAsync(
                    NotificationSettingsType.RollCall,
                    NoListOption,
                    frozenCandidates,
                    count);

            var courseName = _linkageDrawCoordinator.GetCourseName();
            var drawTask = _rollCallDrawService.DrawNumbersAsync(new RollCallNumberDrawRequest(
                NumberRangeStart, NumberRangeEnd, count, courseName, historyGroup));
            var previewTask = ShowPreviewAsync(frozenCandidates, count, MusicSettings.AnimationMusic);
            List<Student> drawnStudents;
            try
            {
                var drawCompletedFirst = await Task.WhenAny(drawTask, previewTask).ConfigureAwait(true) == drawTask;
                var drawResult = await drawTask.ConfigureAwait(true);
                if (drawResult is null)
                {
                    _previewCts?.Cancel();
                    await (_drawAudioService?.StopAnimationMusicAsync(0, immediate: true) ?? Task.CompletedTask)
                        .ConfigureAwait(false);
                    ClearUncommittedPreview();
                    _notificationService?.CancelFullscreenPresentation(NotificationSettingsType.RollCall);
                    StatusText = SR.M_NoCandidates;
                    return;
                }

                drawnStudents = drawResult.Numbers.ToList();
                if (drawCompletedFirst && !previewTask.IsCompleted)
                    await PlayAnimationMusicAsync(DrawMusicAttachedSettingsResolver.GetAnimationMusic(
                        drawnStudents.FirstOrDefault(), MusicSettings.AnimationMusic)).ConfigureAwait(true);

                await previewTask.ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "可验证数字点名抽取失败。");
                _previewCts?.Cancel();
                await (_drawAudioService?.StopAnimationMusicAsync(0, immediate: true) ?? Task.CompletedTask)
                    .ConfigureAwait(false);
                ClearUncommittedPreview();
                _notificationService?.CancelFullscreenPresentation(NotificationSettingsType.RollCall);
                StatusText = SR.M_DrawFailed;
                return;
            }

            ResultItems.Clear();
            _lastResultStudents = drawnStudents;
            RefreshResultItems();

            IsResultVisible = ResultItems.Count > 0;
            TriggerResultAnimation();
            await PlayResultMusicAsync(drawnStudents.FirstOrDefault()).ConfigureAwait(false);
            StatusText = string.Format(SR.M_DrawnCountFormat, ResultItems.Count);
            RefreshCounts();
            ResetForNewRoundIfExhausted();
            OnPropertyChanged(nameof(ResultText));

            if (_notificationService is not null)
            {
                _notificationService.QueueStudents(
                    NotificationSettingsType.RollCall,
                    NoListOption,
                    drawnStudents);

                if (_notificationService.UsesFullscreenNotificationService(NotificationSettingsType.RollCall))
                    _notificationService.CompleteFullscreenPresentation(
                        NotificationSettingsType.RollCall,
                        FullscreenNotificationComposer.CreateSnapshot(
                            NotificationSettingsType.RollCall,
                            FullscreenNotificationComposer.MapStudents(drawnStudents),
                            fullscreenPool,
                            TotalCount,
                            RemainingCount,
                            courseName,
                            historyGroup,
                            NoListOption));
            }

            var fullscreenVoiceActive = _notificationService?.UsesFullscreenNotificationService(NotificationSettingsType.RollCall) == true
                                        && Config.NotificationSettings.Fullscreen.ShowVoiceAnnouncement;
            if (!fullscreenVoiceActive && _voiceAnnouncementService is not null && VoiceAnnouncementSettings.VoiceAnnouncementEnabled)
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

    /// <summary>
    ///     集控 / 手机端远程点名：指定名单与条件抽一次，结果留在这一页上给课堂看。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         与本地抽取走**同一条**路径（同一份候选池、同一套重复规则、同一个事务提交）：
    ///         远程只是"替老师按了按钮"，不是第二条抽取实现。差别只在三处：条件由载荷指定、
    ///         先做无交互闸门判定、结果回执要带上抽到了谁。
    ///     </para>
    ///     <para>
    ///         名单存在性用 <see cref="IProfileCatalogManager.LoadStudentList" /> 判定：
    ///         它读的是**不切换活动档案**的快照，所以"名单不存在/条件不成立"这些拒绝
    ///         不会在拒绝之前先把教室机的页面切到别的名单上。
    ///     </para>
    ///     <para>
    ///         条件校验在切换之前完成（<see cref="ControlDrawConditions.TryResolve" />），
    ///         通过之后才切名单与筛选条件——切过去就意味着这台机器接下来的点名都在这条名单上，
    ///         一个注定被拒绝的命令不该留下这个副作用。
    ///     </para>
    ///     <para>
    ///         这里**不弹交互式验证**：手机那头没人能替教室机输密码。先问 <c>EvaluateGate</c>，
    ///         需要本机验证时直接拒绝，控制台/手机看到"设备拒绝"而不是一条挂住的命令。
    ///     </para>
    /// </remarks>
    public async Task<RemoteDrawOutcome> StartRemoteDrawAsync(
        ControlDrawTriggerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SecurityOperation[] operations = [SecurityOperation.RollCallStart, SecurityOperation.LinkageAction];
        if (ControlDrawGateRejections.From(_linkageDrawCoordinator.EvaluateGate(operations)) is { } rejection)
            return rejection;

        if (IsDrawing)
            return RemoteDrawOutcome.Busy();

        RefreshLists();
        // 数字模式用稳定哨兵值表示：载荷里的空串与哨兵都映射成「不使用名单」；
        // null 仍保持旧语义——沿用本页当前选择。
        var listName = request.ListName is null ? SelectedStudentListName : request.ListName;
        if (string.IsNullOrWhiteSpace(listName) || RosterSelection.IsNone(listName))
            listName = NoListOption;
        if (!StudentListNames.Contains(listName))
            return RemoteDrawOutcome.Invalid("invalid_value:list_name:not_found");

        var isNumberDraw = string.Equals(listName, NoListOption, StringComparison.Ordinal);
        if (!isNumberDraw)
        {
            var members = _profileCatalogManager.LoadStudentList(listName)?.Students;
            if (members is null || members.Count == 0)
                return RemoteDrawOutcome.Invalid("invalid_value:list_name:no_candidate");

            if (!ControlDrawConditions.TryResolve(members, request, out _, out var reason))
                return RemoteDrawOutcome.Invalid(reason);

            // 切换页面选择与共享档案：抽取用的候选池取自共享档案，只改页面选择会抽到上一次的名单。
            if (string.Equals(SelectedStudentListName, listName, StringComparison.Ordinal))
                _profileService.LoadStudentProfile(listName);
            else
                SelectedStudentListName = listName;
        }
        else if (!string.Equals(SelectedStudentListName, NoListOption, StringComparison.Ordinal))
        {
            // 数字模式没有名单可切，只切换本页选择（走与用户选择相同的分支：记录默认值为哨兵）。
            SelectedStudentListName = NoListOption;
        }

        SelectedGender = request.Gender ?? AllGendersOption;
        SelectedGroup = request.Group ?? AllGroupsOption;
        RefreshCounts();
        DrawCount = request.Count ?? 1;

        if (!CanStartDraw)
            return RemoteDrawOutcome.Invalid("invalid_value:list_name:no_candidate");

        // 抽完才换引用：失败路径不清空上一轮结果，因此不能用"结果非空"判断这次是否成功。
        var previousResult = _lastResultStudents;
        var authorized = await _linkageDrawCoordinator.AuthorizeAsync(operations, StartDrawCoreAsync, cancellationToken)
            .ConfigureAwait(true);

        if (!authorized)
            return RemoteDrawOutcome.Denied("local_verification_required");

        if (ReferenceEquals(previousResult, _lastResultStudents) || _lastResultStudents.Count == 0)
            return RemoteDrawOutcome.Denied("no_candidate");

        return RemoteDrawOutcome.DrawnFrom(ControlDrawnMembers.FromStudents(_lastResultStudents), listName);
    }

    public Task ToggleDrawFromShortcutAsync() => StartDrawAsync();

    public Task<bool> ResetProtocolDrawAsync(bool protectLinkage = false) => _linkageDrawCoordinator.AuthorizeAsync(
        protectLinkage ? [SecurityOperation.RollCallReset, SecurityOperation.LinkageAction] : [SecurityOperation.RollCallReset],
        () =>
        {
            ResetDrawHistoryCore(showToast: true);
            return Task.CompletedTask;
        });
    public void StopProtocolDraw() => StopPreview();

    [RelayCommand]
    private void OpenRollCallSettings()
    {
        App.ShowSettingsWindow("settings.picking.rollCall");
    }

    [RelayCommand]
    private void OpenListSettings()
    {
        App.ShowSettingsWindow("settings.listManagement.rollCallList");
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
            // 首项永远是「不使用名单」：它既是"没有任何名单"时的默认值，也是数字范围抽取模式的入口。
            StudentListNames.Add(NoListOption);

            var directory = Utils.GetDirectoryPath("list", "roll_call_list");
            var realNames = Directory.GetFiles(directory, "*.json")
                .OrderBy(Path.GetFileName)
                .Select(file => Path.GetFileNameWithoutExtension(file) ?? string.Empty)
                .Where(RosterSelection.IsUsable)
                .ToList();
            foreach (var name in realNames)
                StudentListNames.Add(name);

            var hasDrawable = realNames.Count > 0 && HasDrawableStudents(realNames);
            var defaultClass = Config.RollCallSettings.DefaultClass;
            // 旧配置里没有「默认抽取名单」时，一旦出现可抽取的真实名单就自动把第一份设为默认：用户没有配置过默认名单，
            // 设置页的默认名单下拉框也应该直接落在这一份上。空串才代表「从未设置」，哨兵「不使用名单」是明确选择，必须保留。
            if (hasDrawable && string.IsNullOrEmpty(defaultClass))
            {
                defaultClass = realNames[0];
                Config.RollCallSettings.DefaultClass = defaultClass;
                _configHandler.Save();
            }

            string selected;
            if (!hasDrawable)
            {
                // 名单可以被完全删掉（这里刻意不再自动创建 default.json），也可能全都关掉了学生：
                // 没有可抽的名单时默认落到数字范围抽取，而不是显示一个抽不出任何人的名单。
                selected = NoListOption;
            }
            else if (realNames.Contains(previousName))
            {
                selected = previousName;
            }
            else if (RosterSelection.IsNone(defaultClass))
            {
                selected = NoListOption;
            }
            else if (realNames.Contains(defaultClass))
            {
                selected = defaultClass;
            }
            else
            {
                selected = realNames[0];
            }

            // 后台刷新（列表文件 watcher）会先清空下拉项，TwoWay 绑定把选中项回写成空值，
            // 随后又原样恢复为刷新前的选择；这种「假变化」不得把共享档案切回本页选中项，
            // 否则会打断闪抽等其它会话已切好的名单。仅当解析结果确实不同于刷新前（名单被删/改名等）
            // 或用户主动选择时才切换共享档案。
            _isApplyingSelectionRefresh = selected == previousName;
            try
            {
                SelectedStudentListName = selected;
            }
            finally
            {
                _isApplyingSelectionRefresh = false;
            }

            UpdateStudentIdPadWidth();
        }
        finally
        {
            _isRefreshingLists = false;
        }
    }

    /// <summary>真实名单里是否至少有一份含有可抽取的学生。</summary>
    /// <remarks>
    ///     用 <see cref="IProfileCatalogManager.GetStudentListNames" /> +
    ///     <see cref="IProfileCatalogManager.LoadStudentList" /> 的快照判定，绝不切换活动档案：
    ///     一个后台刷新（文件 watcher、设置页改名单）不该把别的会话的档案切走。
    /// </remarks>
    private bool HasDrawableStudents(IReadOnlyList<string> realNames)
    {
        var names = _profileCatalogManager.GetStudentListNames();
        if (names.Count == 0)
            names = realNames;

        foreach (var name in names)
        {
            try
            {
                if (_profileCatalogManager.LoadStudentList(name)?.Students.Any(student => student.IsCandidate) == true)
                    return true;
            }
            catch (Exception exception)
            {
                // 单份名单读不出来不能拖垮整个下拉框刷新，按"没有可抽学生"处理。
                _logger.LogWarning(exception, "读取名单 {ListName} 失败。", name);
            }
        }

        return false;
    }

    /// <summary>Refreshes the shared draw session after profile mutations made by settings or import flows.</summary>
    public void RefreshAfterProfileChange()
    {
        RefreshLists();
        RefreshFilterOptions();
        RefreshCounts();
    }

    public void Dispose()
    {
        StopPreview();
        _ = _drawAudioService?.StopAnimationMusicAsync(0, immediate: true);
        StudentListNames.CollectionChanged -= StudentListNamesOnCollectionChanged;
        Config.EffectiveRollCallSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.EffectiveDefaultDrawSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.EffectiveMoreSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.EffectiveHistoryGroups.PropertyChanged -= SettingsOnPropertyChanged;
        Config.Appearance.PropertyChanged -= SettingsOnPropertyChanged;
        _studentListDebouncer?.Dispose();
        _studentListWatcher?.Dispose();
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
        // 合并短时间内的连续文件事件；没有去抖器（移动端）时退回原来的即时合并逻辑。
        if (_studentListDebouncer != null)
        {
            _studentListDebouncer.Notify();
            return;
        }

        if (_isStudentListRefreshQueued)
            return;

        _isStudentListRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isStudentListRefreshQueued = false;
            RefreshAfterListFileChange();
        });
    }

    private void RefreshAfterListFileChange()
    {
        RefreshLists();
        RefreshFilterOptions();
        RefreshCounts();
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Config.EffectiveMoreSettings)
        {
            OnPropertyChanged(nameof(IsControlPanelOnLeft));
            OnPropertyChanged(nameof(IsControlPanelOnRight));
            OnPropertyChanged(nameof(ControlPanelContentAlignment));
            // 三个选择器（分组/性别/剩余名单）的显隐开关与数字模式是"与"的关系，开关变化要重算。
            OnPropertyChanged(nameof(IsGenderSelectorVisible));
            OnPropertyChanged(nameof(IsGroupSelectorVisible));
            OnPropertyChanged(nameof(IsRemainingListButtonVisible));
        }

        if (sender == Config.EffectiveMoreSettings || sender == Config.EffectiveHistoryGroups)
        {
            // 选择器显隐会改变「当前生效的组」的含义（隐藏即不做组隔离），所以两种开关变化
            // 都要重算派生属性、刷新可选组与计数。
            OnPropertyChanged(nameof(IsHistoryGroupSelectorVisible));
            RefreshHistoryGroupOptions();
            RefreshCounts();
        }

        if (sender == Config.EffectiveRollCallSettings && e.PropertyName == nameof(RollCallSettingsConfig.HistoryGroup))
        {
            // 默认记录组在设置页被改掉后重新套用一次，回到主页面立刻看到新的初始选择。
            _historyGroupDefaultApplied = false;
            RefreshHistoryGroupOptions();
            RefreshCounts();
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
        RefreshHistoryGroupOptions();

        if (!GroupOptions.Contains(SelectedGroup))
            SelectedGroup = AllGroupsOption;
        if (!GenderOptions.Contains(SelectedGender))
            SelectedGender = AllGendersOption;
    }

    /// <summary>
    ///     记录组下拉项 = 默认项 + 当前档案里已存在的记录组。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         组名来自「注册过的组 ∪ 记录上出现过的组名」，因此删掉组登记后旧记录仍会作为选项出现，
    ///         不会让用户手里只剩一个看不到数据的空组。
    ///     </para>
    ///     <para>
    ///         选中项在刷新后仍然存在就保留，否则回落到默认项——但仅在选择器可见时才回写，
    ///         免得功能关闭期间把用户的选择悄悄清掉。
    ///     </para>
    /// </remarks>
    private void RefreshHistoryGroupOptions()
    {
        if (!IsHistoryGroupSelectorVisible)
            return;

        IReadOnlyList<string> groups;
        try
        {
            groups = _historyGroupCatalog.GetGroupNames(HistoryGroupKind.RollCall);
        }
        catch (Exception exception)
        {
            // 组目录读不出来时只降级成「只有默认项」，不能连主页面都打不开。
            _logger.LogWarning(exception, "读取点名历史记录组失败。");
            return;
        }

        ReplaceOptions(HistoryGroupOptions, DefaultHistoryGroupOption, groups);
        if (!HistoryGroupOptions.Contains(SelectedHistoryGroup))
            SelectedHistoryGroup = DefaultHistoryGroupOption;

        if (_historyGroupDefaultApplied)
            return;

        // 首次可见时把选择器落到「抽取设置 → 点名」里的默认记录组；此后页面上的切换只是
        // 本次会话的界面状态，绝不回写配置（选择器本身仍然不是持久化设置）。
        _historyGroupDefaultApplied = true;
        ApplyDefaultHistoryGroup();
    }

    /// <summary>把选择器套用到配置里的默认记录组；默认组已不存在时回落到「默认记录组」。</summary>
    private void ApplyDefaultHistoryGroup()
    {
        var configured = Config.EffectiveRollCallSettings.HistoryGroup;
        var option = string.IsNullOrWhiteSpace(configured) ? DefaultHistoryGroupOption : configured;
        SelectedHistoryGroup = HistoryGroupOptions.Contains(option) ? option : DefaultHistoryGroupOption;
    }

    private void RefreshCounts()
    {
        UpdateStudentIdPadWidth();

        if (IsNumberMode)
        {
            var snapshot = _rollCallDrawService.GetNumberSnapshot(NumberRangeStart, NumberRangeEnd);
            TotalCount = snapshot.TotalCount;
            RemainingCount = snapshot.RemainingCount;
            DrawCount = Math.Clamp(DrawCount, 1, Math.Max(1, RemainingCount));
            RefreshNumberRemainingList(snapshot.Remaining);
        }
        else
        {
            var candidates = GetCandidates().ToList();
            TotalCount = candidates.Count;
            RemainingCount = GetEligibleCandidates(candidates).Count();
            DrawCount = Math.Clamp(DrawCount, 1, Math.Max(1, TotalCount));
            RefreshRemainingList(candidates);
        }

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
        if (IsNumberMode)
        {
            RefreshNumberRemainingList(
                _rollCallDrawService.GetNumberSnapshot(NumberRangeStart, NumberRangeEnd).Remaining);
            return;
        }

        RefreshRemainingList(null);
    }

    /// <summary>数字模式的剩余名单：每张票的显示文本就是数字本身，不带编号/名称格式。</summary>
    private void RefreshNumberRemainingList(IReadOnlyList<Student> remaining)
    {
        RemainingItems.Clear();
        foreach (var item in remaining
                     .OrderForList()
                     .Select(student => new RollCallRemainingItem(
                         student.Name,
                         student.Id,
                         student.Name,
                         student.Group,
                         student.Gender,
                         student.Tags)))
            RemainingItems.Add(item);
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
        if (Config.EffectiveRollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(listName);
    }

    private bool ResetForNewRoundIfExhausted()
    {
        if (TotalCount <= 0 || RemainingCount > 0)
            return false;

        // 数字模式没有名单临时记录，重置的是数字票那一份临时记录（与 ResetNumbers 同一口径）。
        if (IsNumberMode)
            _rollCallDrawService.ResetNumbers();
        else
            _temporaryRecordService.ResetStudentList(SelectedStudentListName);
        RefreshCounts();
        MainView.ShowSuccessToast(SR.M_AutoResetDone);
        return true;
    }

    /// <summary>
    ///     动画候选和剩余统计都读取共享档案；闪抽等其它会话可能已把共享档案切到别的名单，
    ///     抽取前必须先切回本页选中的名单，否则动画/候选会取自错误名单。
    /// </summary>
    private void EnsureActiveProfileMatchesSelection()
    {
        if (string.IsNullOrWhiteSpace(SelectedStudentListName)
            || string.Equals(_profileService.StudentListConfig?.Name, SelectedStudentListName, StringComparison.Ordinal))
            return;

        _profileService.LoadStudentProfile(SelectedStudentListName);
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
        var source = candidates ?? GetVisibleStudents();
        var threshold = DrawRepeatPolicy.ResolveThreshold(Config.EffectiveRollCallSettings.DrawMode, Config.EffectiveRollCallSettings.HalfRepeat);
        var counts = _temporaryRecordService.GetStudentCounts(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope);
        return DrawCandidateFilter.FilterEligibleStudents(source, CurrentGroupScope, CurrentGenderScope, counts, threshold);
    }

    private bool MatchesSelection(Student student) => DrawCandidateFilter.MatchesScope(student, CurrentGroupScope, CurrentGenderScope);

    private bool HasReachedRepeatLimit(Student student)
    {
        var threshold = DrawRepeatPolicy.ResolveThreshold(Config.EffectiveRollCallSettings.DrawMode, Config.EffectiveRollCallSettings.HalfRepeat);
        var counts = _temporaryRecordService.GetStudentCounts(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope);
        return DrawRepeatPolicy.HasReachedLimit(counts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(student)), threshold);
    }

    public void ResetForCourseLinkage()
    {
        ResetDrawHistoryCore();
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
            StudentImageSettings.StudentImagePosition,
            AvatarInitialResolver.Resolve(student.Name, student.Id),
            StudentImageSettings.StudentImageSize);
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
        // 数字票的 Id 与 Name 是同一个号码，套用"编号 + 名称"的显示格式会得到「7 7」这种结果，
        // 所以号码直接显示一次。这里按学生自身判定而不是记住上一次抽取的模式：切换名单后，
        // 上一次数字结果与剩余列表还要按各自的数据正确显示。
        var rawId = student.Id.Trim();
        var name = student.Name.Trim();
        if (rawId.Length > 0 && string.Equals(rawId, name, StringComparison.Ordinal) && int.TryParse(name, out _))
            return name;

        var id = FormatNumericId(student.Id, _studentIdPadWidth);
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
        if (Config.EffectiveRollCallSettings.DrawType != DrawType.Fair)
            return 1;

        // 显示的权重要和真正抽取时读到的是同一份历史：锁定记录组时只看该组的记录，
        // 否则结果卡片上的权重会按全局历史算，选中组的同学权重看起来完全没变。
        return _drawEngine.CalculateStudentWeight(
                GetVisibleStudents().ToList(),
                courseName: _linkageDrawCoordinator.GetCourseName(),
                historyGroup: CurrentHistoryGroup)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Candidate, student))
            ?.Weight ?? 1;
    }

    private double BuildResultOpacity(double weight)
    {
        if (!DisplaySettings.ShowWeightTransparency || Config.EffectiveRollCallSettings.DrawType != DrawType.Fair)
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

        return DrawImageCache.Load(settings.ImagePath);
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
    StudentImagePositionMode ImagePosition,
    string Initial,
    int ImageSize)
{
    public bool IsImageVisible => IsImageEnabled && Image is not null;
    public bool IsPlaceholderVisible => IsImageEnabled && Image is null;
    public double InitialFontSize => ImageSize * 0.5;
    public Orientation ImageLayoutOrientation => ImagePosition is StudentImagePositionMode.Left or StudentImagePositionMode.Right
        ? Orientation.Horizontal
        : Orientation.Vertical;
    public bool IsImageBeforeText => IsImageEnabled && ImagePosition is StudentImagePositionMode.Left or StudentImagePositionMode.Top;
    public bool IsImageAfterText => IsImageEnabled && ImagePosition is StudentImagePositionMode.Right or StudentImagePositionMode.Bottom;
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
