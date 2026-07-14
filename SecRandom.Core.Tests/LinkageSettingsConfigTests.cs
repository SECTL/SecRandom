using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.ImportExport;

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

    [Fact]
    public void V2SettingsMigration_MapsEveryLinkageField()
    {
        const string json = """
                            {
                              "linkage_settings": {
                                "verification_required": false,
                                "instant_draw_disable": true,
                                "data_source": 2,
                                "hide_floating_window_on_class_end": true,
                                "pre_class_reset_enabled": true,
                                "pre_class_reset_time": 90,
                                "pre_class_enable_time": 30,
                                "post_class_disable_delay": 15,
                                "subject_history_filter_enabled": true,
                                "subject_history_break_assignment": 2
                              }
                            }
                            """;
        var service = new ImportExportService(null!, null!, null!, null!, NullLogger<ImportExportService>.Instance);
        var method = typeof(ImportExportService).GetMethod("MigrateV2Settings", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = (MainConfigModel)method.Invoke(service, [json, new List<string>()])!;

        Assert.False(result.LinkageSettings.VerificationRequired);
        Assert.True(result.LinkageSettings.InstantDrawDisable);
        Assert.Equal(LinkageDataSource.ClassIsland, result.LinkageSettings.DataSource);
        Assert.True(result.LinkageSettings.HideFloatingWindowOnClassEnd);
        Assert.True(result.LinkageSettings.PreClassResetEnabled);
        Assert.Equal(90, result.LinkageSettings.PreClassResetTime);
        Assert.Equal(30, result.LinkageSettings.PreClassEnableTime);
        Assert.Equal(15, result.LinkageSettings.PostClassDisableDelay);
        Assert.True(result.LinkageSettings.SubjectHistoryFilterEnabled);
        Assert.Equal(LinkageBreakAssignment.NextClass, result.LinkageSettings.SubjectHistoryBreakAssignment);
    }

    [Fact]
    public void V2SettingsMigration_UnknownLinkageDataSourceFallsBackToOff()
    {
        const string json = """
                            {
                              "linkage_settings": {
                                "data_source": 99
                              }
                            }
                            """;
        var service = new ImportExportService(null!, null!, null!, null!, NullLogger<ImportExportService>.Instance);
        var method = typeof(ImportExportService).GetMethod("MigrateV2Settings", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = (MainConfigModel)method.Invoke(service, [json, new List<string>()])!;

        Assert.Equal(LinkageDataSource.Off, result.LinkageSettings.DataSource);
    }
}
