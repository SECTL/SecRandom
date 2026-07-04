using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using SecRandom.Services;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.ViewModels;

public sealed partial class LotteryPageViewModel : ViewModelBase
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
        RefreshPrizeLists();
    }

    public ObservableCollection<string> PrizeListNames { get; } = [];
    public ObservableCollection<LotteryResultItem> ResultItems { get; } = [];
    public bool CanStartDraw => _profileService.CurrentPrizeList?.Prizes.Any(p => p.Exists) ?? false;
    public double ResultFontSize => DisplaySettings.FontSize;
    public FontFamily ResultFontFamily => BuildResultFontFamily();
    public bool ResultFlowAnimationEnabled => AnimationSettings.Animation != AnimationMode.NoAnimation && AnimationSettings.ResultFlowAnimationStyle;
    public int ResultFlowAnimationDuration => Math.Clamp(AnimationSettings.ResultFlowAnimationDuration, 80, 3000);
    public int PreviewAnimationDuration => Math.Clamp(AnimationSettings.AnimationInterval, 60, 240);

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
        OnPropertyChanged(nameof(CanStartDraw));
    }

    [RelayCommand]
    private async Task StartDrawAsync()
    {
        if (!CanStartDraw)
        {
            StatusText = "没有可抽取的奖品";
            return;
        }

        var prizes = (_profileService.CurrentPrizeList?.Prizes ?? []).Where(p => p.Exists).ToList();
        var count = Math.Clamp(DrawCount, 1, Math.Max(1, prizes.Count));
        await ShowPreviewAsync(prizes, count).ConfigureAwait(true);

        var result = _drawEngine.DrawPrize(count, _ => true);
        if (!result.IsSuccess)
        {
            StatusText = "抽奖失败：" + result.Status;
            return;
        }

        var drawn = result.Result.ToList();
        _profileService.RecordPrizeHistory(drawn, DateTime.Now, count);
        ReplaceResults(drawn);
        IsResultVisible = true;
        TriggerResultAnimation();
        StatusText = $"已抽取 {ResultItems.Count} 个奖品";
        await _drawAudioService.PlayAsync(MusicSettings.ResultMusic, MusicSettings.ResultMusicVolume,
            MusicSettings.ResultMusicFadeIn, MusicSettings.ResultMusicFadeOut).ConfigureAwait(false);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _profileService.ClearCurrentPrizeHistory();
        ResultItems.Clear();
        IsResultVisible = false;
        StatusText = "已清空当前奖池历史";
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

    private async Task ShowPreviewAsync(IReadOnlyList<Prize> prizes, int count)
    {
        if (Config.LotterySettings.LotteryShowRandom < 0 || AnimationSettings.Animation == AnimationMode.NoAnimation)
            return;

        await _drawAudioService.PlayAsync(MusicSettings.AnimationMusic, MusicSettings.AnimationMusicVolume,
            MusicSettings.AnimationMusicFadeIn, MusicSettings.AnimationMusicFadeOut).ConfigureAwait(false);

        var iterations = AnimationSettings.Animation == AnimationMode.AutoPlay
            ? Math.Clamp(AnimationSettings.AutoplayCount, 1, 999)
            : 1;
        var delay = Math.Clamp(AnimationSettings.AnimationInterval, 1, 10000);
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
            LotteryShowRandomMode.PrizeHyphenName => string.IsNullOrWhiteSpace(prize.Id) ? prize.Name : $"{prize.Id} - {prize.Name}",
            _ => string.IsNullOrWhiteSpace(prize.Id) ? prize.Name : $"{prize.Id}\n{prize.Name}"
        };
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
