using System.Collections.ObjectModel;
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
    private readonly MobileRollCallService _rollCallService;
    private readonly ILotterySession _lotterySession;
    private readonly MobileDrawMediaService _drawMedia;
    private readonly IMobileSettingsNavigator _settingsNavigator;
    private readonly IMobileCapabilities _capabilities;
    private MobileRollCallSnapshot? _rollCallSnapshot;
    private IReadOnlyList<Prize> _lotteryCandidates = [];
    private IReadOnlyList<Student> _studentResult = [];
    private Prize? _prizeResult;
    private string? _resultStatusText;
    private string? _resultStatusDetail;
    private bool _synchronizingSurface;

    [ObservableProperty] private int _selectedSurface;
    [ObservableProperty] private string _selectedStudentList = string.Empty;
    [ObservableProperty] private string _selectedGroup = LR.O_All;
    [ObservableProperty] private string _selectedGender = LR.O_All;
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private bool _isDrawing;
    [ObservableProperty] private string _rollCallSummary = string.Empty;
    [ObservableProperty] private string _prizePoolName = string.Empty;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private string _resultDetail = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _studentLists = [];
    [ObservableProperty] private IReadOnlyList<string> _groupOptions = [];
    [ObservableProperty] private IReadOnlyList<string> _genderOptions = [];

    public MobileDrawPageViewModel(
        MainConfigHandler configHandler,
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        MobileRollCallService rollCallService,
        ILotterySession lotterySession,
        MobileDrawMediaService drawMedia,
        IMobileSettingsNavigator settingsNavigator,
        IMobileCapabilities capabilities)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _rollCallService = rollCallService;
        _lotterySession = lotterySession;
        _drawMedia = drawMedia;
        _settingsNavigator = settingsNavigator;
        _capabilities = capabilities;

        EnsureRestartTemporaryRecordsCleared();
        RefreshSurface();
    }

    public ObservableCollection<MobileDrawResultImage> ResultImages { get; } = [];

    public bool IsLotteryEnabled => _capabilities.IsLotteryEnabled;
    public bool IsRollCallSurface => SelectedSurface == (int)DrawSurface.RollCall;
    public bool IsLotterySurface => SelectedSurface == (int)DrawSurface.Lottery;
    public bool CanChangeSurface => !IsDrawing;
    public bool CanDecreaseCount => !IsDrawing && DrawCount > 1;
    public bool CanIncreaseCount => !IsDrawing && DrawCount < (_rollCallSnapshot?.RemainingCount ?? 0);
    public bool CanDrawStudents => !IsDrawing && (_rollCallSnapshot?.RemainingCount ?? 0) > 0;
    public bool CanDrawPrize => !IsDrawing && _lotteryCandidates.Count > 0;
    public bool HasResultImages => ResultImages.Count > 0;
    public string DrawStudentsText => IsDrawing ? LR.M_Drawing : LR.C_StartDraw;
    public string DrawPrizeText => IsDrawing ? LR.M_Drawing : LR.C_DrawPrize;

    public event EventHandler<MobileDrawAnimationRequest>? AnimationRequested;
    public event EventHandler<MobileDrawDialogRequest>? DialogRequested;

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
        var maximum = Math.Max(1, _rollCallSnapshot?.RemainingCount ?? 1);
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

        IsDrawing = true;
        _resultStatusText = null;
        _resultStatusDetail = null;
        ResultImages.Clear();
        RefreshSurface();
        await StopMediaSafelyAsync();
        IReadOnlyList<Student>? resultMediaStudents = null;
        var names = _rollCallSnapshot.Remaining.Select(FormatStudent).Where(name => name.Length > 0).ToArray();
        if (names.Length > 0)
            AnimationRequested?.Invoke(this, new MobileDrawAnimationRequest(names, false));

        try
        {
            var output = _rollCallService.Draw(DrawCount, GroupScope, GenderScope);
            if (output.IsSuccess && output.Result.Count > 0)
                await StartStudentAnimationMediaAsync(output.Result[0]);
            await Task.Delay(RollDurationMs);
            if (!output.IsSuccess || output.Result.Count == 0)
            {
                _studentResult = [];
                _resultStatusText = GetDrawFailureText(output.Status);
                _resultStatusDetail = string.Empty;
            }
            else
            {
                _studentResult = output.Result;
                resultMediaStudents = output.Result;
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
        if (IsDrawing || !IsLotteryEnabled)
            return;

        IsDrawing = true;
        _resultStatusText = null;
        _resultStatusDetail = null;
        ResultImages.Clear();
        RefreshSurface();
        await StopMediaSafelyAsync();
        Prize? resultMediaPrize = null;
        var names = _lotteryCandidates.Select(FormatPrize).Where(name => name.Length > 0).ToArray();
        if (names.Length > 0)
            AnimationRequested?.Invoke(this, new MobileDrawAnimationRequest(names, false));

        try
        {
            var output = _lotterySession.DrawOnce();
            if (output.IsSuccess && output.Result.Count > 0)
                await StartPrizeAnimationMediaAsync(output.Result[0]);
            await Task.Delay(RollDurationMs);
            if (!output.IsSuccess || output.Result.Count == 0)
            {
                _prizeResult = null;
                _resultStatusText = GetDrawFailureText(output.Status);
                _resultStatusDetail = string.Empty;
            }
            else
            {
                _prizeResult = output.Result[0];
                resultMediaPrize = _prizeResult;
            }
        }
        catch
        {
            _prizeResult = null;
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
        new MobileDrawDialogRequest(MobileDrawDialogKind.Remaining, _rollCallSnapshot?.Remaining ?? []));

    [RelayCommand]
    private void ShowMore() => DialogRequested?.Invoke(this, new MobileDrawDialogRequest(MobileDrawDialogKind.More));

    public void ResetScope()
    {
        _rollCallService.ClearTemporaryRecords(GroupScope, GenderScope);
        ClearResult();
        RefreshSurface();
    }

    public void ClearTemporaryRecords()
    {
        _temporaryRecordService.ClearStudentList(GetStudentListName());
        _temporaryRecordService.ClearPrizeList(GetPrizeListName());
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
            StudentLists = _rollCallService.GetListNames();
            var currentList = GetStudentListName();
            if (!string.Equals(SelectedStudentList, currentList, StringComparison.Ordinal))
                SelectedStudentList = currentList;
            GroupOptions = new[] { LR.O_All }.Concat(_rollCallService.GetGroups()).ToArray();
            GenderOptions = new[] { LR.O_All }.Concat(_rollCallService.GetGenders()).ToArray();
            _lotteryCandidates = _lotterySession.GetEligiblePrizes().ToArray();
            RollCallSummary = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                LR.M_CountSummary, snapshot.TotalCount, snapshot.RemainingCount);
            PrizePoolName = _profileService.PrizeListConfig?.Name ?? LR.M_DefaultPool;
            UpdateResult(snapshot);
            NotifyStateChanged();
        }
        finally
        {
            _synchronizingSurface = false;
        }
    }

    private void UpdateResult(MobileRollCallSnapshot snapshot)
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

        ResultText = _resultStatusText ?? (_prizeResult is not null ? FormatPrize(_prizeResult)
            : _lotteryCandidates.Count > 0 ? LR.M_Ready
            : hasPrizes ? LR.M_NoEligibleCandidates : LR.M_NoPrizes);
        ResultDetail = _resultStatusDetail ?? (_prizeResult is not null
            ? string.IsNullOrWhiteSpace(_prizeResult.Id) ? LR.M_DrawCompleted
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_PrizeId, _prizeResult.Id)
            : _lotteryCandidates.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidatePrizes, _lotteryCandidates.Count)
                : hasPrizes ? LR.M_RepeatLimitExhausted : LR.M_AddPrizesPrompt);
        UpdateResultImages(_configHandler.Data.LotterySettings.LotteryImage && _prizeResult is not null ? [_prizeResult] : []);
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
        _prizeResult = null;
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
    }

    private static string FormatStudent(Student student) => string.IsNullOrWhiteSpace(student.Id)
        ? student.Name
        : string.IsNullOrWhiteSpace(student.Name) ? student.Id : $"{student.Id}  {student.Name}";

    private static string FormatPrize(Prize prize) => string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;
    private string GroupScope => string.Equals(SelectedGroup, LR.O_All, StringComparison.Ordinal) ? string.Empty : SelectedGroup;
    private string GenderScope => string.Equals(SelectedGender, LR.O_All, StringComparison.Ordinal) ? string.Empty : SelectedGender;
    private string GetStudentListName() => _profileService.StudentListConfig?.Name ?? MobileDefaults.ProfileName;
    private string GetPrizeListName() => _profileService.PrizeListConfig?.Name ?? MobileDefaults.ProfileName;

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
    More
}

public sealed record MobileDrawDialogRequest(
    MobileDrawDialogKind Kind,
    IReadOnlyList<Student>? Remaining = null);
