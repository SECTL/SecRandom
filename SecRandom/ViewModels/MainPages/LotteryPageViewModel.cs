using System;
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
using SecRandom.Services;
using SecRandom.ViewModels;
using SecRandom.Views;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;
using SR = SecRandom.Langs.MainPages.Lottery.Resources;

namespace SecRandom.ViewModels.MainPages;

public sealed partial class LotteryPageViewModel : ViewModelBase, IDisposable
{
    private static string NoStudentOption => SR.O_NoStudentAssignment;
    private static string AllGroupsOption => SR.O_AllGroups;
    private static string AllGendersOption => SR.O_AllGenders;

    private readonly DrawEngine _drawEngine;
    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly IDrawCommitService _drawCommitService;
    private readonly MainConfigHandler _configHandler;
    private readonly DrawAudioService _drawAudioService;
    private readonly IVoiceAnnouncementService? _voiceAnnouncementService;
    private readonly ILogger<LotteryPageViewModel> _logger;
    private readonly ISecurityService _securityService;
    private readonly LinkageDrawCoordinator _linkageDrawCoordinator;
    private readonly VerificationDrawCoordinator _verificationDrawCoordinator;
    private readonly LotteryDrawService _lotteryDrawService;
    private readonly NotificationService? _notificationService;
    private readonly IFeatureAvailabilityService _featureAvailability;
    private readonly FileSystemWatcher? _prizeListWatcher;
    private readonly FileSystemWatcher? _studentListWatcher;
    private bool _isDrawCommandRunning;
    private bool _isRefreshingLists;
    private bool _isPrizeListRefreshQueued;
    private bool _isStudentListRefreshQueued;

    [ObservableProperty] private string _selectedPrizeListName = string.Empty;
    [ObservableProperty] private string _selectedStudentListName = NoStudentOption;
    [ObservableProperty] private string _selectedGroup = AllGroupsOption;
    [ObservableProperty] private string _selectedGender = AllGendersOption;
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private string _statusText = SR.M_Ready;
    [ObservableProperty] private bool _isResultVisible;
    [ObservableProperty] private int _previewAnimationRevision;
    [ObservableProperty] private int _resultAnimationRevision;
    [ObservableProperty] private bool _isDrawing;
    private List<LotteryDisplayPrize> _lastResultPrizes = [];
    private CancellationTokenSource? _previewCts;

    public LotteryPageViewModel(
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        IDrawCommitService drawCommitService,
        DrawAudioService drawAudioService,
        ILogger<LotteryPageViewModel> logger,
        ISecurityService securityService,
        LinkageDrawCoordinator linkageDrawCoordinator,
        VerificationDrawCoordinator verificationDrawCoordinator,
        IFeatureAvailabilityService featureAvailability,
        LotteryDrawService lotteryDrawService,
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
        _voiceAnnouncementService = voiceAnnouncementService;
        _logger = logger;
        _securityService = securityService;
        _linkageDrawCoordinator = linkageDrawCoordinator;
        _verificationDrawCoordinator = verificationDrawCoordinator;
        _lotteryDrawService = lotteryDrawService;
        _featureAvailability = featureAvailability;
        _notificationService = notificationService;
        if (App.IsDesktop && !OperatingSystem.IsIOS())
        {
            _prizeListWatcher = CreatePrizeListWatcher();
            _studentListWatcher = CreateStudentListWatcher();
        }
        PrizeListNames.CollectionChanged += PrizeListNamesOnCollectionChanged;
        StudentListNames.CollectionChanged += StudentListNamesOnCollectionChanged;
        Config.LotterySettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.DefaultDrawSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.MoreSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.Appearance.PropertyChanged += SettingsOnPropertyChanged;
        RefreshPrizeLists();
        RefreshStudentLists();
        RefreshCounts();
    }

