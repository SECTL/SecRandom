using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using Xunit;

namespace SecRandom.Core.Tests;

/// <summary>
///     全屏通知渠道设置的默认值与夹取规则。
/// </summary>
/// <remarks>
///     这些默认值直接决定教师第一次打开渠道时看到的画面：文案只服务打字机、背景跟随主题、
///     不透明度 100%。默认值被改掉不会报错，只会在真实课堂上"看起来不对"，所以钉在这里。
/// </remarks>
public class FullscreenNotificationSettingsTests
{
    [Fact]
    public void Defaults_MatchPresentationContract()
    {
        var settings = new FullscreenNotificationSettings();

        Assert.True(settings.ShowText);
        Assert.False(settings.ShowVoiceAnnouncement);
        Assert.Equal(FullscreenAnimationStyle.Typewriter, settings.AnimationStyle);
        Assert.Equal(FullscreenNotificationSettings.DefaultTypewriterCharactersPerSecond,
            settings.TypewriterCharactersPerSecond);
        Assert.Equal(FullscreenBackgroundMode.Theme, settings.BackgroundMode);
        Assert.Equal(string.Empty, settings.BackgroundImage);
        Assert.Equal(100, settings.BackgroundOpacity);
        Assert.True(settings.UseThemeAccentColor);
        Assert.Equal(string.Empty, settings.AccentColor);
        Assert.True(settings.ShowResultNumber);
        Assert.True(settings.ShowResultName);
        Assert.False(settings.ShowResultAvatar);
        Assert.Equal(FullscreenNotificationSettings.DefaultResultFontSize, settings.ResultFontSize);
        Assert.True(settings.ShowRemainingCount);
    }

    [Theory]
    [InlineData(0, FullscreenNotificationSettings.MinTypewriterCharactersPerSecond)]
    [InlineData(12, 12)]
    [InlineData(999, FullscreenNotificationSettings.MaxTypewriterCharactersPerSecond)]
    public void EffectiveTypewriterSpeed_ClampsToRange(int configured, int expected)
    {
        var settings = new FullscreenNotificationSettings { TypewriterCharactersPerSecond = configured };

        Assert.Equal(expected, settings.EffectiveTypewriterCharactersPerSecond);
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(100, 100)]
    [InlineData(180, 100)]
    public void EffectiveBackgroundOpacity_ClampsToRange(int configured, int expected)
    {
        var settings = new FullscreenNotificationSettings { BackgroundOpacity = configured };

        Assert.Equal(expected, settings.EffectiveBackgroundOpacity);
    }

    [Theory]
    [InlineData(0, FullscreenNotificationSettings.MinResultFontSize)]
    [InlineData(96, 96)]
    [InlineData(999, FullscreenNotificationSettings.MaxResultFontSize)]
    public void EffectiveResultFontSize_ClampsToRange(int configured, int expected)
    {
        var settings = new FullscreenNotificationSettings { ResultFontSize = configured };

        Assert.Equal(expected, settings.EffectiveResultFontSize);
    }

    [Theory]
    [InlineData(-4, FullscreenNotificationSettings.MinBackgroundBlurRadius)]
    [InlineData(24, 24)]
    [InlineData(400, FullscreenNotificationSettings.MaxBackgroundBlurRadius)]
    public void EffectiveBackgroundBlurRadius_ClampsToRange(int configured, int expected)
    {
        var settings = new FullscreenNotificationSettings { BackgroundBlurRadius = configured };

        Assert.Equal(expected, settings.EffectiveBackgroundBlurRadius);
    }

    [Theory]
    [InlineData(-90, FullscreenNotificationSettings.MinBackgroundGradientAngle)]
    [InlineData(135, FullscreenNotificationSettings.DefaultBackgroundGradientAngle)]
    [InlineData(720, FullscreenNotificationSettings.MaxBackgroundGradientAngle)]
    public void EffectiveBackgroundGradientAngle_ClampsToRange(int configured, int expected)
    {
        var settings = new FullscreenNotificationSettings { BackgroundGradientAngle = configured };

        Assert.Equal(expected, settings.EffectiveBackgroundGradientAngle);
    }

    /// <summary>
    ///     默认必须落到"跟随主题"：这是唯一不依赖用户文件、也不会把窗口画成透明的模式。
    /// </summary>
    [Fact]
    public void ResolveBackgroundAppearance_DefaultsToTheme()
    {
        var appearance = new FullscreenNotificationSettings().ResolveBackgroundAppearance();

        Assert.Equal(FullscreenBackgroundMode.Theme, appearance.Mode);
        Assert.Null(appearance.SolidColor);
        Assert.Null(appearance.GradientStart);
        Assert.False(appearance.UsesImage);
    }

