using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
using SecRandom.ViewModels;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.ViewModels.MainPages;

public sealed partial class LotteryPageViewModel : ViewModelBase, IDisposable
{
    private readonly DrawEngine _drawEngine;
    private readonly IProfileService _profileService;
    private readonly MainConfigHandler _configHandler;
    private readonly DrawAudioService _drawAudioService;
    private readonly ILogger<LotteryPageViewModel> _logger;

    [ObservableProperty] private string _selectedPrizeListName = string.Empty;
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private string _statusText = "准备抽奖";
    [ObservableProperty] private bool _isResultVisible;
    [ObservableProperty] private int _previewAnimationRevision;
    [ObservableProperty] private int _resultAnimationRevision;
    private List<Prize> _lastResultPrizes = [];

    public LotteryPageViewModel(
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        IProfileService profileService,
        DrawAudioService drawAudioService,
        ILogger<LotteryPageViewModel> logger)
        : base(configHandler)
    {
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _profileService = profileService;
        _drawAudioService = drawAudioService;
        _logger = logger;
        Config.LotterySettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.DefaultDrawSettings.PropertyChanged += SettingsOnPropertyChanged;
        Config.Appearance.PropertyChanged += SettingsOnPropertyChanged;
        RefreshPrizeLists();
        RefreshCounts();
    }

    public ObservableCollection<string> PrizeListNames { get; } = [];
    public ObservableCollection<LotteryResultItem> ResultItems { get; } = [];
    public ObservableCollection<LotteryRemainingItem> RemainingItems { get; } = [];
    public bool CanStartDraw => TotalCount > 0 && RemainingCount > 0;
    public bool CanDecreaseCount => DrawCount > 1;
    public bool CanIncreaseCount => DrawCount < MaximumDrawCount;
    public int TotalCount { get; private set; }
    public int RemainingCount { get; private set; }
    public int MaximumDrawCount => Math.Max(1, RemainingCount > 0 ? RemainingCount : TotalCount);
    public string CountSummary => $"总数 {TotalCount} / 剩余 {RemainingCount}";
    public string ReminderText => DisplaySettings.ReminderText;
    public double ReminderFontSize => DisplaySettings.ReminderFontSize;
    public IBrush ReminderBrush => BuildReminderBrush();
    public double ResultFontSize => DisplaySettings.FontSize;
    public FontFamily ResultFontFamily => BuildResultFontFamily();
    public bool AnimationEnabled => AnimationSettings.AnimationEnabled;
    public DrawAnimationStyleMode AnimationStyle => AnimationSettings.AnimationStyle;
    public int AnimationDuration => Math.Clamp(AnimationSettings.AnimationDuration, 80, 10000);
    public int PreviewAnimationDuration => Math.Clamp(AnimationSettings.AnimationInterval, 1, 10000);

