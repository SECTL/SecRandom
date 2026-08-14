using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;

namespace SecRandom.Core.Tests;

public sealed class LotteryProcessDisplayFormatterTests
{
    [Fact]
    public void NormalizeTemplate_CollapsesSpacesAndCanonicalizesLineBreaks()
    {
        var template = LotteryProcessDisplayFormatter.NormalizeTemplate("{id}   {prize}\r\n{group}-{member}");

        Assert.Equal("{id} {prize}{/}{group}-{member}", template);
    }

    [Fact]
    public void Format_ExpandsVariablesAndOmitsEmptyLinesAndSeparators()
    {
        var formatted = LotteryProcessDisplayFormatter.Format(
            LotteryProcessDisplayFormatter.DefaultTemplate,
            "1",
            "01",
            "Gift",
            "Group A",
            "7",
            "Lin");
        var prizeOnly = LotteryProcessDisplayFormatter.Format(
            LotteryProcessDisplayFormatter.DefaultTemplate,
            "1",
            "01",
            "Gift",
            null,
            null,
            null);

        Assert.Equal("1 Gift\nGroup A-Lin", formatted);
        Assert.Equal("1 Gift", prizeOnly);
    }

    [Fact]
    public void Format_PreservesDirectSeparators()
    {
        var formatted = LotteryProcessDisplayFormatter.Format(
            "{prizeId} {prize}-{memberId}\u00B7{member}",
            "1",
            "P-01",
            "Gift",
            null,
            "8",
            "Lin");

        Assert.Equal("P-01 Gift-8\u00B7Lin", formatted);
    }

    [Fact]
    public void LotterySettings_UsesTheNewDefaultAndPersistsCustomTemplate()
    {
        LotterySettingsConfig settings = new()
        {
            LotteryShowRandom = LotteryShowRandomMode.Custom,
            CustomLotteryShowRandomFormat = "{prize} {member}"
        };
        var json = JsonSerializer.Serialize(settings, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<LotterySettingsConfig>(json, ConfigServiceBase.JsonOptions);

        Assert.Equal(LotteryShowRandomMode.Custom, settings.LotteryShowRandom);
        Assert.Equal("{prize} {member}", restored?.CustomLotteryShowRandomFormat);
        Assert.Equal(
            LotteryProcessDisplayFormatter.DefaultTemplate,
            new LotterySettingsConfig().CustomLotteryShowRandomFormat);
    }

    [Fact]
    public void LotteryProcessDisplay_UsesTheDefaultTemplateUntilDisplayOverrideIsEnabled()
    {
        MainConfigModel config = new();
        config.LotterySettings.LotteryShowRandom = LotteryShowRandomMode.Custom;
        config.LotterySettings.CustomLotteryShowRandomFormat = "{prizeId}";

        Assert.Equal(LotteryProcessDisplayFormatter.DefaultTemplate, config.GetLotteryProcessDisplayTemplate());

        config.LotterySettings.OverrideDisplaySettings = true;

        Assert.Equal("{prizeId}", config.GetLotteryProcessDisplayTemplate());
    }
}