    [Fact]
    public void ResolveBackgroundAppearance_SolidCarriesOnlySolidColor()
    {
        var settings = new FullscreenNotificationSettings
        {
            BackgroundMode = FullscreenBackgroundMode.Solid,
            BackgroundColor = Avalonia.Media.Color.Parse("#FF336699"),
            BackgroundGradientStart = Avalonia.Media.Color.Parse("#FF000000"),
            BackgroundGradientEnd = Avalonia.Media.Color.Parse("#FFFFFFFF")
        };

        var appearance = settings.ResolveBackgroundAppearance();

        Assert.Equal(FullscreenBackgroundMode.Solid, appearance.Mode);
        Assert.Equal(Avalonia.Media.Color.Parse("#FF336699"), appearance.SolidColor);
        Assert.Null(appearance.GradientStart);
        Assert.Null(appearance.GradientEnd);
        Assert.False(appearance.UsesImage);
    }

    [Fact]
    public void ResolveBackgroundAppearance_GradientCarriesBothColorsAndClampedAngle()
    {
        var settings = new FullscreenNotificationSettings
        {
            BackgroundMode = FullscreenBackgroundMode.Gradient,
            BackgroundGradientStart = Avalonia.Media.Color.Parse("#FF123456"),
            BackgroundGradientEnd = Avalonia.Media.Color.Parse("#FF654321"),
            BackgroundGradientAngle = 999
        };

        var appearance = settings.ResolveBackgroundAppearance();

        Assert.Equal(FullscreenBackgroundMode.Gradient, appearance.Mode);
        Assert.Null(appearance.SolidColor);
        Assert.Equal(Avalonia.Media.Color.Parse("#FF123456"), appearance.GradientStart);
        Assert.Equal(Avalonia.Media.Color.Parse("#FF654321"), appearance.GradientEnd);
        Assert.Equal(FullscreenNotificationSettings.MaxBackgroundGradientAngle, appearance.GradientAngle);
    }

    /// <summary>
    ///     图片模式只带模式本身：图片路径由渲染层按数据目录解析，Core 不碰文件系统。
    /// </summary>
    [Fact]
    public void ResolveBackgroundAppearance_ImageOnlyCarriesMode()
    {
        var settings = new FullscreenNotificationSettings
        {
            BackgroundMode = FullscreenBackgroundMode.Image,
            BackgroundImage = "classroom.png"
        };

        var appearance = settings.ResolveBackgroundAppearance();

        Assert.Equal(FullscreenBackgroundMode.Image, appearance.Mode);
        Assert.True(appearance.UsesImage);
        Assert.Null(appearance.SolidColor);
        Assert.Null(appearance.GradientStart);
    }

    /// <summary>
    ///     从 settings.json 手工改出来的越界值不能把整屏画歪：一次 Normalize 全部夹回去。
    /// </summary>
    [Fact]
    public void NormalizeBackground_ClampsOutOfRangeValues()
    {
        var settings = new FullscreenNotificationSettings
        {
            BackgroundMode = (FullscreenBackgroundMode)42,
            BackgroundGradientAngle = -30,
            BackgroundBlurRadius = 999,
            BackgroundOpacity = -5,
            BackgroundImage = "  classroom.png  "
        };

        settings.NormalizeBackground();

        Assert.Equal(FullscreenBackgroundMode.Theme, settings.BackgroundMode);
        Assert.Equal(FullscreenNotificationSettings.MinBackgroundGradientAngle, settings.BackgroundGradientAngle);
        Assert.Equal(FullscreenNotificationSettings.MaxBackgroundBlurRadius, settings.BackgroundBlurRadius);
        Assert.Equal(0, settings.BackgroundOpacity);
        Assert.Equal("classroom.png", settings.BackgroundImage);
    }

    /// <summary>
    ///     转盘（旧值 2）已移除：旧 settings.json / 备份里留下的 2 必须落到学号网格，
    ///     不能变成未定义枚举掉进窗口 switch 的 default 静态结果。
    /// </summary>
    [Fact]
    public void LegacyWheelAnimationStyle_FallsBackToGrid()
    {
        const string json = """
                            {
                              "notification_settings": {
                                "fullscreen": {
                                  "animation_style": 2
                                }
                              }
                            }
                            """;

        var config = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(FullscreenAnimationStyle.Grid, config.NotificationSettings.Fullscreen.AnimationStyle);
    }

    /// <summary>任何未定义的枚举值都按同样的规则回落，避免手改配置把动画弄丢。</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    public void UndefinedAnimationStyle_NormalizesToGrid(int stored)
    {
        var settings = new FullscreenNotificationSettings
        {
            AnimationStyle = (FullscreenAnimationStyle)stored
        };

        Assert.Equal(FullscreenAnimationStyle.Grid, settings.AnimationStyle);
    }
}
