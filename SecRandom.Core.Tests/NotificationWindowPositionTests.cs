using System.Text.Json;
using System.Xml.Linq;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Tests;

public class NotificationWindowPositionTests
{
    /// <summary>
    ///     0–4 是旧版只有五档时的取值，升级后必须落在同一个地方：
    ///     中心 / 顶部居中 / 底部居中 / 左侧居中 / 右侧居中。
    /// </summary>
    [Fact]
    public void LegacyValuesKeepTheirAnchor()
    {
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Center, NotificationWindowVerticalAnchor.Center),
            NotificationWindowAnchorLayout.Resolve(0));
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Center, NotificationWindowVerticalAnchor.Top),
            NotificationWindowAnchorLayout.Resolve(1));
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Center, NotificationWindowVerticalAnchor.Bottom),
            NotificationWindowAnchorLayout.Resolve(2));
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Left, NotificationWindowVerticalAnchor.Center),
            NotificationWindowAnchorLayout.Resolve(3));
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Right, NotificationWindowVerticalAnchor.Center),
            NotificationWindowAnchorLayout.Resolve(4));
    }

    [Fact]
    public void AppendedCornersResolveToTheirCorner()
    {
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Left, NotificationWindowVerticalAnchor.Top),
            NotificationWindowAnchorLayout.Resolve(5));
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Right, NotificationWindowVerticalAnchor.Top),
            NotificationWindowAnchorLayout.Resolve(6));
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Left, NotificationWindowVerticalAnchor.Bottom),
            NotificationWindowAnchorLayout.Resolve(7));
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Right, NotificationWindowVerticalAnchor.Bottom),
            NotificationWindowAnchorLayout.Resolve(8));
    }

    [Fact]
    public void GridOrderCoversEveryValueExactlyOnce()
    {
        Assert.Equal(NotificationWindowAnchorLayout.CellCount, NotificationWindowAnchorLayout.GridOrder.Count);
        Assert.Equal(
            Enumerable.Range(0, NotificationWindowAnchorLayout.CellCount).OrderBy(value => value),
            NotificationWindowAnchorLayout.GridOrder.Select(position => (int)position).OrderBy(value => value));

        for (var index = 0; index < NotificationWindowAnchorLayout.GridOrder.Count; index++)
        {
            var value = (int)NotificationWindowAnchorLayout.GridOrder[index];
            Assert.Equal(index, NotificationWindowAnchorLayout.ToCellIndex(value));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void UndefinedValuesFallBackToCenter(int value)
    {
        Assert.False(NotificationWindowAnchorLayout.IsDefined(value));
        Assert.Equal(NotificationWindowPosition.Center, NotificationWindowAnchorLayout.Normalize(value));
        Assert.Equal(
            (NotificationWindowHorizontalAnchor.Center, NotificationWindowVerticalAnchor.Center),
            NotificationWindowAnchorLayout.Resolve(value));
        Assert.Equal(
            NotificationWindowAnchorLayout.ToCellIndex((int)NotificationWindowPosition.Center),
            NotificationWindowAnchorLayout.ToCellIndex(value));
    }

    [Fact]
    public void ChannelSettingsClampUndefinedPosition()
    {
        NotificationChannelSettings defaults = new();
        Assert.Equal((int)NotificationWindowPosition.Center, defaults.WindowPosition);

        NotificationChannelSettings corners = new()
        {
            WindowPosition = (int)NotificationWindowPosition.BottomRight
        };
        Assert.Equal((int)NotificationWindowPosition.BottomRight, corners.WindowPosition);

        NotificationChannelSettings outOfRange = new() { WindowPosition = 42 };
        Assert.Equal((int)NotificationWindowPosition.Center, outOfRange.WindowPosition);

        // 旧配置缺这个键时反序列化同样得到中心。
        NotificationChannelSettings? missing = JsonSerializer.Deserialize<NotificationChannelSettings>(
            "{}",
            ConfigServiceBase.JsonOptions);
        Assert.NotNull(missing);
        Assert.Equal((int)NotificationWindowPosition.Center, missing.WindowPosition);
    }

    [Fact]
    public void ChannelSettingsPersistPositionUnderTheLegacyJsonKey()
    {
        NotificationChannelSettings settings = new()
        {
            WindowPosition = (int)NotificationWindowPosition.TopLeft
        };

        string json = JsonSerializer.Serialize(settings, ConfigServiceBase.JsonOptions);
        Assert.Contains("\"window_position\": 5", json, StringComparison.Ordinal);

        NotificationChannelSettings? restored = JsonSerializer.Deserialize<NotificationChannelSettings>(
            """{ "window_position": 7 }""",
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal((int)NotificationWindowPosition.BottomLeft, restored.WindowPosition);
    }

    /// <summary>
    ///     圆点选择器的九格提示直接取自这些资源键；缺一个就会有格子没有名字（悬停提示与无障碍名称同时丢失）。
    /// </summary>
    [Theory]
    [InlineData("Resources.resx")]
    [InlineData("Resources.en-US.resx")]
    [InlineData("Resources.ja-JP.resx")]
    public void NotificationResourcesLocalizeEveryAnchor(string fileName)
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        XDocument resource = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SecRandom/Langs/SettingsPages/Notification",
            fileName));
        string[] expectedKeys =
        [
            "O_WindowPosition_Center",
            "O_WindowPosition_Top",
            "O_WindowPosition_Bottom",
            "O_WindowPosition_Left",
            "O_WindowPosition_Right",
            "O_WindowPosition_TopLeft",
            "O_WindowPosition_TopRight",
            "O_WindowPosition_BottomLeft",
            "O_WindowPosition_BottomRight"
        ];

        var localized = resource.Root?
            .Elements("data")
            .Where(element => expectedKeys.Contains((string?)element.Attribute("name")))
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value)
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .ToHashSet() ?? [];

        Assert.Equal(expectedKeys.OrderBy(key => key), localized.OrderBy(key => key));
    }
}