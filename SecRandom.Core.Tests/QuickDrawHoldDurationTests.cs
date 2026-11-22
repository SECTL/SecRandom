using SecRandom.Core.Services.FloatingWindow;

namespace SecRandom.Core.Tests;

public class QuickDrawHoldDurationTests
{
    [Fact]
    public void DefaultHoldDuration_IsEightHundredMilliseconds()
    {
        Assert.Equal(800, QuickDrawLongPressGesture.DefaultHoldDurationMilliseconds);
    }

    [Theory]
    [InlineData(500, 500)]
    [InlineData(40, 40)]
    [InlineData(3000, 3000)]
    public void PrimaryButton_KeepsTheConfiguredDuration(int configured, int expected)
    {
        Assert.Equal(expected, QuickDrawLongPressGesture.ResolveHoldDuration(configured, isSecondaryButton: false));
    }

    [Theory]
    [InlineData(500, 160)]
    [InlineData(3000, 160)]
    [InlineData(40, 40)]
    public void SecondaryButton_ShortensToItsConfirmationWindow(int configured, int expected)
    {
        // 右键是 Windows 触控长按的替身：用户真正的按住时长已经被系统用掉，
        // 所以不能再等满配置值，但也不能一按下就当长按。
        Assert.Equal(expected, QuickDrawLongPressGesture.ResolveHoldDuration(configured, isSecondaryButton: true));
    }

    [Fact]
    public void BothButtons_StayInsideTheExecutableRange()
    {
        Assert.Equal(
            QuickDrawLongPressGesture.MinHoldDurationMilliseconds,
            QuickDrawLongPressGesture.ResolveHoldDuration(-10, isSecondaryButton: false));
        Assert.Equal(
            QuickDrawLongPressGesture.MinHoldDurationMilliseconds,
            QuickDrawLongPressGesture.ResolveHoldDuration(-10, isSecondaryButton: true));
        Assert.Equal(
            QuickDrawLongPressGesture.MaxHoldDurationMilliseconds,
            QuickDrawLongPressGesture.ResolveHoldDuration(int.MaxValue, isSecondaryButton: false));
        Assert.Equal(
            QuickDrawLongPressGesture.SecondaryButtonHoldDurationMilliseconds,
            QuickDrawLongPressGesture.ResolveHoldDuration(int.MaxValue, isSecondaryButton: true));
    }

    [Fact]
    public void SecondaryWindow_IsShorterThanTheDefaultHold()
    {
        Assert.True(
            QuickDrawLongPressGesture.SecondaryButtonHoldDurationMilliseconds
            < QuickDrawLongPressGesture.DefaultHoldDurationMilliseconds);
    }
}
