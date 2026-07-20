using Avalonia.Controls;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile.Controls;
using SecRandom.Mobile.Services;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 抽取设置页：点名 / 抽奖两组规则（抽取类型、重复规则、半重复阈值、清除记录）。
/// 抽奖组整体按启动时注入的功能能力投影，关闭时不渲染。
/// 配置存取与 Save 模式（变更 → Save → 重渲染）与原实现等价。
/// </summary>
public sealed partial class MobileDrawSettingsPage : MobileSettingsPageBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly MobileDrawMediaService _drawMedia;
    private readonly MobileMediaLibraryService _mediaLibrary;

    public MobileDrawSettingsPage(
        MainConfigHandler configHandler,
        MobileDrawMediaService drawMedia,
        MobileMediaLibraryService mediaLibrary,
        IMobileNavigator navigator,
        IMobileCapabilities capabilities)
        : base(navigator, capabilities)
    {
        _configHandler = configHandler;
        _drawMedia = drawMedia;
        _mediaLibrary = mediaLibrary;
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        var rollCall = _configHandler.Data.RollCallSettings;
        var lottery = _configHandler.Data.LotterySettings;
        var items = new List<Control>
        {
            new MobileSectionHeader(LR.S_RollCall, FluentIcons.PersonFilled),
            MobileSettingRow.Choice(LR.O_Fair, rollCall.DrawType == DrawType.Fair, () => Save(() => rollCall.DrawType = DrawType.Fair)),
            MobileSettingRow.Choice(LR.O_Random, rollCall.DrawType == DrawType.Random, () => Save(() => rollCall.DrawType = DrawType.Random)),
            new MobileSectionHeader(LR.S_RepeatMode),
            CreateDrawModeRow(LR.O_NoRepeat, rollCall.DrawMode == DrawMode.NoRepeat, () => rollCall.DrawMode = DrawMode.NoRepeat),
            CreateDrawModeRow(LR.O_Repeat, rollCall.DrawMode == DrawMode.Repeat, () => rollCall.DrawMode = DrawMode.Repeat),
            CreateDrawModeRow(LR.O_HalfRepeat, rollCall.DrawMode == DrawMode.HalfRepeat, () => rollCall.DrawMode = DrawMode.HalfRepeat),
            MobileSettingRow.Integer(LR.S_HalfRepeat, null, rollCall.HalfRepeat, 1, value => Save(() => rollCall.HalfRepeat = value)),
            MobileSettingRow.Toggle(LR.S_AttachedImage, null,
                _configHandler.Data.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.StudentImage).StudentImage,
                value => Save(() => _configHandler.Data.GetOverrideDrawSettings(
                    DrawSettingsType.RollCall, OverridableDrawSettingsType.StudentImage).StudentImage = value))
        };

        if (_drawMedia.IsSupported)
        {
            items.AddRange(CreateMusicSettings(LR.S_RollCall + " · " + LR.S_Music, rollCall, rollCall));
            items.Add(MobileSettingRow.Toggle(LR.S_VoiceAnnouncements, null,
                _configHandler.Data.GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.VoiceAnnouncement).VoiceAnnouncementEnabled,
                value => Save(() => _configHandler.Data.GetOverrideDrawSettings(
                    DrawSettingsType.RollCall, OverridableDrawSettingsType.VoiceAnnouncement).VoiceAnnouncementEnabled = value)));
        }

        items.Add(new MobileSectionHeader(LR.S_ClearRecord));
        items.Add(MobileSettingRow.Choice(LR.O_Restarted, rollCall.ClearRecord == ClearRecordMode.Restarted,
            () => Save(() => rollCall.ClearRecord = ClearRecordMode.Restarted)));
        items.Add(MobileSettingRow.Choice(LR.O_Cleared, rollCall.ClearRecord == ClearRecordMode.Cleared,
            () => Save(() => rollCall.ClearRecord = ClearRecordMode.Cleared)));

        // 抽奖能力关闭时整组隐藏。
        if (IsLotteryEnabled)
        {
            items.Add(new MobileSectionHeader(LR.S_Lottery, FluentIcons.GiftFilled));
            items.Add(MobileSettingRow.Choice(LR.O_Count, lottery.DrawType == LotteryDrawType.Count, () => Save(() => lottery.DrawType = LotteryDrawType.Count)));
            items.Add(MobileSettingRow.Choice(LR.O_Pan, lottery.DrawType == LotteryDrawType.Pan, () => Save(() => lottery.DrawType = LotteryDrawType.Pan)));

            if (lottery.DrawType == LotteryDrawType.Pan)
            {
                items.Add(new MobileSectionHeader(LR.S_RepeatMode));
                items.Add(CreateDrawModeRow(LR.O_NoRepeat, lottery.DrawMode == DrawMode.NoRepeat, () => lottery.DrawMode = DrawMode.NoRepeat));
                items.Add(CreateDrawModeRow(LR.O_Repeat, lottery.DrawMode == DrawMode.Repeat, () => lottery.DrawMode = DrawMode.Repeat));
                items.Add(CreateDrawModeRow(LR.O_HalfRepeat, lottery.DrawMode == DrawMode.HalfRepeat, () => lottery.DrawMode = DrawMode.HalfRepeat));
                items.Add(MobileSettingRow.Integer(LR.S_HalfRepeat, null, lottery.HalfRepeat, 1, value => Save(() => lottery.HalfRepeat = value)));
            }

            items.Add(MobileSettingRow.Toggle(LR.S_AttachedImage, null, lottery.LotteryImage,
                value => Save(() => lottery.LotteryImage = value)));
            if (_drawMedia.IsSupported)
            {
                items.AddRange(CreateMusicSettings(LR.S_Lottery + " · " + LR.S_Music, lottery, lottery));
                items.Add(MobileSettingRow.Toggle(LR.S_VoiceAnnouncements, null,
                    _configHandler.Data.GetOverrideDrawSettings(DrawSettingsType.Lottery, OverridableDrawSettingsType.VoiceAnnouncement).VoiceAnnouncementEnabled,
                    value => Save(() => _configHandler.Data.GetOverrideDrawSettings(
                        DrawSettingsType.Lottery, OverridableDrawSettingsType.VoiceAnnouncement).VoiceAnnouncementEnabled = value)));
            }
            items.Add(new MobileSectionHeader(LR.S_ClearRecord));
            items.Add(MobileSettingRow.Choice(LR.O_Restarted, lottery.ClearRecord == ClearRecordMode.Restarted, () => Save(() => lottery.ClearRecord = ClearRecordMode.Restarted)));
            items.Add(MobileSettingRow.Choice(LR.O_Cleared, lottery.ClearRecord == ClearRecordMode.Cleared, () => Save(() => lottery.ClearRecord = ClearRecordMode.Cleared)));
        }

        if (_drawMedia.IsSupported)
        {
            items.AddRange(CreateMusicSettings(LR.S_Music, _configHandler.Data.DefaultDrawSettings));
            var voice = _configHandler.Data.VoiceSettings;
            items.Add(new MobileSectionHeader(LR.S_VoiceAnnouncements, FluentIcons.IotFilled));
            items.Add(MobileSettingRow.Toggle(LR.S_VoiceAnnouncements, null, voice.VoiceEnable,
                value => Save(() => voice.VoiceEnable = value)));
            items.Add(MobileSettingRow.Toggle(LR.S_AnnounceId, null, voice.AnnounceId,
                value => Save(() => voice.AnnounceId = value)));
            items.Add(MobileSettingRow.Toggle(LR.S_AnnounceName, null, voice.AnnounceName,
                value => Save(() => voice.AnnounceName = value)));
            items.Add(MobileSettingRow.Integer(LR.S_VoiceVolume, null, voice.VolumeSize, 0,
                value => Save(() => voice.VolumeSize = Math.Clamp(value, 0, 100))));
            items.Add(MobileSettingRow.Integer(LR.S_VoiceRate, null, voice.SpeechRate, 50,
                value => Save(() => voice.SpeechRate = Math.Clamp(value, 50, 200))));
        }

        RenderPage(items);
    }

    private Control CreateDrawModeRow(string text, bool selected, Action setMode) =>
        MobileSettingRow.Choice(text, selected, () => Save(setMode));

    private IEnumerable<Control> CreateMusicSettings(
        string title,
        DrawSettingsConfigBase settings,
        OverridableDrawSettings? overridingSettings = null)
    {
        var items = new List<Control>
        {
            new MobileSectionHeader(title, FluentIcons.Speaker2Filled)
        };
        if (overridingSettings is not null)
        {
            items.Add(MobileSettingRow.Toggle(LR.S_UseCustomMusic, null, overridingSettings.OverrideMusicSettings,
                value => Save(() => overridingSettings.OverrideMusicSettings = value)));
            if (!overridingSettings.OverrideMusicSettings)
                return items;
        }

        var selections = _mediaLibrary.GetSelections();
        var animation = CreateMusicSelector(selections, settings.AnimationMusic);
        var result = CreateMusicSelector(selections, settings.ResultMusic);
        animation.SelectionChanged += (_, _) => Save(() => settings.AnimationMusic = SelectedMusicId(animation));
        result.SelectionChanged += (_, _) => Save(() => settings.ResultMusic = SelectedMusicId(result));
        items.Add(CreateLabeledControl(LR.S_AnimationMusic, animation));
        items.Add(CreateLabeledControl(LR.S_ResultMusic, result));
        items.Add(MobileSettingRow.Toggle(LR.S_AnimationMusicLoop, null, settings.AnimationMusicLoop,
            value => Save(() => settings.AnimationMusicLoop = value)));
        items.Add(MobileSettingRow.Integer(LR.S_AnimationMusicVolume, null, settings.AnimationMusicVolume, 0,
            value => Save(() => settings.AnimationMusicVolume = Math.Clamp(value, 0, 100))));
        items.Add(MobileSettingRow.Integer(LR.S_ResultMusicVolume, null, settings.ResultMusicVolume, 0,
            value => Save(() => settings.ResultMusicVolume = Math.Clamp(value, 0, 100))));
        return items;
    }

    private static ComboBox CreateMusicSelector(IReadOnlyList<MobileMediaSelection> selections, string selected) => new()
    {
        ItemsSource = selections,
        SelectedItem = selections.FirstOrDefault(selection => selection.Id == selected) ?? selections.FirstOrDefault(),
        MinHeight = 44,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<MobileMediaSelection>((selection, _) => new TextBlock
        {
            Text = selection?.DisplayName ?? string.Empty,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        })
    };

    private static string SelectedMusicId(ComboBox selector) =>
        selector.SelectedItem is MobileMediaSelection selection
            ? selection.Id
            : MobileMediaLibraryService.NoMusicTrackId;

    private static Control CreateLabeledControl(string label, Control control) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, FontSize = 12 },
            control
        }
    };

    private void Save(Action mutate)
    {
        mutate();
        _configHandler.Save();
        Render();
    }

}
