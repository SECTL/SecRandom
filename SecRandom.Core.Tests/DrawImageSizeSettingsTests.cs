using SecRandom.Core.Enums;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     结果图片大小：默认页给一份，点名/闪抽/抽奖各自可覆盖，值必须留在可读可点的范围内。
/// </summary>
public sealed class DrawImageSizeSettingsTests
{
    [Fact]
    public void 四份抽取设置的图片大小默认一致()
    {
        var config = new MainConfigModel();

        Assert.Equal(DrawSettingsConfigBase.DefaultImageSize, config.DefaultDrawSettings.StudentImageSize);
        Assert.Equal(DrawSettingsConfigBase.DefaultImageSize, config.RollCallSettings.StudentImageSize);
        Assert.Equal(DrawSettingsConfigBase.DefaultImageSize, config.QuickDrawSettings.StudentImageSize);
        Assert.Equal(DrawSettingsConfigBase.DefaultImageSize, config.LotterySettings.LotteryImageSize);
    }

    [Fact]
    public void 覆盖开关关闭时各页跟随默认页的图片大小()
    {
        var config = new MainConfigModel();
        config.DefaultDrawSettings.StudentImageSize = 120;
        config.RollCallSettings.StudentImageSize = 40;
        config.QuickDrawSettings.StudentImageSize = 40;
        config.LotterySettings.LotteryImageSize = 40;

        Assert.Equal(120, Resolve(config, DrawSettingsType.RollCall).StudentImageSize);
        Assert.Equal(120, Resolve(config, DrawSettingsType.QuickDraw).StudentImageSize);

        config.RollCallSettings.OverrideStudentImageSettings = true;
        config.QuickDrawSettings.OverrideStudentImageSettings = true;

        Assert.Equal(40, Resolve(config, DrawSettingsType.RollCall).StudentImageSize);
        Assert.Equal(40, Resolve(config, DrawSettingsType.QuickDraw).StudentImageSize);
    }

    [Theory]
    [InlineData(0, DrawSettingsConfigBase.MinImageSize)]
    [InlineData(-40, DrawSettingsConfigBase.MinImageSize)]
    [InlineData(1, DrawSettingsConfigBase.MinImageSize)]
    [InlineData(1000, DrawSettingsConfigBase.MaxImageSize)]
    [InlineData(201, DrawSettingsConfigBase.MaxImageSize)]
    [InlineData(96, 96)]
    public void 越界的图片大小会被夹回范围内(int input, int expected)
    {
        var settings = new DefaultDrawSettingsConfig { StudentImageSize = input };

        Assert.Equal(expected, settings.StudentImageSize);
    }

    [Fact]
    public void 奖品图片大小同样受范围约束()
    {
        var lottery = new LotterySettingsConfig { LotteryImageSize = 9999 };

        Assert.Equal(DrawSettingsConfigBase.MaxImageSize, lottery.LotteryImageSize);
    }

    [Fact]
    public void 控制面目录里的图片大小带标签与范围()
    {
        var fields = ControlSettingsCatalog.Describe(new MainConfigModel())
            .SelectMany(category => category.Fields)
            .ToDictionary(field => field.Path, StringComparer.Ordinal);

        foreach (var path in new[] { "default_draw.student_image_size", "roll_call.student_image_size", "lottery.lottery_image_size" })
        {
            var field = fields[path];
            Assert.False(string.IsNullOrWhiteSpace(field.Label));
            Assert.Equal(DrawSettingsConfigBase.MinImageSize, field.Min);
            Assert.Equal(DrawSettingsConfigBase.MaxImageSize, field.Max);
        }
    }

    private static DrawSettingsConfigBase Resolve(MainConfigModel config, DrawSettingsType type) =>
        config.GetOverrideDrawSettings(type, OverridableDrawSettingsType.StudentImage);
}
