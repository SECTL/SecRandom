using SecRandom.Core.Services.SimpleMode;

namespace SecRandom.Core.Tests;

/// <summary>
///     Page policy of the simple mode: only roll call stays reachable on the main window, the settings window keeps
///     the basic/appearance/roster/privacy/update/about pages, and every other id is normalized away instead of
///     being silently navigated to. The policy is pure so the app layer only has to ask it, and hidden features are
///     deliberately not represented here — they are gated by <c>IFeatureAvailabilityService</c>.
/// </summary>
public sealed class SimpleModePolicyTests
{
    [Theory]
    [InlineData("main.rollCall", true)]
    [InlineData("main.lottery", false)]
    [InlineData("main.history", false)]
    [InlineData("main.remoteDraw", false)]
    [InlineData("plugin.somePage", false)]
    public void MainPageWhitelist_KeepsOnlyRollCall(string pageId, bool expected)
    {
        Assert.Equal(expected, SimpleModePolicy.IsMainPageAllowed(pageId));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void MainPageWhitelist_TreatsMissingPageAsUnspecified(string? pageId, bool expected)
    {
        Assert.Equal(expected, SimpleModePolicy.IsMainPageAllowed(pageId));
    }

    [Theory]
    [InlineData("settings.overview", true)]
    [InlineData("settings.general.basic", true)]
    [InlineData("settings.personalized.appearance", true)]
    [InlineData("settings.listManagement.rollCallList", true)]
    [InlineData("settings.general.privacy", true)]
    [InlineData("settings.update", true)]
    [InlineData("settings.about", true)]
    [InlineData("settings.more", false)]
    [InlineData("settings.personalized.floatingWindow", false)]
    [InlineData("settings.listManagement.lotteryList", false)]
    [InlineData("settings.history.management", false)]
    [InlineData("settings.general.security", false)]
    [InlineData("settings.plugin", false)]
    [InlineData("settings.debug", false)]
    public void SettingsPageWhitelist_MatchesTheAgreedPages(string pageId, bool expected)
    {
        Assert.Equal(expected, SimpleModePolicy.IsSettingsPageAllowed(pageId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("main.lottery")]
    [InlineData("main.history")]
    [InlineData("main.remoteDraw")]
    public void NormalizeMainPageId_FallsBackToRollCall(string? pageId)
    {
        Assert.Equal(SimpleModePolicy.DefaultMainPageId, SimpleModePolicy.NormalizeMainPageId(pageId));
    }

    [Fact]
    public void NormalizeMainPageId_KeepsRollCall()
    {
        Assert.Equal(SimpleModePolicy.DefaultMainPageId,
            SimpleModePolicy.NormalizeMainPageId(SimpleModePolicy.DefaultMainPageId));
    }

    [Fact]
    public void NormalizeSettingsPageId_KeepsWhitelistedPage()
    {
        Assert.Equal("settings.general.basic",
            SimpleModePolicy.NormalizeSettingsPageId("settings.general.basic"));
    }

    [Theory]
    [InlineData("settings.more")]
    [InlineData("settings.history.management")]
    [InlineData("settings.plugin")]
    public void NormalizeSettingsPageId_DropsHiddenPage(string pageId)
    {
        Assert.Null(SimpleModePolicy.NormalizeSettingsPageId(pageId));
    }

    [Fact]
    public void NormalizeSettingsPageId_LeavesUnspecifiedPageAlone()
    {
        Assert.Null(SimpleModePolicy.NormalizeSettingsPageId(null));
        Assert.Null(SimpleModePolicy.NormalizeSettingsPageId(""));
    }

    [Fact]
    public void FilterSettingsPages_KeepsOnlyWhitelistedPagesInOrder()
    {
        var pages = new[]
        {
            "settings.general.basic",
            "settings.more",
            "settings.personalized.appearance",
            "settings.history.management"
        };

        var filtered = SimpleModePolicy.FilterSettingsPages(pages, page => page).ToArray();

        Assert.Equal(["settings.general.basic", "settings.personalized.appearance"], filtered);
    }

    [Fact]
    public void SimpleModeRuntime_ReadsThePersistedSwitch()
    {
        var config = new SecRandom.Core.Models.MainConfigModel();

        Assert.False(SimpleModeRuntime.IsEnabled(config));

        config.General.SimpleMode.Enabled = true;

        Assert.True(SimpleModeRuntime.IsEnabled(config));
    }

    [Fact]
    public void SimpleModeDefaults_AreBuiltFromAFreshModelThatIsNotItselfInSimpleMode()
    {
        Assert.False(SimpleModeDefaults.Model.General.SimpleMode.Enabled);
        Assert.True(SimpleModeDefaults.MoreSettings.LotteryEnabled);
    }

    [Fact]
    public void EffectiveSettings_AreTheRealInstancesWhenSimpleModeIsOff()
    {
        var config = new SecRandom.Core.Models.MainConfigModel();

        Assert.Same(config.MoreSettings, config.EffectiveMoreSettings);
        Assert.Same(config.RollCallSettings, config.EffectiveRollCallSettings);
        Assert.Same(config.DefaultDrawSettings, config.EffectiveDefaultDrawSettings);
        Assert.Same(config.General.HistoryGroups, config.EffectiveHistoryGroups);
    }

    [Fact]
    public void EffectiveSettings_ReadDefaultsWhileSimpleModeKeepsThePersistedValuesUntouched()
    {
        var config = new SecRandom.Core.Models.MainConfigModel();
        config.General.SimpleMode.Enabled = true;
        config.MoreSettings.EnableShortcut = true;
        config.MoreSettings.RollCallResetButton = false;
        config.RollCallSettings.DrawMode = SecRandom.Core.Enums.Configs.DrawMode.Repeat;
        config.LotterySettings.DrawMode = SecRandom.Core.Enums.Configs.DrawMode.Repeat;
        config.DefaultDrawSettings.DrawMode = SecRandom.Core.Enums.Configs.DrawMode.Repeat;
        config.General.HistoryGroups.Enabled = true;
        config.FloatingWindowSettings.StartupDisplayFloatingWindow = false;

        Assert.False(config.EffectiveMoreSettings.EnableShortcut);
        Assert.True(config.EffectiveMoreSettings.RollCallResetButton);
        Assert.Equal(SecRandom.Core.Enums.Configs.DrawMode.NoRepeat, config.EffectiveRollCallSettings.DrawMode);
        Assert.Equal(SecRandom.Core.Enums.Configs.DrawMode.NoRepeat, config.EffectiveLotterySettings.DrawMode);
        Assert.Equal(SecRandom.Core.Enums.Configs.DrawMode.NoRepeat, config.EffectiveDefaultDrawSettings.DrawMode);
        Assert.False(config.EffectiveHistoryGroups.Enabled);
        Assert.True(config.EffectiveFloatingWindowSettings.StartupDisplayFloatingWindow);

        // 简洁模式只影响读取，用户存下的真实设置原样保留（settings.json 不会被改写）。
        Assert.True(config.MoreSettings.EnableShortcut);
        Assert.False(config.MoreSettings.RollCallResetButton);
        Assert.Equal(SecRandom.Core.Enums.Configs.DrawMode.Repeat, config.RollCallSettings.DrawMode);
        Assert.True(config.General.HistoryGroups.Enabled);
        Assert.False(config.FloatingWindowSettings.StartupDisplayFloatingWindow);

        config.General.SimpleMode.Enabled = false;

        Assert.True(config.EffectiveMoreSettings.EnableShortcut);
        Assert.Equal(SecRandom.Core.Enums.Configs.DrawMode.Repeat, config.EffectiveRollCallSettings.DrawMode);
        Assert.True(config.EffectiveHistoryGroups.Enabled);
    }
}
