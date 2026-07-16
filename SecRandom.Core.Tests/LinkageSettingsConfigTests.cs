using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;

namespace SecRandom.Core.Tests;

public sealed class LinkageSettingsConfigTests
{
    [Fact]
    public void DataSourceValues_MatchV2Contract()
    {
        Assert.Equal(0, (int)LinkageDataSource.Off);
        Assert.Equal(1, (int)LinkageDataSource.Cses);
        Assert.Equal(2, (int)LinkageDataSource.ClassIsland);
    }

    [Fact]
    public void MainConfig_RoundTripsNumericLinkageEnums()
    {
        var config = new MainConfigModel();
        config.LinkageSettings.DataSource = LinkageDataSource.Cses;
        config.LinkageSettings.SubjectHistoryBreakAssignment = LinkageBreakAssignment.NextClass;

        var json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        var restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(LinkageDataSource.Cses, restored.LinkageSettings.DataSource);
        Assert.Equal(LinkageBreakAssignment.NextClass, restored.LinkageSettings.SubjectHistoryBreakAssignment);
    }

    [Fact]
    public void HistoryItem_MissingCourseNameKeepsLegacyGlobalScope()
    {
        var item = JsonSerializer.Deserialize<SecRandom.Shared.Models.Profile.HistoryItem>("{}", ConfigServiceBase.JsonOptions);

        Assert.NotNull(item);
        Assert.Equal(string.Empty, item.CourseName);
    }

}
