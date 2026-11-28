using System.Globalization;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Views;

namespace SecRandom.Core.Tests;

public class FloatingWindowDockHandleTests
{
    [Theory]
    [InlineData(1.0, 16, false)]
    [InlineData(1.0, 17, true)]
    [InlineData(1.5, 24, false)]
    [InlineData(1.5, 25, true)]
    [InlineData(2.0, 20, false)]
    [InlineData(2.0, 32, false)]
    [InlineData(2.0, 33, true)]
    [InlineData(2.0, -33, true)]
    public void TouchDragThresholdUsesDeviceIndependentPixels(double scaling, int deltaY, bool expected)
    {
        Assert.Equal(expected, FloatingWindow.HasExceededDockDragThreshold(deltaY, scaling, isTouchOrPen: true));
    }

    [Fact]
    public void ChineseUi_AllowsExactlyOneCharacter()
    {
        Assert.Equal(1, FloatingWindowDockHandle.GetMaxTextLength(CultureInfo.GetCultureInfo("zh-CN")));
        Assert.Equal(1, FloatingWindowDockHandle.GetMaxTextLength(CultureInfo.GetCultureInfo("zh-TW")));
    }

    [Fact]
    public void OtherLanguages_DoNotUseTheSingleCharacterRule()
    {
        Assert.Equal(4, FloatingWindowDockHandle.GetMaxTextLength(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(4, FloatingWindowDockHandle.GetMaxTextLength(CultureInfo.GetCultureInfo("ja-JP")));
        // 没有语言信息时按“非中文”处理，宽松优先，避免把内容悄悄截掉
        Assert.Equal(4, FloatingWindowDockHandle.GetMaxTextLength(null));
    }

    [Fact]
    public void NormalizeText_KeepsOnlyAllowedCharactersUpToLimit()
    {
        Assert.Equal("抽", FloatingWindowDockHandle.NormalizeText("抽", 1));
        Assert.Equal("Go", FloatingWindowDockHandle.NormalizeText("  Go  ", 4));
        Assert.Equal("G", FloatingWindowDockHandle.NormalizeText("Go", 1));
        Assert.Equal(string.Empty, FloatingWindowDockHandle.NormalizeText("   ", 1));
    }

    [Fact]
    public void NormalizeText_RejectsEmojiButKeepsOrdinarySymbols()
    {
        Assert.Equal("抽", FloatingWindowDockHandle.NormalizeText("🎲抽", 1));
        Assert.Equal("抽", FloatingWindowDockHandle.NormalizeText("抽🎲", 1));
        Assert.Equal(string.Empty, FloatingWindowDockHandle.NormalizeText("😀", 4));
        // 组合型 emoji（变体选择符 / ZWJ）也不能漏进来
        Assert.Equal(string.Empty, FloatingWindowDockHandle.NormalizeText("❤️", 4));
        // 普通字母和标点不是 emoji，照常保留
        Assert.Equal("A", FloatingWindowDockHandle.NormalizeText("A", 4));
        Assert.Equal("#", FloatingWindowDockHandle.NormalizeText("#", 4));
        // 换行之类的控制字符直接丢弃
        Assert.Equal("抽", FloatingWindowDockHandle.NormalizeText("抽\n", 1));
    }

    [Fact]
    public void ResolveText_FallsBackToLocalizedDefault()
    {
        Assert.Equal("抽", FloatingWindowDockHandle.ResolveText("", "抽"));
        Assert.Equal("抽", FloatingWindowDockHandle.ResolveText(null, "抽"));
        Assert.Equal("抽", FloatingWindowDockHandle.ResolveText("   ", "抽"));
        Assert.Equal("Go", FloatingWindowDockHandle.ResolveText("Go", "抽"));
        Assert.Equal("Go", FloatingWindowDockHandle.ResolveText("Go🎲", "抽"));
    }

    [Fact]
    public void ResolveText_KeepsFourCharactersEvenOnChineseUi()
    {
        // 存储里的“Go”不因当前是中文界面而被截成一个字，切语言不丢内容
        Assert.Equal("Go", FloatingWindowDockHandle.ResolveText("Go", "抽"));
    }

    [Fact]
    public void TextFontScale_ShrinksAsTextGrows()
    {
        Assert.True(FloatingWindowDockHandle.GetTextFontScale(1) >
                    FloatingWindowDockHandle.GetTextFontScale(2));
        Assert.True(FloatingWindowDockHandle.GetTextFontScale(2) >
                    FloatingWindowDockHandle.GetTextFontScale(4));
    }

    [Fact]
    public void StyleConstants_AreStable()
    {
        Assert.True(FloatingWindowDockHandle.IsIconStyle(FloatingWindowDockHandle.IconStyle));
        Assert.True(FloatingWindowDockHandle.IsTextStyle(FloatingWindowDockHandle.TextStyle));
        Assert.False(FloatingWindowDockHandle.IsTextStyle(FloatingWindowDockHandle.ArrowStyle));
        Assert.NotEqual(FloatingWindowDockHandle.LeftArrowGlyph, FloatingWindowDockHandle.RightArrowGlyph);
    }

    [Fact]
    public void Config_DockHandleFieldsRoundTripThroughJson()
    {
        MainConfigModel config = new();
        config.FloatingWindowSettings.StickToEdgeDisplayStyle = FloatingWindowDockHandle.ArrowStyle;
        config.FloatingWindowSettings.StickToEdgeDisplayText = "抽";
        config.FloatingWindowSettings.StickToEdgeDisplayIconGlyph = "PeopleFilled";
        config.FloatingWindowSettings.StickToEdgeDisplayIconImage = "abc.png";

        string json = System.Text.Json.JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored =
            System.Text.Json.JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(FloatingWindowDockHandle.ArrowStyle, restored.FloatingWindowSettings.StickToEdgeDisplayStyle);
        Assert.Equal("抽", restored.FloatingWindowSettings.StickToEdgeDisplayText);
        Assert.Equal("PeopleFilled", restored.FloatingWindowSettings.StickToEdgeDisplayIconGlyph);
        Assert.Equal("abc.png", restored.FloatingWindowSettings.StickToEdgeDisplayIconImage);
    }
}
