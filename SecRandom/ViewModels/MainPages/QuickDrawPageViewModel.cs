using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using SecRandom.Services.Draw;
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
    private readonly MainConfigHandler _configHandler;
    private readonly DrawAudioService _drawAudioService;
    private readonly ILogger<QuickDrawPageViewModel> _logger;
    private readonly ISecurityService _securityService;
    private readonly VerificationDrawCoordinator _verificationDrawCoordinator;
    private readonly NotificationService? _notificationService;
    private bool _isDrawCommandRunning;
    private bool _isCoolingDown;
    private CancellationTokenSource? _previewCts;

    [ObservableProperty] private string _selectedStudentListName = string.Empty;
    [ObservableProperty] private string _statusText = "准备闪抽";
    [ObservableProperty] private bool _isResultVisible;
    [ObservableProperty] private int _previewAnimationRevision;
    [ObservableProperty] private int _resultAnimationRevision;
    [ObservableProperty] private bool _isDrawing;
    [ObservableProperty] private Student? _lastDrawnStudent;

    public QuickDrawPageViewModel(
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        DrawAudioService drawAudioService,
        ILogger<QuickDrawPageViewModel> logger,
        ISecurityService securityService,
        VerificationDrawCoordinator verificationDrawCoordinator,
        NotificationService? notificationService = null)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _drawAudioService = drawAudioService;
        _logger = logger;
        _securityService = securityService;
        _verificationDrawCoordinator = verificationDrawCoordinator;
        _notificationService = notificationService;
        ResultItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ResultFontSize));
        RefreshStudentLists();
    }

    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<QuickDrawResultItem> ResultItems { get; } = [];
    public bool CanStartDraw => IsDrawing || (!_isDrawCommandRunning && !_isCoolingDown && GetEligibleCandidates().Any());
    public string DrawButtonText => IsDrawing ? "停止" : "闪抽";
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

        if (_isCoolingDown)
            return;

        if (!await _securityService.AuthorizeAsync(SecurityOperation.QuickDrawStart, () => Task.CompletedTask))
            return;
        await StartDrawCoreAsync();
    }

    private async Task StartDrawCoreAsync(bool skipPreview = false)
    {
        if (IsDrawing)
            return;

        if (_isCoolingDown)
            return;

        if (!TryLoadDefaultStudentList())
            return;

        var candidates = GetEligibleCandidates().ToList();
        if (candidates.Count == 0)
        {
            StatusText = "没有可抽取的学生";
            return;
        }

        const int count = 1;
        SetDrawCommandRunning(true);
        try
        {
            var verificationDrawTask = _verificationDrawCoordinator.DrawStudentsAsync(
                count,
                candidates,
                DrawSettingsType.QuickDraw,
                cancellationToken: default);
            if (!skipPreview)
                await ShowPreviewAsync(candidates, count).ConfigureAwait(true);

            List<Student> drawn;
            try
            {
                drawn = (await verificationDrawTask.ConfigureAwait(true)).Winners.ToList();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "可验证闪抽失败。");
                ResultItems.Clear();
                LastDrawnStudent = null;
                IsResultVisible = false;
                StatusText = "闪抽失败";
                return;
            }

            var weights = BuildWeightSnapshot(drawn);
            _profileService.RecordStudentHistory(drawn, DateTime.Now, count, drawMethod: (int)Config.QuickDrawSettings.DrawType, weights: weights);
            _temporaryRecordService.RecordStudents(SelectedStudentListName, string.Empty, string.Empty, drawn);
            LastDrawnStudent = drawn[0];
            ReplaceResults(drawn);
            IsResultVisible = true;
            TriggerResultAnimation();
            StatusText = $"已抽取 {ResultItems.Count} 人";
            if (_notificationService is not null)
                await _notificationService.ShowAsync(
                    NotificationSettingsType.QuickDraw,
                    "闪抽结果",
                    ResultItems.Select(item => item.DisplayText).ToList());
            await _drawAudioService.PlayAsync(MusicSettings.ResultMusic, MusicSettings.ResultMusicVolume,
                MusicSettings.ResultMusicFadeIn, MusicSettings.ResultMusicFadeOut).ConfigureAwait(false);

            StartCooldown();
        }
        finally
        {
            SetDrawCommandRunning(false);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (!await _securityService.AuthorizeAsync(SecurityOperation.QuickDrawReset, () => Task.CompletedTask))
            return;
        ClearHistoryCore();
    }

    private void ClearHistoryCore()
    {
        _temporaryRecordService.ClearStudentScope(SelectedStudentListName, string.Empty, string.Empty);
        ResultItems.Clear();
        LastDrawnStudent = null;
        IsResultVisible = false;
        StatusText = "已清空当前名单抽取记录";
        OnPropertyChanged(nameof(CanStartDraw));
    }

    public async Task StartProtocolDrawAsync()
    {
        LastDrawnStudent = null;
        await StartDrawCoreAsync(skipPreview: true);
    }

    public Task ResetProtocolDrawAsync()
    {
        ClearHistoryCore();
        return Task.CompletedTask;
    }

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
        var defaultClass = Config.QuickDrawSettings.DefaultClass.Trim();
        if (string.IsNullOrWhiteSpace(defaultClass))
        {
            StatusText = "请先在闪抽设置中选择默认抽取名单";
            return false;
        }

        if (!StudentListNames.Contains(defaultClass))
        {
            StatusText = "默认抽取名单不存在，请在闪抽设置中重新选择";
            return false;
        }

        if (SelectedStudentListName != defaultClass)
            SelectedStudentListName = defaultClass;

        return true;
    }

    private void EnsureRestartTemporaryRecordsCleared(string listName)
    {
        if (Config.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(listName);
    }

    private IEnumerable<Student> GetEligibleCandidates()
    {
        return (_profileService.CurrentStudentList?.Students ?? [])
            .Where(student => student.IsCandidate)
            .Where(student => !HasReachedRepeatLimit(student));
    }

    private bool HasReachedRepeatLimit(Student student)
    {
        var threshold = Config.QuickDrawSettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, Config.QuickDrawSettings.HalfRepeat),
            _ => 1
        };

        if (threshold <= 0)
            return false;

        var recordId = ProfileRecordIdentity.EnsureRecordId(student);
        var counts = _temporaryRecordService.GetStudentCounts(SelectedStudentListName, string.Empty, string.Empty);
        return counts.GetValueOrDefault(recordId) >= threshold;
    }

    private async Task ShowPreviewAsync(IReadOnlyList<Student> candidates, int count)
    {
        if (AnimationSettings.Animation == AnimationMode.NoAnimation)
            return;

        await _drawAudioService.PlayAsync(MusicSettings.AnimationMusic, MusicSettings.AnimationMusicVolume,
            MusicSettings.AnimationMusicFadeIn, MusicSettings.AnimationMusicFadeOut).ConfigureAwait(true);

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

    private Dictionary<Student, double> BuildWeightSnapshot(IReadOnlyCollection<Student> drawnStudents)
    {
        if (Config.QuickDrawSettings.DrawType != DrawType.Fair)
            return drawnStudents.ToDictionary(student => student, _ => 1.0);

        return _drawEngine.CalculateStudentWeight((_profileService.CurrentStudentList?.Students ?? []).Where(s => s.IsCandidate).ToList())
            .Where(candidate => drawnStudents.Contains(candidate.Candidate))
            .ToDictionary(candidate => candidate.Candidate, candidate => candidate.Weight);
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
            BuildInitial(student));
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

        return _drawEngine.CalculateStudentWeight((_profileService.CurrentStudentList?.Students ?? []).Where(s => s.IsCandidate).ToList())
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

    private static string BuildInitial(Student student)
    {
        var text = string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name;
        return string.IsNullOrWhiteSpace(text) ? "?" : text.Trim()[0].ToString();
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
    string Initial)
{
    public bool IsImageVisible => IsImageEnabled && Image is not null;
    public bool IsPlaceholderVisible => IsImageEnabled && Image is null;
}
