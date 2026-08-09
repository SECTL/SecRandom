using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Mobile;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// Fixed draw-policy and media form structure lives in AXAML. Runtime code only
/// synchronizes the managed music collection and persists edited settings.
/// </summary>
[PageInfo(MobilePageIds.DrawSettings, FluentIcons.PeopleFilled, "settings.mobile.preferences")]
public sealed partial class MobileDrawSettingsPage : MobileSettingsPageBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly MobileDrawMediaService _drawMedia;
    private readonly MobileMediaLibraryService _mediaLibrary;
    private bool _synchronizing;

    public MobileDrawSettingsPage(
        MainConfigHandler configHandler,
        MobileDrawMediaService drawMedia,
        MobileMediaLibraryService mediaLibrary,
        IMobileCapabilities capabilities)
        : base(capabilities)
    {
        _configHandler = configHandler;
        _drawMedia = drawMedia;
        _mediaLibrary = mediaLibrary;
        InitializeComponent();
        RefreshSurface();
    }

    private void RefreshSurface()
    {
        _synchronizing = true;
        try
        {
            var rollCall = _configHandler.Data.RollCallSettings;
            RollCallFairButton.IsChecked = rollCall.DrawType == DrawType.Fair;
            RollCallRandomButton.IsChecked = rollCall.DrawType == DrawType.Random;
            RollCallNoRepeatButton.IsChecked = rollCall.DrawMode == DrawMode.NoRepeat;
            RollCallRepeatButton.IsChecked = rollCall.DrawMode == DrawMode.Repeat;
            RollCallHalfRepeatButton.IsChecked = rollCall.DrawMode == DrawMode.HalfRepeat;
            RollCallHalfRepeatBox.Text = rollCall.HalfRepeat.ToString();
            RollCallImageToggle.IsChecked = _configHandler.Data.GetOverrideDrawSettings(
                DrawSettingsType.RollCall, OverridableDrawSettingsType.StudentImage).StudentImage;
            RollCallRestartedButton.IsChecked = rollCall.ClearRecord == ClearRecordMode.Restarted;
            RollCallClearedButton.IsChecked = rollCall.ClearRecord == ClearRecordMode.Cleared;

            LotterySettingsSection.IsVisible = IsLotteryEnabled;
            if (IsLotteryEnabled)
            {
                var lottery = _configHandler.Data.LotterySettings;
                LotteryCountButton.IsChecked = lottery.DrawType == LotteryDrawType.Count;
                LotteryPanButton.IsChecked = lottery.DrawType == LotteryDrawType.Pan;
                LotteryRepeatSettings.IsVisible = lottery.DrawType == LotteryDrawType.Pan;
                LotteryNoRepeatButton.IsChecked = lottery.DrawMode == DrawMode.NoRepeat;
                LotteryRepeatButton.IsChecked = lottery.DrawMode == DrawMode.Repeat;
                LotteryHalfRepeatButton.IsChecked = lottery.DrawMode == DrawMode.HalfRepeat;
                LotteryHalfRepeatBox.Text = lottery.HalfRepeat.ToString();
                LotteryImageToggle.IsChecked = lottery.LotteryImage;
                LotteryRestartedButton.IsChecked = lottery.ClearRecord == ClearRecordMode.Restarted;
                LotteryClearedButton.IsChecked = lottery.ClearRecord == ClearRecordMode.Cleared;
            }

            MediaSettingsSection.IsVisible = _drawMedia.IsSupported;
            if (!_drawMedia.IsSupported)
                return;

            RefreshMusicControls(
                rollCall,
                RollCallMusicOverrideToggle,
                RollCallMusicOptions,
                RollCallAnimationMusicSelector,
                RollCallResultMusicSelector,
                RollCallAnimationLoopToggle,
                RollCallAnimationVolumeBox,
                RollCallResultVolumeBox,
                isOverridable: true);
            RefreshMusicControls(
                _configHandler.Data.DefaultDrawSettings,
                null,
                null,
                DefaultAnimationMusicSelector,
                DefaultResultMusicSelector,
                DefaultAnimationLoopToggle,
                DefaultAnimationVolumeBox,
                DefaultResultVolumeBox,
                isOverridable: false);

            LotteryMusicItem.IsVisible = IsLotteryEnabled;
            if (IsLotteryEnabled)
            {
                RefreshMusicControls(
                    _configHandler.Data.LotterySettings,
                    LotteryMusicOverrideToggle,
                    LotteryMusicOptions,
                    LotteryAnimationMusicSelector,
                    LotteryResultMusicSelector,
                    LotteryAnimationLoopToggle,
                    LotteryAnimationVolumeBox,
                    LotteryResultVolumeBox,
                    isOverridable: true);
            }

            var rollCallVoice = _configHandler.Data.GetOverrideDrawSettings(
                DrawSettingsType.RollCall, OverridableDrawSettingsType.VoiceAnnouncement);
            RollCallVoiceAnnouncementToggle.IsChecked = rollCallVoice.VoiceAnnouncementEnabled;
            LotteryVoiceAnnouncementItem.IsVisible = IsLotteryEnabled;
            if (IsLotteryEnabled)
            {
                var lotteryVoice = _configHandler.Data.GetOverrideDrawSettings(
                    DrawSettingsType.Lottery, OverridableDrawSettingsType.VoiceAnnouncement);
                LotteryVoiceAnnouncementToggle.IsChecked = lotteryVoice.VoiceAnnouncementEnabled;
            }

            var voice = _configHandler.Data.VoiceSettings;
            VoiceEnabledToggle.IsChecked = voice.VoiceEnable;
            VoiceAnnounceIdToggle.IsChecked = voice.AnnounceId;
            VoiceAnnounceNameToggle.IsChecked = voice.AnnounceName;
            VoiceVolumeBox.Text = voice.VolumeSize.ToString();
            VoiceRateBox.Text = voice.SpeechRate.ToString();
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void RefreshMusicControls(
        DrawSettingsConfigBase settings,
        ToggleSwitch? overrideToggle,
        Control? options,
        ComboBox animationSelector,
        ComboBox resultSelector,
        ToggleSwitch loopToggle,
        TextBox animationVolumeBox,
        TextBox resultVolumeBox,
        bool isOverridable)
    {
        if (isOverridable && settings is OverridableDrawSettings overriding)
        {
            overrideToggle!.IsChecked = overriding.OverrideMusicSettings;
            options!.IsVisible = overriding.OverrideMusicSettings;
        }

        SetMusicSelection(animationSelector, settings.AnimationMusic);
        SetMusicSelection(resultSelector, settings.ResultMusic);
        loopToggle.IsChecked = settings.AnimationMusicLoop;
        animationVolumeBox.Text = settings.AnimationMusicVolume.ToString();
        resultVolumeBox.Text = settings.ResultMusicVolume.ToString();
    }

    private void SetMusicSelection(ComboBox selector, string selectedId)
    {
        var selections = _mediaLibrary.GetSelections();
        selector.ItemsSource = selections;
        selector.SelectedItem = selections.FirstOrDefault(selection => selection.Id == selectedId)
            ?? selections.FirstOrDefault();
    }

    private void RollCallDrawType_OnChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            SaveIfActive(() => _configHandler.Data.RollCallSettings.DrawType =
                ReferenceEquals(sender, RollCallFairButton) ? DrawType.Fair : DrawType.Random);
    }

    private void RollCallDrawMode_OnChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            SaveIfActive(() => _configHandler.Data.RollCallSettings.DrawMode =
                GetDrawMode(sender, RollCallNoRepeatButton, RollCallRepeatButton));
    }

    private void RollCallHalfRepeatBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(RollCallHalfRepeatBox, 1, int.MaxValue,
            value => _configHandler.Data.RollCallSettings.HalfRepeat = value);

    private void RollCallImageToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.GetOverrideDrawSettings(
            DrawSettingsType.RollCall, OverridableDrawSettingsType.StudentImage).StudentImage =
            RollCallImageToggle.IsChecked == true);

    private void RollCallClearRecord_OnChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            SaveIfActive(() => _configHandler.Data.RollCallSettings.ClearRecord =
                ReferenceEquals(sender, RollCallRestartedButton) ? ClearRecordMode.Restarted : ClearRecordMode.Cleared);
    }

    private void LotteryDrawType_OnChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            SaveIfActive(() => _configHandler.Data.LotterySettings.DrawType =
                ReferenceEquals(sender, LotteryCountButton) ? LotteryDrawType.Count : LotteryDrawType.Pan);
    }

    private void LotteryDrawMode_OnChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            SaveIfActive(() => _configHandler.Data.LotterySettings.DrawMode =
                GetDrawMode(sender, LotteryNoRepeatButton, LotteryRepeatButton));
    }

    private void LotteryHalfRepeatBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(LotteryHalfRepeatBox, 1, int.MaxValue,
            value => _configHandler.Data.LotterySettings.HalfRepeat = value);

    private void LotteryImageToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.LotterySettings.LotteryImage = LotteryImageToggle.IsChecked == true);

    private void LotteryClearRecord_OnChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            SaveIfActive(() => _configHandler.Data.LotterySettings.ClearRecord =
                ReferenceEquals(sender, LotteryRestartedButton) ? ClearRecordMode.Restarted : ClearRecordMode.Cleared);
    }

    private void RollCallMusicOverrideToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.RollCallSettings.OverrideMusicSettings =
            RollCallMusicOverrideToggle.IsChecked == true);

    private void RollCallAnimationMusicSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveMusicSelection(RollCallAnimationMusicSelector,
            value => _configHandler.Data.RollCallSettings.AnimationMusic = value);

    private void RollCallResultMusicSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveMusicSelection(RollCallResultMusicSelector,
            value => _configHandler.Data.RollCallSettings.ResultMusic = value);

    private void RollCallAnimationLoopToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.RollCallSettings.AnimationMusicLoop =
            RollCallAnimationLoopToggle.IsChecked == true);

    private void RollCallAnimationVolumeBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(RollCallAnimationVolumeBox, 0, 100,
            value => _configHandler.Data.RollCallSettings.AnimationMusicVolume = value);

    private void RollCallResultVolumeBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(RollCallResultVolumeBox, 0, 100,
            value => _configHandler.Data.RollCallSettings.ResultMusicVolume = value);

    private void DefaultAnimationMusicSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveMusicSelection(DefaultAnimationMusicSelector,
            value => _configHandler.Data.DefaultDrawSettings.AnimationMusic = value);

    private void DefaultResultMusicSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveMusicSelection(DefaultResultMusicSelector,
            value => _configHandler.Data.DefaultDrawSettings.ResultMusic = value);

    private void DefaultAnimationLoopToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.DefaultDrawSettings.AnimationMusicLoop =
            DefaultAnimationLoopToggle.IsChecked == true);

    private void DefaultAnimationVolumeBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(DefaultAnimationVolumeBox, 0, 100,
            value => _configHandler.Data.DefaultDrawSettings.AnimationMusicVolume = value);

    private void DefaultResultVolumeBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(DefaultResultVolumeBox, 0, 100,
            value => _configHandler.Data.DefaultDrawSettings.ResultMusicVolume = value);

    private void LotteryMusicOverrideToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.LotterySettings.OverrideMusicSettings =
            LotteryMusicOverrideToggle.IsChecked == true);

    private void LotteryAnimationMusicSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveMusicSelection(LotteryAnimationMusicSelector,
            value => _configHandler.Data.LotterySettings.AnimationMusic = value);

    private void LotteryResultMusicSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveMusicSelection(LotteryResultMusicSelector,
            value => _configHandler.Data.LotterySettings.ResultMusic = value);

    private void LotteryAnimationLoopToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.LotterySettings.AnimationMusicLoop =
            LotteryAnimationLoopToggle.IsChecked == true);

    private void LotteryAnimationVolumeBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(LotteryAnimationVolumeBox, 0, 100,
            value => _configHandler.Data.LotterySettings.AnimationMusicVolume = value);

    private void LotteryResultVolumeBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(LotteryResultVolumeBox, 0, 100,
            value => _configHandler.Data.LotterySettings.ResultMusicVolume = value);

    private void RollCallVoiceAnnouncementToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.GetOverrideDrawSettings(
            DrawSettingsType.RollCall, OverridableDrawSettingsType.VoiceAnnouncement).VoiceAnnouncementEnabled =
            RollCallVoiceAnnouncementToggle.IsChecked == true);

    private void LotteryVoiceAnnouncementToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.GetOverrideDrawSettings(
            DrawSettingsType.Lottery, OverridableDrawSettingsType.VoiceAnnouncement).VoiceAnnouncementEnabled =
            LotteryVoiceAnnouncementToggle.IsChecked == true);

    private void VoiceEnabledToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.VoiceSettings.VoiceEnable = VoiceEnabledToggle.IsChecked == true);

    private void VoiceAnnounceIdToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.VoiceSettings.AnnounceId = VoiceAnnounceIdToggle.IsChecked == true);

    private void VoiceAnnounceNameToggle_OnIsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveIfActive(() => _configHandler.Data.VoiceSettings.AnnounceName = VoiceAnnounceNameToggle.IsChecked == true);

    private void VoiceVolumeBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(VoiceVolumeBox, 0, 100,
            value => _configHandler.Data.VoiceSettings.VolumeSize = value);

    private void VoiceRateBox_OnLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SaveBoundedInteger(VoiceRateBox, 50, 200,
            value => _configHandler.Data.VoiceSettings.SpeechRate = value);

    private void SaveMusicSelection(ComboBox selector, Action<string> mutate)
    {
        if (!_synchronizing)
            Save(() => mutate(SelectedMusicId(selector)));
    }

    private void SaveIfActive(Action mutate)
    {
        if (!_synchronizing)
            Save(mutate);
    }

    private void SaveBoundedInteger(TextBox box, int minimum, int maximum, Action<int> mutate)
    {
        if (_synchronizing || !int.TryParse(box.Text, out var value))
            return;

        Save(() => mutate(Math.Clamp(value, minimum, maximum)));
    }

    private static string SelectedMusicId(ComboBox selector) =>
        selector.SelectedItem is MobileMediaSelection selection
            ? selection.Id
            : MobileMediaLibraryService.NoMusicTrackId;

    private static DrawMode GetDrawMode(object? sender, RadioButton noRepeat, RadioButton repeat) =>
        ReferenceEquals(sender, noRepeat) ? DrawMode.NoRepeat :
        ReferenceEquals(sender, repeat) ? DrawMode.Repeat : DrawMode.HalfRepeat;

    private void Save(Action mutate)
    {
        mutate();
        _configHandler.Save();
        RefreshSurface();
    }
}
