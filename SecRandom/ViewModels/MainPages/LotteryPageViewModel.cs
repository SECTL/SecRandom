using System;
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
    private readonly MainConfigHandler _configHandler;
    private readonly DrawAudioService _drawAudioService;
    private readonly IVoiceAnnouncementService? _voiceAnnouncementService;
    private readonly ILogger<LotteryPageViewModel> _logger;
    private readonly ISecurityService _securityService;
    private readonly LinkageDrawCoordinator _linkageDrawCoordinator;
    private readonly VerificationDrawCoordinator _verificationDrawCoordinator;
    private readonly NotificationService? _notificationService;
    private readonly FileSystemWatcher _prizeListWatcher;
    private readonly FileSystemWatcher _studentListWatcher;
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
        DrawAudioService drawAudioService,
        ILogger<LotteryPageViewModel> logger,
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
        _drawAudioService = drawAudioService;
        _voiceAnnouncementService = voiceAnnouncementService;
        _logger = logger;
        _securityService = securityService;
        _linkageDrawCoordinator = linkageDrawCoordinator;
        _verificationDrawCoordinator = verificationDrawCoordinator;
        _notificationService = notificationService;
        _prizeListWatcher = CreatePrizeListWatcher();
        _studentListWatcher = CreateStudentListWatcher();
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
    public bool CanStartDraw => IsDrawing || (!_isDrawCommandRunning && TotalCount > 0 && RemainingCount > 0);
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
        if (IsDrawing)
        {
            StopPreview();
            return;
        }

        if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.LotteryStart, () => Task.CompletedTask))
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
            StatusText = TotalCount == 0 ? SR.M_NoPrizes : SR.M_NoRemainingPrizes;
            return;
        }

        var prizes = (_profileService.CurrentPrizeList?.Prizes ?? []).Where(p => p.Exists).ToList();
        if (prizes.Count == 0)
        {
            StatusText = SR.M_NoPrizes;
            return;
        }

        var count = Math.Clamp(DrawCount, 1, Math.Max(1, RemainingCount > 0 ? RemainingCount : prizes.Count));
        SetDrawCommandRunning(true);
        try
        {
            var courseName = _linkageDrawCoordinator.GetCourseName();
            var verificationDrawTask = _verificationDrawCoordinator.DrawPrizesAsync(
                count,
                _temporaryRecordService.GetPrizeCounts(SelectedPrizeListName),
                prizes,
                cancellationToken: default);
            await ShowPreviewAsync(prizes, count).ConfigureAwait(true);

            List<Prize> drawn;
            Guid? prizeProofId;
            try
            {
                var proofOutcome = await verificationDrawTask.ConfigureAwait(true);
                drawn = proofOutcome.Winners.ToList();
                prizeProofId = proofOutcome.Proof.ProofId;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "可验证奖品抽取失败。");
                await _drawAudioService.StopAnimationMusicAsync(0, immediate: true).ConfigureAwait(false);
                _lastResultPrizes.Clear();
                ResultItems.Clear();
                IsResultVisible = false;
                StatusText = SR.M_DrawFailed;
                return;
            }

            var assignedStudents = await DrawAssignedStudentsAsync(drawn.Count, prizeProofId, courseName).ConfigureAwait(true);
            if (assignedStudents is null)
            {
                await _drawAudioService.StopAnimationMusicAsync(0, immediate: true).ConfigureAwait(false);
                return;
            }

            _profileService.RecordPrizeHistory(drawn, DateTime.Now, count);
            _temporaryRecordService.RecordPrizes(SelectedPrizeListName, drawn);
            if (assignedStudents.Count > 0)
            {
                _profileService.RecordStudentHistory(
                    assignedStudents,
                    DateTime.Now,
                    assignedStudents.Count,
                    SelectedGroup == AllGroupsOption ? string.Empty : SelectedGroup,
                    SelectedGender == AllGendersOption ? string.Empty : SelectedGender,
                    (int)Config.RollCallSettings.DrawType,
                    courseName: courseName);
                _temporaryRecordService.RecordStudents(
                    SelectedStudentListName,
                    CurrentGenderScope,
                    CurrentGroupScope,
                    assignedStudents);
            }

            _lastResultPrizes = BuildDisplayPrizes(drawn, assignedStudents);
            ReplaceResults(_lastResultPrizes);
            IsResultVisible = true;
            TriggerResultAnimation();
            StatusText = string.Format(SR.M_DrawnCountFormat, ResultItems.Count);
            RefreshCounts();
            await _drawAudioService.TransitionToResultMusicAsync(MusicSettings.ResultMusic,
                MusicSettings.ResultMusicVolume, MusicSettings.ResultMusicFadeIn, MusicSettings.ResultMusicFadeOut,
                MusicSettings.AnimationMusicFadeOut).ConfigureAwait(false);
            if (_notificationService is not null)
                await _notificationService.ShowAsync(
                    NotificationSettingsType.Lottery,
                    "抽奖结果",
                    ResultItems.Select(item => item.DisplayText).ToList());
            if (_voiceAnnouncementService is not null)
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
        if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.LotteryReset, () => Task.CompletedTask))
            return;
        ResetDisplayCore();
    }

    private void ResetDisplayCore()
    {
        _lastResultPrizes.Clear();
        ResultItems.Clear();
        _temporaryRecordService.ClearPrizeList(SelectedPrizeListName);
        if (IsStudentAssignmentEnabled)
            _temporaryRecordService.ClearStudentScope(SelectedStudentListName, CurrentGenderScope, CurrentGroupScope);
        IsResultVisible = false;
        StatusText = SR.M_ResetDone;
        RefreshCounts();
    }

    public Task<bool> StartProtocolDrawAsync(bool protectLinkage = false) => _linkageDrawCoordinator.AuthorizeAsync(
        protectLinkage ? [SecurityOperation.LotteryStart, SecurityOperation.LinkageAction] : [SecurityOperation.LotteryStart],
        () =>
        {
            _ = StartDrawCoreAsync();
            return Task.CompletedTask;
        });

    public Task ToggleDrawFromShortcutAsync() => StartDrawAsync();

    public Task<bool> ResetProtocolDrawAsync(bool protectLinkage = false) => _linkageDrawCoordinator.AuthorizeAsync(
        protectLinkage ? [SecurityOperation.LotteryReset, SecurityOperation.LinkageAction] : [SecurityOperation.LotteryReset],
        () =>
        {
            ResetDisplayCore();
            return Task.CompletedTask;
        });
    public void StopProtocolDraw() => StopPreview();

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
                     .OrderBy(prize => string.IsNullOrWhiteSpace(prize.Id))
                     .ThenBy(prize => int.TryParse(prize.Id, out _) ? 0 : 1)
                     .ThenBy(prize => int.TryParse(prize.Id, out var id) ? id : int.MaxValue)
                     .ThenBy(prize => string.IsNullOrWhiteSpace(prize.Id) ? prize.Name.Trim() : prize.Id.Trim(), StringComparer.CurrentCultureIgnoreCase)
                     .Select(prize => CreateRemainingItem(prize, temporaryCounts)))
            RemainingItems.Add(item);
    }

    private IEnumerable<Prize> GetCurrentPrizes()
    {
        return (_profileService.CurrentPrizeList?.Prizes ?? []).Where(prize => prize.Exists);
    }

    private int CalculateRemainingCount(IReadOnlyCollection<Prize> prizes)
    {
        var temporaryCounts = _temporaryRecordService.GetPrizeCounts(SelectedPrizeListName);
        if (Config.LotterySettings.DrawType == LotteryDrawType.Count)
            return prizes.Sum(prize => Math.Max(0, prize.Count - GetTemporaryPrizeCount(prize, temporaryCounts)));

        var threshold = Config.LotterySettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, Config.LotterySettings.HalfRepeat),
            _ => 1
        };

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
        return Config.LotterySettings.DrawMode switch
        {
            DrawMode.Repeat => int.MaxValue,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, Config.LotterySettings.HalfRepeat),
            _ => 1
        };
    }

    private async Task ShowPreviewAsync(IReadOnlyList<Prize> prizes, int count)
    {
        if (Config.LotterySettings.LotteryShowRandom < 0 || AnimationSettings.Animation == AnimationMode.NoAnimation)
            return;

        await _drawAudioService.StartAnimationMusicAsync(MusicSettings.AnimationMusic, MusicSettings.AnimationMusicVolume,
            MusicSettings.AnimationMusicFadeIn, Config.MoreSettings.BackgroundMusicLoop).ConfigureAwait(true);

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
            Config.LotterySettings.LotteryImage,
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

        return (_profileService.CurrentStudentList?.Students ?? [])
            .Where(student => student.IsCandidate)
            .Where(student => SelectedGroup == AllGroupsOption || student.Group == SelectedGroup)
            .Where(student => SelectedGender == AllGendersOption || student.Gender == SelectedGender)
            .Where(student => !HasReachedStudentRepeatLimit(student));
    }

    private bool HasReachedStudentRepeatLimit(Student student)
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
        return Config.LotterySettings.LotteryShowRandom switch
        {
            LotteryShowRandomMode.PrizeHyphenName => JoinInline(prize.Name, student.Name),
            LotteryShowRandomMode.PrizeBreakName => JoinLines(prize.Name, student.Name),
            LotteryShowRandomMode.PrizeHyphenGroupHyphenName => JoinInline(prize.Name, student.Group, student.Name),
            LotteryShowRandomMode.PrizeBreakGroupHyphenName => JoinLines(prize.Name, JoinInline(student.Group, student.Name)),
            LotteryShowRandomMode.PrizeBreakGroupBreakName => JoinLines(prize.Name, student.Group, student.Name),
            LotteryShowRandomMode.PrizeBreakGroup => JoinLines(prize.Name, student.Group),
            LotteryShowRandomMode.PrizeHyphenGroup => JoinInline(prize.Name, student.Group),
            _ => JoinLines(prize.Name, student.Name)
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
        _prizeListWatcher.Dispose();
        _studentListWatcher.Dispose();
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
    string Initial)
{
    public bool IsImageVisible => IsImageEnabled && Image is not null;
    public bool IsPlaceholderVisible => IsImageEnabled && Image is null;
}

public sealed record LotteryRemainingItem(string DisplayText, string Id, string Name, string Tags, int Remaining, int DrawnCount)
{
    public bool IsTagsVisible => !string.IsNullOrWhiteSpace(Tags);
    public string CountText => string.Format(SR.M_RemainingCountFormat, Remaining, DrawnCount);
}