    private DrawSettingsConfigBase DisplaySettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Display);
    private DrawSettingsConfigBase AnimationSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Animation);
    private DrawSettingsConfigBase ColorSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Color);
    private DrawSettingsConfigBase MusicSettings =>
        Config.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.Music);

    partial void OnSelectedPrizeListNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _profileService.LoadPrizeProfile(value);
        Config.LotterySettings.DefaultPool = value;
        _configHandler.Save();
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
    private async Task StartDrawAsync()
    {
        if (!CanStartDraw)
        {
            StatusText = TotalCount == 0 ? "没有可抽取的奖品" : "没有剩余奖品";
            return;
        }

        var prizes = (_profileService.CurrentPrizeList?.Prizes ?? []).Where(p => p.Exists).ToList();
        var count = Math.Clamp(DrawCount, 1, Math.Max(1, RemainingCount > 0 ? RemainingCount : prizes.Count));
        await ShowPreviewAsync(prizes, count).ConfigureAwait(true);

        var result = _drawEngine.DrawPrize(count, _ => true);
        if (!result.IsSuccess)
        {
            StatusText = "抽奖失败：" + result.Status;
            return;
        }

        var drawn = result.Result.ToList();
        _profileService.RecordPrizeHistory(drawn, DateTime.Now, count);
        _lastResultPrizes = drawn;
        ReplaceResults(drawn);
        IsResultVisible = true;
        TriggerResultAnimation();
        StatusText = $"已抽取 {ResultItems.Count} 个奖品";
        RefreshCounts();
        await _drawAudioService.PlayAsync(MusicSettings.ResultMusic, MusicSettings.ResultMusicVolume,
            MusicSettings.ResultMusicFadeIn, MusicSettings.ResultMusicFadeOut).ConfigureAwait(false);
    }

    [RelayCommand]
    private void ResetDisplay()
    {
        _lastResultPrizes.Clear();
        ResultItems.Clear();
        IsResultVisible = false;
        StatusText = "准备抽奖";
        RefreshCounts();
    }

    private void RefreshPrizeLists()
    {
        PrizeListNames.Clear();
        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("list", "lottery_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            PrizeListNames.Add(Path.GetFileNameWithoutExtension(file));

        var defaultPool = Config.LotterySettings.DefaultPool;
        SelectedPrizeListName = PrizeListNames.Contains(defaultPool)
            ? defaultPool
            : PrizeListNames.FirstOrDefault() ?? string.Empty;
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
        var historyCache = BuildPrizeHistoryCache(prizes);
        RemainingItems.Clear();
        foreach (var item in prizes.Select(prize => CreateRemainingItem(prize, historyCache)))
            RemainingItems.Add(item);
    }

    private IEnumerable<Prize> GetCurrentPrizes()
    {
        return (_profileService.CurrentPrizeList?.Prizes ?? []).Where(prize => prize.Exists);
    }

    private int CalculateRemainingCount(IReadOnlyCollection<Prize> prizes)
    {
        var historyCache = BuildPrizeHistoryCache(prizes);
        if (Config.LotterySettings.DrawType == LotteryDrawType.Count)
            return prizes.Sum(prize => Math.Max(0, prize.Count - (historyCache.GetValueOrDefault(prize)?.TotalCount ?? 0)));

        var threshold = Config.LotterySettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, Config.LotterySettings.HalfRepeat),
            _ => 1
        };

        return threshold <= 0
            ? prizes.Count
            : prizes.Count(prize => (historyCache.GetValueOrDefault(prize)?.TotalCount ?? 0) < threshold);
    }

    private Dictionary<Prize, History> BuildPrizeHistoryCache(IEnumerable<Prize> prizes)
    {
        var history = _profileService.CurrentPrizeHistory;
        if (history is null)
            return [];

        var prizeList = prizes.ToList();
        var uniqueLegacyKeys = ProfileRecordIdentity.BuildUniquePrizeLegacyKeySet(prizeList);
        Dictionary<Prize, History> result = [];
        foreach (var prize in prizeList)
        {
            var item = ProfileRecordIdentity.GetPrizeHistory(history, prize, uniqueLegacyKeys.Contains);
            if (item is not null)
                result[prize] = item;
        }

        return result;
    }

    private LotteryRemainingItem CreateRemainingItem(Prize prize, IReadOnlyDictionary<Prize, History> historyCache)
    {
        var history = historyCache.GetValueOrDefault(prize);
        var drawn = history?.TotalCount ?? 0;
        var remaining = Config.LotterySettings.DrawType == LotteryDrawType.Count
            ? Math.Max(0, prize.Count - drawn)
            : Config.LotterySettings.DrawMode == DrawMode.Repeat
                ? Math.Max(1, drawn + 1)
                : Math.Max(0, GetLotteryRepeatThreshold() - drawn);
        return new LotteryRemainingItem(FormatPrize(prize), prize.Tags, remaining, drawn);
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

        await _drawAudioService.PlayAsync(MusicSettings.AnimationMusic, MusicSettings.AnimationMusicVolume,
            MusicSettings.AnimationMusicFadeIn, MusicSettings.AnimationMusicFadeOut).ConfigureAwait(true);

        var iterations = AnimationSettings.Animation == AnimationMode.AutoPlay
            ? Math.Clamp(AnimationSettings.AutoplayCount, 1, 999)
            : 1;
        var delay = PreviewAnimationDuration;
        for (var i = 0; i < iterations; i++)
        {
            ReplaceResults(prizes.OrderBy(_ => Random.Shared.Next()).Take(count).ToList());
            IsResultVisible = true;
            TriggerPreviewAnimation();
            await Task.Delay(delay).ConfigureAwait(true);
        }
    }

    private void TriggerPreviewAnimation()
    {
        PreviewAnimationRevision++;
    }

    private void ReplaceResults(IReadOnlyList<Prize> prizes)
    {
        ResultItems.Clear();
        foreach (var prize in prizes)
            ResultItems.Add(CreateResultItem(prize));
    }

    private void TriggerResultAnimation()
    {
        ResultAnimationRevision++;
    }

    private LotteryResultItem CreateResultItem(Prize prize)
    {
        var accentBrush = ResolveAccentBrush();
        return new LotteryResultItem(
            FormatPrize(prize),
            prize.Tags,
            DisplaySettings.ShowTags && !string.IsNullOrWhiteSpace(prize.Tags),
            DisplaySettings.DisplayStyle == DisplayStyleMode.Card,
            accentBrush,
            DrawColorHelper.ResolveTextBrush(accentBrush, Config.Appearance.Theme),
            DisplaySettings.ShowWeightTransparency,
            $"权重 {prize.Weight:0.##}",
            BuildImage(prize),
            Config.LotterySettings.LotteryImage,
            BuildInitial(prize));
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

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        Config.LotterySettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.DefaultDrawSettings.PropertyChanged -= SettingsOnPropertyChanged;
        Config.Appearance.PropertyChanged -= SettingsOnPropertyChanged;
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
}

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

public sealed record LotteryRemainingItem(string DisplayText, string Tags, int Remaining, int DrawnCount)
{
    public bool IsTagsVisible => !string.IsNullOrWhiteSpace(Tags);
    public string CountText => $"剩余 {Remaining} / 已抽 {DrawnCount}";
}
