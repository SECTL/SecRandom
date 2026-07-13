using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Models;
using SecRandom.Services.ImportExport;

namespace SecRandom.Core.Tests;

public class ImportExportArchiveTests
{
    [Theory]
    [InlineData("../settings.json")]
    [InlineData("C:/settings.json")]
    [InlineData("config/../security/credentials.v1.json")]
    public void ArchivePathNormalizer_RejectsUnsafePaths(string path)
    {
        var method = typeof(ImportExportService).GetMethod("NormalizePath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [path]));

        Assert.IsType<InvalidDataException>(exception.InnerException);
    }

    [Fact]
    public void ArchivePathNormalizer_NormalizesDirectorySeparators()
    {
        var method = typeof(ImportExportService).GetMethod("NormalizePath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var normalized = (string)method.Invoke(null, ["list\\roll_call_list\\class.json"])!;

        Assert.Equal("list/roll_call_list/class.json", normalized);
    }

    [Fact]
    public void V2SettingsMigration_PreservesShortcutSettings()
    {
        const string json = """
                            {
                              "shortcut_settings": {
                                "enable_shortcut": true,
                                "open_roll_call_page": "Ctrl+Alt+R",
                                "use_quick_draw": "Ctrl+Alt+Q",
                                "open_lottery_page": "Ctrl+Alt+L",
                                "increase_roll_call_count": "Ctrl+Up",
                                "decrease_roll_call_count": "Ctrl+Down",
                                "increase_lottery_count": "Alt+Up",
                                "decrease_lottery_count": "Alt+Down",
                                "start_roll_call": "F7",
                                "start_lottery": "F8"
                              }
                            }
                            """;
        var service = new ImportExportService(null!, null!, null!, null!, NullLogger<ImportExportService>.Instance);
        var method = typeof(ImportExportService).GetMethod("MigrateV2Settings", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var settings = (MainConfigModel)method.Invoke(service, [json, new List<string>()])!;

        Assert.True(settings.MoreSettings.EnableShortcut);
        Assert.Equal("Ctrl+Alt+R", settings.MoreSettings.OpenRollCallPageShortcut);
        Assert.Equal("Ctrl+Alt+Q", settings.MoreSettings.QuickDrawShortcut);
        Assert.Equal("Ctrl+Alt+L", settings.MoreSettings.OpenLotteryPageShortcut);
        Assert.Equal("Ctrl+Up", settings.MoreSettings.IncreaseRollCallCountShortcut);
        Assert.Equal("Ctrl+Down", settings.MoreSettings.DecreaseRollCallCountShortcut);
        Assert.Equal("Alt+Up", settings.MoreSettings.IncreaseLotteryCountShortcut);
        Assert.Equal("Alt+Down", settings.MoreSettings.DecreaseLotteryCountShortcut);
        Assert.Equal("F7", settings.MoreSettings.StartRollCallShortcut);
        Assert.Equal("F8", settings.MoreSettings.StartLotteryShortcut);
    }
}
