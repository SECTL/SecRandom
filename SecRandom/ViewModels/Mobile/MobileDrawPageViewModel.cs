using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Draw;
using SecRandom.Services.Linkage;
using SecRandom.Services.Mobile;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;
using SecRandom.Views.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.ViewModels.Mobile;

public sealed partial class MobileDrawPageViewModel : ViewModelBase
{
    private const int RollDurationMs = 800;

    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly MainConfigHandler _configHandler;
    private readonly RollCallDrawService _rollCallService;
    private readonly LotteryDrawService _lotteryService;
    private readonly LinkageDrawCoordinator _linkageDrawCoordinator;
    private readonly MobileDrawMediaService _drawMedia;
    private readonly IMobileSettingsNavigator _settingsNavigator;
    private readonly IMobileCapabilities _capabilities;
    private RollCallDrawSnapshot? _rollCallSnapshot;
    private LotteryDrawSnapshot? _lotterySnapshot;
    private IReadOnlyList<Student> _studentResult = [];
    private IReadOnlyList<Prize> _prizeResults = [];
    private IReadOnlyList<Student> _lotteryAssignedStudents = [];
    private string? _resultStatusText;
    private string? _resultStatusDetail;
    private bool _synchronizingSurface;

    [ObservableProperty] private int _selectedSurface;
    [ObservableProperty] private string _selectedStudentList = string.Empty;
    [ObservableProperty] private string _selectedPrizePool = string.Empty;
    [ObservableProperty] private string? _selectedLotteryStudentList;
    [ObservableProperty] private string _selectedGroup = LR.O_All;
    [ObservableProperty] private string _selectedGender = LR.O_All;
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private bool _isDrawing;
    [ObservableProperty] private string _rollCallSummary = string.Empty;
    [ObservableProperty] private string _prizePoolName = string.Empty;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private string _resultDetail = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _studentLists = [];
    [ObservableProperty] private IReadOnlyList<string> _prizePools = [];
    [ObservableProperty] private IReadOnlyList<string> _lotteryStudentLists = [];
    [ObservableProperty] private IReadOnlyList<string> _groupOptions = [];
    [ObservableProperty] private IReadOnlyList<string> _genderOptions = [];

    public MobileDrawPageViewModel(
        MainConfigHandler configHandler,
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        RollCallDrawService rollCallService,
        LotteryDrawService lotteryService,
        LinkageDrawCoordinator linkageDrawCoordinator,
        MobileDrawMediaService drawMedia,
        IMobileSettingsNavigator settingsNavigator,
        IMobileCapabilities capabilities)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _rollCallService = rollCallService;
        _lotteryService = lotteryService;
        _linkageDrawCoordinator = linkageDrawCoordinator;
        _drawMedia = drawMedia;
        _settingsNavigator = settingsNavigator;
        _capabilities = capabilities;

        EnsureRestartTemporaryRecordsCleared();
        RefreshSurface();

