using SecRandom.Core.Services.FloatingWindow;

namespace SecRandom.Core.Tests;

/// <summary>
///     浮窗结果卡的自适应列数：4 人 2×2、6 人横向 3×2 / 窄高 2×3，且列数不会因为
///     长姓名把卡片撑宽而退化成竖着排。
/// </summary>
public sealed class QuickDrawResultGridLayoutTests
{
    [Theory]
    [InlineData(4, 1920, 1080, 200, 100, 2)]
    [InlineData(6, 1920, 1080, 200, 100, 3)]
    [InlineData(6, 1200, 1600, 200, 100, 2)]
    [InlineData(4, 800, 1200, 200, 100, 2)]
    [InlineData(5, 1920, 1080, 200, 100, 3)]
    [InlineData(1, 1920, 1080, 200, 100, 1)]
    public void 按屏幕宽高选择列数(
        int itemCount,
        double availableWidth,
        double availableHeight,
        double cardWidth,
        double cardHeight,
        int expectedColumns)
    {
        var columns = QuickDrawResultGridLayout.ResolveColumns(
            itemCount, availableWidth, availableHeight, cardWidth, cardHeight);

        Assert.Equal(expectedColumns, columns);
    }

    [Theory]
    [InlineData(6, 3840, 1080, 200, 100, 3)] // 超宽屏也不把 6 人排成 4+2
    [InlineData(4, 300, 1000, 200, 100, 1)] // 宽度只够一张时允许单列
    [InlineData(7, 1920, 1080, 200, 100, 4)] // 3+3+1 的孤行补成 4+3
    public void 列数受宽度与末行平衡约束(
        int itemCount,
        double availableWidth,
        double availableHeight,
        double cardWidth,
        double cardHeight,
        int expectedColumns)
    {
        var columns = QuickDrawResultGridLayout.ResolveColumns(
            itemCount, availableWidth, availableHeight, cardWidth, cardHeight);

        Assert.Equal(expectedColumns, columns);
    }

    [Fact]
    public void 高度装不下时优先加列减少行数()
    {
        // 3 列时 8 人要 3 行（600 DIP）超过 500 的可用高度；4 列只要 2 行。
        var columns = QuickDrawResultGridLayout.ResolveColumns(8, 800, 500, 100, 200);

        Assert.Equal(4, columns);
    }

    [Fact]
    public void 没有结果时不产生列()
    {
        Assert.Equal(0, QuickDrawResultGridLayout.ResolveColumns(0, 1920, 1080, 200, 100));
    }
}