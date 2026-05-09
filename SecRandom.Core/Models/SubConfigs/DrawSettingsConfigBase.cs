using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class DrawSettingsConfigBase : ObservableObject
{
    [ObservableProperty] private AnimationMode _animation = AnimationMode.AutoPlay;

    [ObservableProperty] private AnimationColorThemeMode _animationColorTheme = AnimationColorThemeMode.None;
    [ObservableProperty] private Color _animationFixedColor = Color.Parse(GlobalConstants.DefaultThemeColor);
    [ObservableProperty] private int _animationInterval = 80;

    // music settings below

    [ObservableProperty] private string _animationMusic = string.Empty;
    [ObservableProperty] private int _animationMusicFadeIn = 300;
    [ObservableProperty] private int _animationMusicFadeOut = 300;

    [ObservableProperty] private int _animationMusicVolume = 100;
    [ObservableProperty] private int _autoplayCount = 10;
    [ObservableProperty] private string _customFont = GlobalConstants.DefaultFontFamily;
    [ObservableProperty] private DisplayFormatMode _displayFormat = DisplayFormatMode.Both;
    [ObservableProperty] private DisplayStyleMode _displayStyle = DisplayStyleMode.Default;
    [ObservableProperty] private int _fontSize = 50;
    [ObservableProperty] private int _resultFlowAnimationDuration = 250;
    [ObservableProperty] private bool _resultFlowAnimationStyle;

    [ObservableProperty] private string _resultMusic = string.Empty;
    [ObservableProperty] private int _resultMusicFadeIn = 300;
    [ObservableProperty] private int _resultMusicFadeOut = 300;
    [ObservableProperty] private ShowRandomMode _showRandom = ShowRandomMode.None;
    [ObservableProperty] private bool _showTags;

    [ObservableProperty] private bool _studentImage;
    [ObservableProperty] private StudentImagePositionMode _studentImagePosition = StudentImagePositionMode.Left;
    [ObservableProperty] private UseGlobalFontMode _useGlobalFont = UseGlobalFontMode.FollowGlobal;
}