using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using System;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class DrawSettingsConfigBase : ObservableObject
{
    [ObservableProperty] private UseGlobalFontMode _useGlobalFont = UseGlobalFontMode.FollowGlobal;
    [ObservableProperty] private string _customFont = GlobalConstants.DefaultFontFamily;
    [ObservableProperty] private int _fontSize = 50;
    [ObservableProperty] private DisplayFormatMode _displayFormat = DisplayFormatMode.Both;
    [ObservableProperty] private DisplayStyleMode _displayStyle = DisplayStyleMode.Default;
    [ObservableProperty] private bool _showTags = false;
    [ObservableProperty] private bool _showWeightTransparency = false;
    [ObservableProperty] private string _reminderText = "别紧张";
    [ObservableProperty] private int _reminderFontSize = 30;
    [ObservableProperty] private Color _reminderTextColor = Color.Parse("#808080");
    [ObservableProperty] private int _reminderTextOpacity = 50;

    [ObservableProperty] private AnimationMode _animation = AnimationMode.AutoPlay;
    [ObservableProperty] private int _animationInterval = 80;
    [ObservableProperty] private int _autoplayCount = 5;
    [ObservableProperty] private DrawAnimationStyleMode _animationStyle = DrawAnimationStyleMode.DirectRotate;

    [JsonPropertyName("result_flow_animation_mode")]
    public DrawAnimationStyleMode LegacyResultFlowAnimationMode
    {
        get => AnimationStyle;
        set => AnimationStyle = value;
    }

    partial void OnAnimationStyleChanged(DrawAnimationStyleMode value)
    {
        if (!Enum.IsDefined(typeof(DrawAnimationStyleMode), value))
            AnimationStyle = DrawAnimationStyleMode.DirectRotate;
    }

    [ObservableProperty] private AnimationColorThemeMode _animationColorTheme = AnimationColorThemeMode.None;
    [ObservableProperty] private Color _animationFixedColor = Color.Parse(GlobalConstants.DefaultThemeColor);

    [ObservableProperty] private bool _studentImage = false;
    [ObservableProperty] private StudentImagePositionMode _studentImagePosition = StudentImagePositionMode.Left;

    // music settings below

    [ObservableProperty] private string _animationMusic = "$none";
    private bool _animationMusicLoop = true;
    private bool _hasAnimationMusicLoop;
    [ObservableProperty] private bool _voiceAnnouncementEnabled = true;

    public bool AnimationMusicLoop
    {
        get => _animationMusicLoop;
        set
        {
            _hasAnimationMusicLoop = true;
            SetProperty(ref _animationMusicLoop, value);
        }
    }

    internal bool HasAnimationMusicLoop => _hasAnimationMusicLoop;
    [ObservableProperty] private int _animationMusicFadeIn = 300;
    [ObservableProperty] private int _animationMusicFadeOut = 300;

    [ObservableProperty] private string _resultMusic = "$none";
    [ObservableProperty] private int _resultMusicFadeIn = 300;
    [ObservableProperty] private int _resultMusicFadeOut = 300;

    [ObservableProperty] private int _animationMusicVolume = 100;
    [ObservableProperty] private int _resultMusicVolume = 100;
}