        Config.MoreSettings.PropertyChanged += MoreSettingsOnPropertyChanged;
    }

    public ObservableCollection<MobileDrawResultImage> ResultImages { get; } = [];

    public bool IsLotteryEnabled => _capabilities.IsLotteryEnabled;
    public bool IsRollCallSurface => SelectedSurface == (int)DrawSurface.RollCall;
    public bool IsLotterySurface => SelectedSurface == (int)DrawSurface.Lottery;
    public bool CanChangeSurface => !IsDrawing;
    public bool CanDecreaseCount => !IsDrawing && DrawCount > 1;
    public bool CanIncreaseCount => !IsDrawing && DrawCount < CurrentRemainingCount;
    public bool CanDrawStudents => !IsDrawing && (_rollCallSnapshot?.RemainingCount ?? 0) > 0;
    public bool CanDrawPrize => !IsDrawing && !string.IsNullOrWhiteSpace(SelectedLotteryStudentList) &&
                                (_lotterySnapshot?.RemainingCount ?? 0) > 0 &&
                                (_lotterySnapshot?.EligibleStudents.Count ?? 0) >= DrawCount;
    public bool HasResultImages => ResultImages.Count > 0;
    public string DrawStudentsText => IsDrawing ? LR.M_Drawing : LR.C_StartDraw;
    public string DrawPrizeText => IsDrawing ? LR.M_Drawing : LR.C_DrawPrize;
    public bool RollCallPositionLeft =>
        Config.MoreSettings.RollCallControlPanelPosition == RollCallControlPanelPosition.Left;
    public bool LotteryPositionLeft =>
        Config.MoreSettings.LotteryControlPanelPosition == RollCallControlPanelPosition.Left;
    public int CurrentRemainingCount => IsLotterySurface
        ? _lotterySnapshot?.RemainingCount ?? 0
        : _rollCallSnapshot?.RemainingCount ?? 0;

    public event EventHandler<MobileDrawAnimationRequest>? AnimationRequested;
    public event EventHandler<MobileDrawDialogRequest>? DialogRequested;

    private void MoreSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RollCallPositionLeft));
        OnPropertyChanged(nameof(LotteryPositionLeft));
    }

    partial void OnSelectedSurfaceChanged(int value)
    {
        if (_synchronizingSurface)
            return;

        if (value == (int)DrawSurface.Lottery && !IsLotteryEnabled)
        {
            SelectedSurface = (int)DrawSurface.RollCall;
            return;
        }

        ClearResult();
        RefreshSurface();
    }

    partial void OnSelectedStudentListChanged(string value)
    {
        if (_synchronizingSurface || IsDrawing || string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, GetStudentListName(), StringComparison.Ordinal))
            return;

        _rollCallService.SwitchList(value);
        SelectedGroup = LR.O_All;
        SelectedGender = LR.O_All;
        DrawCount = 1;
        ClearResult();
        RefreshSurface();
    }

    partial void OnSelectedPrizePoolChanged(string value)
    {
        if (_synchronizingSurface || IsDrawing || string.IsNullOrWhiteSpace(value))
            return;
        _lotteryService.SwitchPrizePool(value);
        DrawCount = 1;
        ClearResult();
        RefreshSurface();
    }

    partial void OnSelectedLotteryStudentListChanged(string? value)
    {
        if (_synchronizingSurface || IsDrawing)
            return;
        if (!string.IsNullOrWhiteSpace(value))
            _lotteryService.SwitchStudentList(value);
        SelectedGroup = LR.O_All;
        SelectedGender = LR.O_All;
        DrawCount = 1;
        ClearResult();
        RefreshSurface();
    }

    partial void OnSelectedGroupChanged(string value)
    {
        if (_synchronizingSurface || IsDrawing)
            return;

        DrawCount = 1;
        ClearResult();
        RefreshSurface();
    }

    partial void OnSelectedGenderChanged(string value)
    {
        if (_synchronizingSurface || IsDrawing)
            return;

        DrawCount = 1;
        ClearResult();
        RefreshSurface();
    }

    partial void OnDrawCountChanged(int value)
    {
        var maximum = Math.Max(1, CurrentRemainingCount);
        var normalized = Math.Clamp(value, 1, maximum);
        if (normalized != value)
            DrawCount = normalized;
        NotifyStateChanged();
    }

    partial void OnIsDrawingChanged(bool value) => NotifyStateChanged();

    [RelayCommand]
    private void IncreaseCount() => DrawCount++;

    [RelayCommand]
    private void DecreaseCount() => DrawCount--;

    [RelayCommand]
    private async Task DrawStudentsAsync()
    {
        if (IsDrawing || _rollCallSnapshot is null)
            return;

        if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.RollCallStart, () => Task.CompletedTask))
            return;

        IsDrawing = true;
        _resultStatusText = null;
        _resultStatusDetail = null;
        ResultImages.Clear();
        NotifyStateChanged();
        await StopMediaSafelyAsync();
        IReadOnlyList<Student>? resultMediaStudents = null;
        var names = _rollCallSnapshot.Remaining.Select(FormatStudent).Where(name => name.Length > 0).ToArray();
        if (names.Length > 0)
            AnimationRequested?.Invoke(this, new MobileDrawAnimationRequest(names, false));

        try
        {
            var output = await _rollCallService.DrawAsync(new RollCallDrawRequest(
                GetStudentListName(), GroupScope, GenderScope, DrawCount, _linkageDrawCoordinator.GetCourseName()));
            if (output is not null && output.Students.Count > 0)
                await StartStudentAnimationMediaAsync(output.Students[0]);
            await Task.Delay(RollDurationMs);
            if (output is null || output.Students.Count == 0)
            {
                _studentResult = [];
                _resultStatusText = LR.M_NoEligibleCandidates;
                _resultStatusDetail = string.Empty;
            }
            else
            {
                _studentResult = output.Students;
                resultMediaStudents = output.Students;
            }
        }
        catch
        {
            _studentResult = [];
            _resultStatusText = LR.M_DrawFailed;
            _resultStatusDetail = string.Empty;
            await StopMediaSafelyAsync();
        }
        finally
        {
            IsDrawing = false;
            AnimationRequested?.Invoke(this, new MobileDrawAnimationRequest([], true));
        }

        RefreshSurface();
        AnimationRequested?.Invoke(this, new MobileDrawAnimationRequest([], false, true));
        if (resultMediaStudents is not null)
            await PlayStudentResultMediaAsync(resultMediaStudents);
    }

    [RelayCommand]
    private async Task DrawPrizeAsync()
    {
        if (IsDrawing || !IsLotteryEnabled || !CanDrawPrize)
            return;

        IsDrawing = true;
        _resultStatusText = null;
        _resultStatusDetail = null;
        ResultImages.Clear();
        NotifyStateChanged();
        await StopMediaSafelyAsync();
        Prize? resultMediaPrize = null;
        var names = BuildLotteryPreviewNames();
        if (names.Count > 0)
            AnimationRequested?.Invoke(this, new MobileDrawAnimationRequest(names, false));

        try
        {
            if (!await _linkageDrawCoordinator.AuthorizeAsync(SecurityOperation.LotteryStart, () => Task.CompletedTask))
                return;
            var output = await _lotteryService.DrawAsync(new LotteryDrawRequest(
                GetPrizePoolName(), SelectedLotteryStudentList ?? string.Empty, GroupScope, GenderScope, DrawCount,
                _linkageDrawCoordinator.GetCourseName()));
            if (output is not null && output.Prizes.Count > 0)
                await StartPrizeAnimationMediaAsync(output.Prizes[0]);
            await Task.Delay(RollDurationMs);
            if (output is null || output.Prizes.Count == 0)
            {
                _prizeResults = [];
                _lotteryAssignedStudents = [];
                _resultStatusText = LR.M_NoEligibleCandidates;
                _resultStatusDetail = string.Empty;
            }
            else
            {
                _prizeResults = output.Prizes;
                _lotteryAssignedStudents = output.AssignedStudents;
                resultMediaPrize = output.Prizes[0];
            }
        }
        catch
        {
            _prizeResults = [];
            _lotteryAssignedStudents = [];
            _resultStatusText = LR.M_DrawFailed;
            _resultStatusDetail = string.Empty;
            await StopMediaSafelyAsync();
        }
        finally
        {
            IsDrawing = false;
            AnimationRequested?.Invoke(this, new MobileDrawAnimationRequest([], true));
        }

        RefreshSurface();
        AnimationRequested?.Invoke(this, new MobileDrawAnimationRequest([], false, true));
        if (resultMediaPrize is not null)
            await PlayPrizeResultMediaAsync(resultMediaPrize);
    }

    [RelayCommand]
    private void ShowRemaining() => DialogRequested?.Invoke(this,
        IsLotterySurface
            ? new MobileDrawDialogRequest(MobileDrawDialogKind.RemainingPrizes, null, _lotterySnapshot?.Remaining ?? [])
            : new MobileDrawDialogRequest(MobileDrawDialogKind.Remaining, _rollCallSnapshot?.Remaining ?? []));

    [RelayCommand]
    public void ResetScope()
    {
        if (IsLotterySurface)
            _lotteryService.Reset(SelectedLotteryStudentList ?? string.Empty, GroupScope, GenderScope);
        else
            _rollCallService.Reset(GroupScope, GenderScope);
        ClearResult();
        RefreshSurface();
    }

    [RelayCommand]
    public void ClearTemporaryRecords()
    {
        _rollCallService.Reset(string.Empty, string.Empty);
        _lotteryService.Reset(SelectedLotteryStudentList ?? string.Empty, string.Empty, string.Empty);
        ClearResult();
        RefreshSurface();
    }

    [RelayCommand]
    private Task OpenListManagementAsync() => _settingsNavigator.NavigateAsync(MobilePageIds.ListManagement);

    [RelayCommand]
    private Task OpenDrawSettingsAsync() => _settingsNavigator.NavigateAsync(MobilePageIds.DrawSettings);

    private void RefreshSurface()
    {
        if (_synchronizingSurface)
            return;

        _synchronizingSurface = true;
        try
        {
            if (SelectedSurface == (int)DrawSurface.Lottery && !IsLotteryEnabled)
                SelectedSurface = (int)DrawSurface.RollCall;

            var snapshot = _rollCallService.GetSnapshot(GroupScope, GenderScope);
            _rollCallSnapshot = snapshot;
            DrawCount = Math.Clamp(DrawCount, 1, Math.Max(1, snapshot.RemainingCount));
            StudentLists = snapshot.ListNames;
            var currentList = GetStudentListName();
            if (!string.Equals(SelectedStudentList, currentList, StringComparison.Ordinal))
                SelectedStudentList = currentList;
            GroupOptions = new[] { LR.O_All }.Concat(snapshot.Groups).ToArray();
            GenderOptions = new[] { LR.O_All }.Concat(snapshot.Genders).ToArray();
            _lotterySnapshot = _lotteryService.GetSnapshot(SelectedLotteryStudentList ?? string.Empty, GroupScope, GenderScope);
            PrizePools = _lotterySnapshot.PrizePoolNames;
            LotteryStudentLists = _lotterySnapshot.StudentListNames;
            var currentPrizePool = GetPrizePoolName();
            if (!string.Equals(SelectedPrizePool, currentPrizePool, StringComparison.Ordinal))
                SelectedPrizePool = currentPrizePool;
            if (!string.IsNullOrWhiteSpace(SelectedLotteryStudentList) &&
                !LotteryStudentLists.Contains(SelectedLotteryStudentList))
                SelectedLotteryStudentList = null;
            DrawCount = Math.Clamp(DrawCount, 1, Math.Max(1, CurrentRemainingCount));
            RollCallSummary = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                LR.M_CountSummary, snapshot.TotalCount, snapshot.RemainingCount);
            PrizePoolName = currentPrizePool;
            UpdateResult(snapshot);
            NotifyStateChanged();
        }
        finally
        {
            _synchronizingSurface = false;
        }
    }

    private void UpdateResult(RollCallDrawSnapshot snapshot)
    {
        var hasStudents = _profileService.CurrentStudentList?.Students.Any(student => student.IsCandidate) ?? false;
        var hasPrizes = _profileService.CurrentPrizeList?.Prizes.Any(prize => prize.IsCandidate) ?? false;
        if (IsRollCallSurface)
        {
            ResultText = _resultStatusText ?? (_studentResult.Count > 0
                ? string.Join("\n", _studentResult.Select(FormatStudent))
                : snapshot.RemainingCount > 0 ? LR.M_Ready
                : hasStudents ? LR.M_NoEligibleCandidates : LR.M_NoStudents);
            ResultDetail = _resultStatusDetail ?? (_studentResult.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_DrawResultCount, _studentResult.Count)
                : snapshot.RemainingCount > 0
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidateStudents, snapshot.RemainingCount)
                    : hasStudents ? LR.M_RepeatLimitExhausted : LR.M_AddStudentsPrompt);
            UpdateResultImages(ShouldShowStudentImages() ? _studentResult : []);
            return;
        }

        ResultText = _resultStatusText ?? (_prizeResults.Count > 0 ? string.Join("\n",
                _prizeResults.Select((prize, index) => FormatLotteryResult(prize,
                    index < _lotteryAssignedStudents.Count ? _lotteryAssignedStudents[index] : null)))
            : (_lotterySnapshot?.RemainingCount ?? 0) > 0 ? LR.M_Ready
            : hasPrizes ? LR.M_NoEligibleCandidates : LR.M_NoPrizes);
        ResultDetail = _resultStatusDetail ?? (_prizeResults.Count > 0
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_DrawResultCount, _prizeResults.Count)
            : (_lotterySnapshot?.RemainingCount ?? 0) > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidatePrizes, _lotterySnapshot!.RemainingCount)
                : hasPrizes ? LR.M_RepeatLimitExhausted : LR.M_AddPrizesPrompt);
        UpdateResultImages(_configHandler.Data.LotterySettings.LotteryImage ? _prizeResults : []);
    }

    private bool ShouldShowStudentImages() => Config.GetOverrideDrawSettings(
        DrawSettingsType.RollCall, OverridableDrawSettingsType.StudentImage).StudentImage;

    private void UpdateResultImages(IEnumerable<IAttachableSettingsObject> records)
    {
        ResultImages.Clear();
        foreach (var record in records)
        {
            var settings = record.GetAttachedObject<DrawImageAttachedSettings>(
                Guid.Parse(GlobalConstants.DrawImageAttachedSettings));
            if (settings is not { IsAttachSettingsEnabled: true } || string.IsNullOrWhiteSpace(settings.ImagePath))
                continue;
            try
            {
                if (File.Exists(settings.ImagePath))
                    ResultImages.Add(new MobileDrawResultImage(new Bitmap(settings.ImagePath)));
            }
            catch
            {
                // Result images are optional decoration.
            }
        }
    }

    private async Task PlayStudentResultMediaAsync(IReadOnlyList<Student> students)
    {
        try
        {
            var music = Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.Music);
            await _drawMedia.PlayResultAsync(students.FirstOrDefault(), music);
            var voice = Config.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.VoiceAnnouncement);
            if (Config.VoiceSettings.VoiceEnable && voice.VoiceAnnouncementEnabled)
                await _drawMedia.SpeakStudentsAsync(students, Config.VoiceSettings.AnnounceId, Config.VoiceSettings.AnnounceName,
                    Config.VoiceSettings.VolumeSize, Config.VoiceSettings.SpeechRate);
        }
        catch { }
    }

    private async Task StartStudentAnimationMediaAsync(Student student)
    {
        try
        {
            await _drawMedia.StartAnimationAsync(student, Config.GetOverrideDrawSettings(
                DrawSettingsType.RollCall, OverridableDrawSettingsType.Music));
        }
        catch { }
    }

    private async Task PlayPrizeResultMediaAsync(Prize prize)
    {
        try
        {
            var music = Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Music);
            await _drawMedia.PlayResultAsync(prize, music);
            var voice = Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.VoiceAnnouncement);
            if (Config.VoiceSettings.VoiceEnable && voice.VoiceAnnouncementEnabled)
                await _drawMedia.SpeakPrizesAsync([prize], Config.VoiceSettings.AnnounceId, Config.VoiceSettings.AnnounceName,
                    Config.VoiceSettings.VolumeSize, Config.VoiceSettings.SpeechRate);
        }
        catch { }
    }

    private async Task StartPrizeAnimationMediaAsync(Prize prize)
    {
        try
        {
            await _drawMedia.StartAnimationAsync(prize, Config.GetOverrideDrawSettings(
                DrawSettingsType.Lottery, OverridableDrawSettingsType.Music));
        }
        catch { }
    }

    private async Task StopMediaSafelyAsync()
    {
        try { await _drawMedia.StopAsync(); }
        catch { }
    }

    private void ClearResult()
    {
        _studentResult = [];
        _prizeResults = [];
        _lotteryAssignedStudents = [];
        _resultStatusText = null;
        _resultStatusDetail = null;
        ResultImages.Clear();
    }

    private void EnsureRestartTemporaryRecordsCleared()
    {
        if (Config.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(GetStudentListName());
        if (Config.LotterySettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearPrizeListOnce(GetPrizeListName());
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsLotteryEnabled));
        OnPropertyChanged(nameof(IsRollCallSurface));
        OnPropertyChanged(nameof(IsLotterySurface));
        OnPropertyChanged(nameof(CanChangeSurface));
        OnPropertyChanged(nameof(CanDecreaseCount));
        OnPropertyChanged(nameof(CanIncreaseCount));
        OnPropertyChanged(nameof(CanDrawStudents));
        OnPropertyChanged(nameof(CanDrawPrize));
        OnPropertyChanged(nameof(HasResultImages));
        OnPropertyChanged(nameof(DrawStudentsText));
        OnPropertyChanged(nameof(DrawPrizeText));
        OnPropertyChanged(nameof(CurrentRemainingCount));
    }

    private static string FormatStudent(Student student) => string.IsNullOrWhiteSpace(student.Id)
        ? student.Name
        : string.IsNullOrWhiteSpace(student.Name) ? student.Id : $"{student.Id}  {student.Name}";

    private static string FormatPrize(Prize prize) => string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;

    private IReadOnlyList<string> BuildLotteryPreviewNames()
    {
        if (_lotterySnapshot is not { Remaining.Count: > 0, EligibleStudents.Count: > 0 })
            return [];

        return _lotterySnapshot.Remaining.SelectMany(prize => _lotterySnapshot.EligibleStudents
            .Select(student => FormatLotteryResult(prize, student))).ToArray();
    }

    private static string FormatLotteryResult(Prize prize, Student? student)
    {
        var prizeText = FormatPrize(prize);
        return student is null ? prizeText : $"{prizeText}  -  {FormatStudent(student)}";
    }
    private string GroupScope => string.Equals(SelectedGroup, LR.O_All, StringComparison.Ordinal) ? string.Empty : SelectedGroup;
    private string GenderScope => string.Equals(SelectedGender, LR.O_All, StringComparison.Ordinal) ? string.Empty : SelectedGender;
    private string GetStudentListName() => _profileService.StudentListConfig?.Name ?? MobileDefaults.ProfileName;
    private string GetPrizeListName() => _profileService.PrizeListConfig?.Name ?? MobileDefaults.ProfileName;
    private string GetPrizePoolName() => _profileService.PrizeListConfig?.Name ?? MobileDefaults.ProfileName;

    private static string GetDrawFailureText(DrawStatus status) => status switch
    {
        DrawStatus.NoCandidates => LR.M_NoCandidates,
        DrawStatus.NoEligibleCandidates => LR.M_NoEligibleCandidates,
        DrawStatus.RepeatLimitExhausted => LR.M_RepeatLimitExhausted,
        DrawStatus.InvalidWeight => LR.M_InvalidWeight,
        _ => LR.M_DrawFailed
    };
}

public sealed record MobileDrawResultImage(Bitmap Source);

public sealed record MobileDrawAnimationRequest(IReadOnlyList<string> Names, bool Stop, bool Reveal = false);

public enum MobileDrawDialogKind
{
    Remaining,
    RemainingPrizes
}

public sealed record MobileDrawDialogRequest(
    MobileDrawDialogKind Kind,
    IReadOnlyList<Student>? Remaining = null,
    IReadOnlyList<Prize>? RemainingPrizes = null);
