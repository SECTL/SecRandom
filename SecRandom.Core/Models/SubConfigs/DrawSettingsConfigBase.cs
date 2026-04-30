using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class DrawSettingsConfigBase : ObservableObject
{
    [ObservableProperty] private UseGlobalFontMode _useGlobalFont = UseGlobalFontMode.FollowGlobal;
    [ObservableProperty] private string _customFont = GlobalConstants.DefaultFontFamily;
    [ObservableProperty] private int _fontSize = 50;
    [ObservableProperty] private DisplayFormatMode _displayFormat = DisplayFormatMode.Both;
    [ObservableProperty] private DisplayStyleMode _displayStyle = DisplayStyleMode.Default;
    [ObservableProperty] private ShowRandomMode _showRandom = ShowRandomMode.None;
    [ObservableProperty] private bool _showTags = false;

    [ObservableProperty] private AnimationMode _animation = AnimationMode.AutoPlay;
    [ObservableProperty] private int _animationInterval = 80;
    [ObservableProperty] private int _autoplayCount = 10;
    [ObservableProperty] private bool _resultFlowAnimationStyle = false;
    [ObservableProperty] private int _resultFlowAnimationDuration = 250;

    [ObservableProperty] private AnimationColorThemeMode _animationColorTheme = AnimationColorThemeMode.None;
    [ObservableProperty] private Color _animationFixedColor = Color.Parse(GlobalConstants.DefaultThemeColor);

    [ObservableProperty] private bool _studentImage = false;
    [ObservableProperty] private StudentImagePositionMode _studentImagePosition = StudentImagePositionMode.Left;
    
    // music settings below
    
    [ObservableProperty] private string _animationMusic = string.Empty;
    [ObservableProperty] private int _animationMusicFadeIn = 300;
    [ObservableProperty] private int _animationMusicFadeOut = 300;
    
    [ObservableProperty] private string _resultMusic = string.Empty;
    [ObservableProperty] private int _resultMusicFadeIn = 300;
    [ObservableProperty] private int _resultMusicFadeOut = 300;
    
    [ObservableProperty] private int _animationMusicVolume = 100;
}