    public ObservableCollection<string> PrizeListNames { get; } = [];
    public ObservableCollection<string> StudentListNames { get; } = [NoStudentOption];
    public ObservableCollection<string> GroupOptions { get; } = [AllGroupsOption];
    public ObservableCollection<string> GenderOptions { get; } = [AllGendersOption];
    public ObservableCollection<LotteryResultItem> ResultItems { get; } = [];
    public ObservableCollection<LotteryRemainingItem> RemainingItems { get; } = [];
    public MoreSettingsConfig MoreSettings => Config.MoreSettings;
    public bool IsControlPanelOnLeft => MoreSettings.LotteryControlPanelPosition == RollCallControlPanelPosition.Left;
    public bool IsControlPanelOnRight => !IsControlPanelOnLeft;
    public bool IsStudentAssignmentEnabled => SelectedStudentListName != NoStudentOption;
    public bool IsGroupSelectorVisible => MoreSettings.LotteryRangeSelector && IsStudentAssignmentEnabled;
    public bool IsGenderSelectorVisible => MoreSettings.LotteryGenderSelector && IsStudentAssignmentEnabled;
    public bool CanStartDraw => IsDrawing || (!_isDrawCommandRunning && TotalCount > 0);
    public string DrawButtonText => IsDrawing ? SR.C_Stop : SR.C_Start;
    public bool CanDecreaseCount => DrawCount > 1;
    public bool CanIncreaseCount => DrawCount < MaximumDrawCount;
    public int TotalCount { get; private set; }
    public int RemainingCount { get; private set; }
    public int MaximumDrawCount => Math.Max(1, RemainingCount > 0 ? RemainingCount : TotalCount);
    public string CountSummary => string.Format(SR.M_CountSummaryFormat, TotalCount, RemainingCount);
    public string ReminderText => DisplaySettings.ReminderText;
    public double ReminderFontSize => DisplaySettings.ReminderFontSize;
    public IBrush ReminderBrush => BuildReminderBrush();
    public double ResultFontSize => DisplaySettings.FontSize;
    public FontFamily ResultFontFamily => BuildResultFontFamily();
    public bool AnimationEnabled => AnimationSettings.Animation != AnimationMode.NoAnimation;
    public DrawAnimationStyleMode AnimationStyle => AnimationSettings.AnimationStyle;
    public int AnimationDuration => 250;
    public int PreviewAnimationDuration => AnimationSettings.Animation == AnimationMode.AutoPlay
        ? Math.Clamp(AnimationSettings.AnimationInterval, 1, 10000)
        : 80;

    private DrawSettingsConfigBase DisplaySettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Display);
    private DrawSettingsConfigBase AnimationSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Animation);
    private DrawSettingsConfigBase ColorSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Color);
    private DrawSettingsConfigBase MusicSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Music);
    private DrawSettingsConfigBase VoiceAnnouncementSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.VoiceAnnouncement);
    private bool IsLotteryImageEnabled => Config.LotterySettings.OverrideStudentImageSettings
        ? Config.LotterySettings.LotteryImage
        : Config.DefaultDrawSettings.StudentImage;
    private StudentImagePositionMode LotteryImagePosition => Config.LotterySettings.OverrideStudentImageSettings
        ? Config.LotterySettings.LotteryImagePosition
        : Config.DefaultDrawSettings.StudentImagePosition;
    private string CurrentGroupScope => SelectedGroup == AllGroupsOption ? string.Empty : SelectedGroup;
    private string CurrentGenderScope => SelectedGender == AllGendersOption ? string.Empty : SelectedGender;

    partial void OnSelectedPrizeListNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _profileService.LoadPrizeProfile(value);
        EnsureRestartPrizeRecordsCleared(value);
        Config.LotterySettings.DefaultPool = value;
        _configHandler.Save();
        RefreshCounts();
    }

    partial void OnSelectedStudentListNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != NoStudentOption)
        {
            _profileService.LoadStudentProfile(value);
            EnsureRestartStudentRecordsCleared(value);
        }

        RefreshFilterOptions();
        OnPropertyChanged(nameof(IsStudentAssignmentEnabled));
        OnPropertyChanged(nameof(IsGroupSelectorVisible));
        OnPropertyChanged(nameof(IsGenderSelectorVisible));
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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task StartDrawAsync()
    {
        if (!_featureAvailability.IsLotteryEnabled)
            return;

        if (IsDrawing)
        {
            StopPreview();
            return;
        }

        if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.LotteryStart, () => Task.CompletedTask) ||
            !_featureAvailability.IsLotteryEnabled)
            return;
        await StartDrawCoreAsync();
    }

    private async Task StartDrawCoreAsync()
    {
        if (IsDrawing)
            return;

        RefreshCounts();
        ResetExhaustedTemporaryRecords();
        if (!CanStartDraw)
        {
            StatusText = TotalCount == 0 ? SR.M_NoPrizes : SR.M_NoRemainingPrizes;
            return;
        }

        var prizes = (_profileService.CurrentPrizeList?.Prizes ?? []).Where(p => p.IsCandidate).ToList();
        if (prizes.Count == 0)
        {
            StatusText = SR.M_NoPrizes;
            return;
        }

        var count = Math.Clamp(DrawCount, 1, Math.Max(1, RemainingCount > 0 ? RemainingCount : prizes.Count));
        SetDrawCommandRunning(true);
        try
        {
            if (_notificationService is not null)
                await _notificationService.BeginClassIslandLotteryAnimationAsync(
                    SelectedPrizeListName,
                    prizes,
                    count);

            var courseName = _linkageDrawCoordinator.GetCourseName();
            var drawTask = _lotteryDrawService.DrawAsync(new LotteryDrawRequest(
                SelectedPrizeListName,
                IsStudentAssignmentEnabled ? SelectedStudentListName : string.Empty,
                CurrentGroupScope,
                CurrentGenderScope,
                count,
                courseName));
            var previewTask = ShowPreviewAsync(prizes, count, MusicSettings.AnimationMusic);
            List<Prize> drawn;
            List<Student> assignedStudents;
            try
            {
                var drawCompletedFirst = await Task.WhenAny(drawTask, previewTask).ConfigureAwait(true) == drawTask;
                var drawResult = await drawTask.ConfigureAwait(true);
                if (drawResult is null)
                    throw new InvalidOperationException("No eligible lottery candidates.");
                drawn = drawResult.Prizes.ToList();
                assignedStudents = drawResult.AssignedStudents.ToList();
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
                _logger.LogWarning(exception, "可验证奖品抽取失败。");
                StopPreview();
                _lastResultPrizes.Clear();
                ResultItems.Clear();
                IsResultVisible = false;
                StatusText = SR.M_DrawFailed;
                return;
            }

            _lastResultPrizes = BuildDisplayPrizes(drawn, assignedStudents);
            ReplaceResults(_lastResultPrizes);
            IsResultVisible = true;
            TriggerResultAnimation();
            StatusText = string.Format(SR.M_DrawnCountFormat, ResultItems.Count);
            RefreshCounts();
            ResetExhaustedTemporaryRecords();
            await _drawAudioService.TransitionToResultMusicAsync(
                DrawMusicAttachedSettingsResolver.GetResultMusic(drawn.FirstOrDefault(), MusicSettings.ResultMusic),
                MusicSettings.ResultMusicVolume, MusicSettings.ResultMusicFadeIn, MusicSettings.ResultMusicFadeOut,
                MusicSettings.AnimationMusicFadeOut).ConfigureAwait(false);
            if (_notificationService is not null)
                _notificationService.QueueLottery(SelectedPrizeListName, drawn, assignedStudents);
            if (_voiceAnnouncementService is not null && VoiceAnnouncementSettings.VoiceAnnouncementEnabled)
                await _voiceAnnouncementService.SpeakPrizesAsync(drawn).ConfigureAwait(false);
        }
        finally
        {
            SetDrawCommandRunning(false);
        }
    }


    [RelayCommand]
    private async Task ResetDisplayAsync()
    {
        if (!_featureAvailability.IsLotteryEnabled ||
            !await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.LotteryReset, () => Task.CompletedTask) ||
            !_featureAvailability.IsLotteryEnabled)
            return;
        ResetDisplayCore(showToast: true);
    }

    private void ResetDisplayCore(bool showToast = false)
    {
        _lastResultPrizes.Clear();
        ResultItems.Clear();
        _lotteryDrawService.Reset(
            IsStudentAssignmentEnabled ? SelectedStudentListName : string.Empty,
            CurrentGroupScope,
            CurrentGenderScope);
        IsResultVisible = false;
        StatusText = SR.M_ResetDone;
        if (showToast)
            MainView.ShowSuccessToast(SR.M_ResetDone);
        RefreshCounts();
    }

    public async Task<bool> StartProtocolDrawAsync(bool protectLinkage = false)
    {
        if (!_featureAvailability.IsLotteryEnabled)
            return false;

        var started = false;
        var authorized = await _linkageDrawCoordinator.AuthorizeAsync(
            protectLinkage ? [SecurityOperation.LotteryStart, SecurityOperation.LinkageAction] : [SecurityOperation.LotteryStart],
            () =>
            {
                if (!_featureAvailability.IsLotteryEnabled)
                    return Task.CompletedTask;

                started = true;
                _ = StartDrawCoreAsync();
                return Task.CompletedTask;
            });
        return authorized && started;
    }

    public Task ToggleDrawFromShortcutAsync() => StartDrawAsync();

    public async Task<bool> ResetProtocolDrawAsync(bool protectLinkage = false)
    {
        if (!_featureAvailability.IsLotteryEnabled)
            return false;

        var reset = false;
        var authorized = await _linkageDrawCoordinator.AuthorizeAsync(
            protectLinkage ? [SecurityOperation.LotteryReset, SecurityOperation.LinkageAction] : [SecurityOperation.LotteryReset],
            () =>
            {
                if (!_featureAvailability.IsLotteryEnabled)
                    return Task.CompletedTask;

                reset = true;
                ResetDisplayCore(showToast: true);
                return Task.CompletedTask;
            });
        return authorized && reset;
    }

    public void StopProtocolDraw()
    {
        if (_featureAvailability.IsLotteryEnabled)
            StopPreview();
    }

    public void RefreshPrizeLists()
    {
        if (_isRefreshingLists)
            return;

        _isRefreshingLists = true;
        try
        {
            var previousName = SelectedPrizeListName;
            PrizeListNames.Clear();
            foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "lottery_list"), "*.json")
                         .OrderBy(Path.GetFileName))
                PrizeListNames.Add(Path.GetFileNameWithoutExtension(file));

            if (PrizeListNames.Count == 0)
            {
                var config = new PrizeListConfig("default");
                config.Save();
                PrizeListNames.Add(config.Name);
            }

            var defaultPool = Config.LotterySettings.DefaultPool;
            var currentName = _profileService.PrizeListConfig?.Name ?? string.Empty;
            SelectedPrizeListName = PrizeListNames.Contains(previousName)
                ? previousName
                : PrizeListNames.Contains(defaultPool)
                    ? defaultPool
                    : PrizeListNames.Contains(currentName)
                        ? currentName
                        : PrizeListNames.FirstOrDefault() ?? string.Empty;
        }
        finally
        {
            _isRefreshingLists = false;
        }
    }

    public void RefreshStudentLists()
    {
        var previousName = SelectedStudentListName;
        StudentListNames.Clear();
        StudentListNames.Add(NoStudentOption);

        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "roll_call_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            StudentListNames.Add(Path.GetFileNameWithoutExtension(file));

        SelectedStudentListName = StudentListNames.Contains(previousName) ? previousName : NoStudentOption;
        RefreshFilterOptions();
    }

    /// <summary>Refreshes the shared draw session after profile mutations made by settings or import flows.</summary>
    public void RefreshAfterProfileChange()
    {
        RefreshPrizeLists();
        RefreshStudentLists();
        RefreshCounts();
    }

    private void PrizeListNamesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PrizeListNames));
    }

    private void StudentListNamesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StudentListNames));
    }

    private FileSystemWatcher CreatePrizeListWatcher()
    {
        var watcher = new FileSystemWatcher(Utils.GetDirectoryPath("list", "lottery_list"), "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        watcher.Created += PrizeListFiles_OnChanged;
        watcher.Deleted += PrizeListFiles_OnChanged;
        watcher.Renamed += PrizeListFiles_OnChanged;
        watcher.Changed += PrizeListFiles_OnChanged;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void PrizeListFiles_OnChanged(object? sender, FileSystemEventArgs e)
    {
        QueuePrizeListRefresh();
    }

    private void QueuePrizeListRefresh()
    {
        if (_isPrizeListRefreshQueued)
            return;

        _isPrizeListRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isPrizeListRefreshQueued = false;
            RefreshPrizeLists();
            RefreshCounts();
        });
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
            RefreshStudentLists();
            RefreshCounts();
        });
    }

    public void RefreshRemainingList()
    {
        RefreshRemainingList(null);
    }

    private void RefreshCounts()
    {
        var prizes = GetCurrentPrizes().ToList();
        TotalCount = Config.LotterySettings.DrawType == LotteryDrawType.Count
            ? prizes.Sum(prize => Math.Max(0, prize.Count))
            : prizes.Count;
        RemainingCount = CalculateRemainingCount(prizes);
        DrawCount = Math.Clamp(DrawCount, 1, MaximumDrawCount);
        RefreshRemainingList(prizes);

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(MaximumDrawCount));
        OnPropertyChanged(nameof(CountSummary));
        OnPropertyChanged(nameof(CanStartDraw));
        OnPropertyChanged(nameof(CanDecreaseCount));
        OnPropertyChanged(nameof(CanIncreaseCount));
    }

    private void RefreshRemainingList(IEnumerable<Prize>? source)
    {
        var prizes = (source ?? GetCurrentPrizes()).ToList();
        var temporaryCounts = _temporaryRecordService.GetPrizeCounts(SelectedPrizeListName);
        RemainingItems.Clear();
        foreach (var item in prizes
                      .OrderForList()
                      .Select(prize => CreateRemainingItem(prize, temporaryCounts)))
            RemainingItems.Add(item);
    }

    private IEnumerable<Prize> GetCurrentPrizes()
    {
        return (_profileService.CurrentPrizeList?.Prizes ?? []).Where(prize => prize.IsCandidate);
    }

    private int CalculateRemainingCount(IReadOnlyCollection<Prize> prizes)
    {
        var temporaryCounts = _temporaryRecordService.GetPrizeCounts(SelectedPrizeListName);
        if (Config.LotterySettings.DrawType == LotteryDrawType.Count)
            return prizes.Sum(prize => Math.Max(0, prize.Count - GetTemporaryPrizeCount(prize, temporaryCounts)));

        var threshold = DrawRepeatPolicy.ResolveThreshold(Config.LotterySettings.DrawMode, Config.LotterySettings.HalfRepeat);

        return threshold <= 0
            ? prizes.Count
            : prizes.Count(prize => GetTemporaryPrizeCount(prize, temporaryCounts) < threshold);
    }

    private LotteryRemainingItem CreateRemainingItem(Prize prize, IReadOnlyDictionary<string, int> temporaryCounts)
    {
        var drawn = GetTemporaryPrizeCount(prize, temporaryCounts);
        var remaining = Config.LotterySettings.DrawType == LotteryDrawType.Count
            ? Math.Max(0, prize.Count - drawn)
            : Config.LotterySettings.DrawMode == DrawMode.Repeat
                ? Math.Max(1, drawn + 1)
                : Math.Max(0, GetLotteryRepeatThreshold() - drawn);
        return new LotteryRemainingItem(FormatPrize(prize), prize.Id, prize.Name, prize.Tags, remaining, drawn);
    }

    private static int GetTemporaryPrizeCount(Prize prize, IReadOnlyDictionary<string, int> temporaryCounts)
    {
        return temporaryCounts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(prize));
    }

    private int GetLotteryRepeatThreshold()
    {
        // 展示口径：Repeat 视为无限剩余，对应抽取阈值 helper 的 0（不限制）。
        var threshold = DrawRepeatPolicy.ResolveThreshold(Config.LotterySettings.DrawMode, Config.LotterySettings.HalfRepeat);
        return threshold <= 0 ? int.MaxValue : threshold;
    }

    private async Task ShowPreviewAsync(IReadOnlyList<Prize> prizes, int count, string animationMusic)
    {
        if (Config.LotterySettings.LotteryShowRandom < 0 || AnimationSettings.Animation == AnimationMode.NoAnimation)
            return;

        await _drawAudioService.StartAnimationMusicAsync(
            animationMusic,
            MusicSettings.AnimationMusicVolume,
            MusicSettings.AnimationMusicFadeIn, MusicSettings.AnimationMusicLoop).ConfigureAwait(true);

        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        var token = previewCts.Token;
        var isManualStop = AnimationSettings.Animation == AnimationMode.ManualStop;
        var iterations = AnimationSettings.Animation == AnimationMode.AutoPlay
            ? Math.Clamp(AnimationSettings.AutoplayCount, 1, 999)
            : 1;
        var delay = PreviewAnimationDuration;

        IsDrawing = true;
        try
        {
            for (var i = 0; isManualStop ? !token.IsCancellationRequested : i < iterations; i++)
            {
                ReplaceResults(BuildDisplayPrizes(
                    prizes.OrderBy(_ => Random.Shared.Next()).Take(count).ToList(),
                    GetRandomAssignedStudents(count)));
                IsResultVisible = true;
                TriggerPreviewAnimation();
                StatusText = SR.M_Drawing;
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

    private void ReplaceResults(IReadOnlyList<LotteryDisplayPrize> prizes)
    {
        ResultItems.Clear();
        foreach (var prize in prizes)
            ResultItems.Add(CreateResultItem(prize));
    }

    private void TriggerResultAnimation()
    {
        ResultAnimationRevision++;
    }

    private LotteryResultItem CreateResultItem(LotteryDisplayPrize item)
    {
        var prize = item.Prize;
        var accentBrush = ResolveAccentBrush();
        return new LotteryResultItem(
            item.DisplayText,
            item.Tags,
            DisplaySettings.ShowTags && !string.IsNullOrWhiteSpace(item.Tags),
            DisplaySettings.DisplayStyle == DisplayStyleMode.Card,
            accentBrush,
            DrawColorHelper.ResolveTextBrush(accentBrush, Config.Appearance.Theme),
            DisplaySettings.ShowWeightTransparency,
            $"权重 {prize.Weight:0.##}",
            BuildImage(prize),
            IsLotteryImageEnabled,
            LotteryImagePosition,
            BuildInitial(prize));
    }

    private List<LotteryDisplayPrize> BuildDisplayPrizes(IReadOnlyList<Prize> prizes, IReadOnlyList<Student> assignedStudents)
    {
        List<LotteryDisplayPrize> result = [];
        for (var i = 0; i < prizes.Count; i++)
        {
            var prize = prizes[i];
            var student = i < assignedStudents.Count ? assignedStudents[i] : null;
            result.Add(student is null
                ? new LotteryDisplayPrize(prize, FormatPrize(prize), prize.Tags)
                : new LotteryDisplayPrize(prize, FormatAssignedPrize(prize, student), prize.Tags));
        }

        return result;
    }

    private async Task<List<Student>?> DrawAssignedStudentsAsync(int count, Guid? parentProofId, string courseName)
    {
        if (!IsStudentAssignmentEnabled)
            return [];

        var candidates = GetStudentCandidates().ToList();
        if (count <= 0)
            return [];

        if (candidates.Count == 0)
        {
            StatusText = SR.M_NoStudents;
            return null;
        }

        if (count > candidates.Count)
        {
            StatusText = SR.M_NoRemainingStudents;
            return null;
        }

        try
        {
            return (await _verificationDrawCoordinator.DrawStudentsAsync(
                count,
                candidates,
                DrawSettingsType.RollCall,
                DrawProofExportContext.ForStudents(SelectedStudentListName, CurrentGroupScope, CurrentGenderScope, courseName),
                parentProofId,
                courseName).ConfigureAwait(true)).Winners.ToList();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "可验证获奖学生分配失败。");
            StatusText = SR.M_DrawFailed;
            return null;
        }
    }

    private List<Student> GetRandomAssignedStudents(int count)
    {
        var candidates = GetStudentCandidates().ToList();
        if (count <= 0 || candidates.Count == 0)
            return [];

        return candidates.OrderBy(_ => Random.Shared.Next()).Take(Math.Min(count, candidates.Count)).ToList();
    }

    private IEnumerable<Student> GetStudentCandidates()
    {
        if (!IsStudentAssignmentEnabled)
            return [];

        var threshold = DrawRepeatPolicy.ResolveThreshold(Config.RollCallSettings.DrawMode, Config.RollCallSettings.HalfRepeat);
        var counts = _temporaryRecordService.GetStudentCounts(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope);
        return DrawCandidateFilter.FilterEligibleStudents(
            _profileService.CurrentStudentList?.Students ?? [],
            CurrentGroupScope,
            CurrentGenderScope,
            counts,
            threshold);
    }

    private IEnumerable<Student> GetStudentPoolCandidates()
    {
        if (!IsStudentAssignmentEnabled)
            return [];

        return (_profileService.CurrentStudentList?.Students ?? [])
            .Where(student => student.IsCandidate)
            .Where(student => DrawCandidateFilter.MatchesScope(student, CurrentGroupScope, CurrentGenderScope));
    }

    private void EnsureRestartPrizeRecordsCleared(string listName)
    {
        if (Config.LotterySettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearPrizeListOnce(listName);
    }

    private void EnsureRestartStudentRecordsCleared(string listName)
    {
        if (Config.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(listName);
    }

    private bool ResetExhaustedTemporaryRecords()
    {
        if (TotalCount <= 0)
            return false;

        var reset = false;
        if (RemainingCount <= 0)
        {
            _temporaryRecordService.ResetPrizeList(SelectedPrizeListName);
            reset = true;
        }

        if (IsStudentAssignmentEnabled)
        {
            var studentPool = GetStudentPoolCandidates().ToList();
            if (studentPool.Count > 0 && !GetStudentCandidates().Any())
            {
                _temporaryRecordService.ResetStudentList(SelectedStudentListName);
                reset = true;
            }
        }

        if (!reset)
            return false;

        RefreshCounts();
        MainView.ShowSuccessToast(SR.M_AutoResetDone);
        return true;
    }

    private void RefreshFilterOptions()
    {
        var students = (_profileService.CurrentStudentList?.Students ?? [])
            .Where(student => student.IsCandidate)
            .ToList();

        ReplaceOptions(GroupOptions, AllGroupsOption, students.Select(student => student.Group));
        ReplaceOptions(GenderOptions, AllGendersOption, students.Select(student => student.Gender));

        if (!GroupOptions.Contains(SelectedGroup))
            SelectedGroup = AllGroupsOption;
        if (!GenderOptions.Contains(SelectedGender))
            SelectedGender = AllGendersOption;
    }

    private string FormatPrize(Prize prize)
    {
        return Config.LotterySettings.LotteryShowRandom switch
        {
            LotteryShowRandomMode.PrizeHyphenName => JoinInline(prize.Id, prize.Name),
            LotteryShowRandomMode.PrizeBreakName => JoinLines(prize.Id, prize.Name),
            LotteryShowRandomMode.PrizeHyphenGroupHyphenName => JoinInline(prize.Id, prize.Tags, prize.Name),
            LotteryShowRandomMode.PrizeBreakGroupHyphenName => JoinLines(prize.Id, JoinInline(prize.Tags, prize.Name)),
            LotteryShowRandomMode.PrizeBreakGroupBreakName => JoinLines(prize.Id, prize.Tags, prize.Name),
            LotteryShowRandomMode.PrizeBreakGroup => JoinLines(prize.Id, prize.Tags),
            LotteryShowRandomMode.PrizeHyphenGroup => JoinInline(prize.Id, prize.Tags),
            _ => JoinLines(prize.Id, prize.Name)
        };
    }

    private string FormatAssignedPrize(Prize prize, Student student)
    {
        var prizeText = string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;
        return Config.LotterySettings.LotteryShowRandom switch
        {
            LotteryShowRandomMode.PrizeHyphenName => JoinInline(prizeText, student.Name),
            LotteryShowRandomMode.PrizeBreakName => JoinLines(prizeText, student.Name),
            LotteryShowRandomMode.PrizeHyphenGroupHyphenName => JoinInline(prizeText, student.Group, student.Name),
            LotteryShowRandomMode.PrizeBreakGroupHyphenName => JoinLines(prizeText, JoinInline(student.Group, student.Name)),
            LotteryShowRandomMode.PrizeBreakGroupBreakName => JoinLines(prizeText, student.Group, student.Name),
            LotteryShowRandomMode.PrizeBreakGroup => JoinLines(prizeText, student.Group),
            LotteryShowRandomMode.PrizeHyphenGroup => JoinInline(prizeText, student.Group),
            _ => JoinLines(prizeText, student.Name)
        };
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Config.MoreSettings)
        {
            OnPropertyChanged(nameof(IsControlPanelOnLeft));
            OnPropertyChanged(nameof(IsControlPanelOnRight));
            OnPropertyChanged(nameof(IsGroupSelectorVisible));
            OnPropertyChanged(nameof(IsGenderSelectorVisible));
        }

        if (_lastResultPrizes.Count > 0)
            ReplaceResults(_lastResultPrizes);

        OnPropertyChanged(nameof(ResultFontSize));
        OnPropertyChanged(nameof(ResultFontFamily));
        OnPropertyChanged(nameof(ReminderText));
        OnPropertyChanged(nameof(ReminderFontSize));
        OnPropertyChanged(nameof(ReminderBrush));
        OnPropertyChanged(nameof(AnimationEnabled));
        OnPropertyChanged(nameof(AnimationStyle));
        OnPropertyChanged(nameof(AnimationDuration));
        RefreshCounts();
    }

    public void Dispose()
    {
        StopPreview();
        PrizeListNames.CollectionChanged -= PrizeListNamesOnCollectionChanged;
        StudentListNames.CollectionChanged -= StudentListNamesOnCollectionChanged;
        Config.LotterySettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.DefaultDrawSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.MoreSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.Appearance.PropertyChanged -= SettingsOnPropertyChanged;
        _prizeListWatcher?.Dispose();
        _studentListWatcher?.Dispose();
    }

    private IBrush BuildReminderBrush()
    {
        var color = DisplaySettings.ReminderTextColor;
        color = Color.FromArgb((byte)Math.Clamp(DisplaySettings.ReminderTextOpacity * 255 / 100, 0, 255),
            color.R, color.G, color.B);
        return new SolidColorBrush(color);
    }

    private static string JoinInline(params string[] values)
    {
        return string.Join(" - ", values.Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string JoinLines(params string[] values)
    {
        return string.Join("\n", values.Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)));
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

    private static Bitmap? BuildImage(Prize prize)
    {
        var settings = prize.GetAttachedObject<DrawImageAttachedSettings>(Guid.Parse(GlobalConstants.DrawImageAttachedSettings));
        if (settings is not { IsAttachSettingsEnabled: true } || string.IsNullOrWhiteSpace(settings.ImagePath))
            return null;

        try { return File.Exists(settings.ImagePath) ? new Bitmap(settings.ImagePath) : null; }
        catch { return null; }
    }

    private static string BuildInitial(Prize prize)
    {
        var text = string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;
        return string.IsNullOrWhiteSpace(text) ? "?" : text.Trim()[0].ToString();
    }

    private string ToStatusMessage(DrawStatus status)
    {
        _logger.LogWarning("Lottery draw failed with status {Status}.", status);
        return status switch
        {
            DrawStatus.NoCandidates => SR.M_NoCandidates,
            DrawStatus.NoEligibleCandidates => SR.M_NoEligibleCandidates,
            DrawStatus.RepeatLimitExhausted => SR.M_RepeatLimitExhausted,
            DrawStatus.InvalidWeight => SR.M_InvalidWeight,
            _ => SR.M_DrawFailed
        };
    }

    public void ResetForCourseLinkage()
    {
        ResetDisplayCore();
    }
}

public sealed record LotteryDisplayPrize(Prize Prize, string DisplayText, string Tags);

public sealed record LotteryResultItem(
    string DisplayText,
    string Tags,
    bool IsTagsVisible,
    bool IsCardStyle,
    IBrush? AccentBrush,
    IBrush TextBrush,
    bool IsWeightVisible,
    string WeightText,
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

public sealed record LotteryRemainingItem(string DisplayText, string Id, string Name, string Tags, int Remaining, int DrawnCount)
{
    public bool IsTagsVisible => !string.IsNullOrWhiteSpace(Tags);
    public string CountText => string.Format(SR.M_RemainingCountFormat, Remaining, DrawnCount);
}
