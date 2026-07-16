namespace SecRandom.Core.Tests;

public class SettingsMarkupTests
{
    [Fact]
    public void NotificationOverrideSectionsUseSettingsExpanderItems()
    {
        var document = System.Xml.Linq.XDocument.Load(GetNotificationMarkupPath());
        var overrideSections = document.Descendants()
            .Where(element => element.Name.LocalName == "FASettingsExpander"
                              && element.Attribute("IsExpanded") is not null)
            .ToList();

        Assert.Equal(3, overrideSections.Count);
        Assert.All(overrideSections, section =>
        {
            var items = section.Elements()
                .Where(element => element.Name.LocalName != "FASettingsExpander.Footer")
                .ToList();
            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.Equal("FASettingsExpanderItem", item.Name.LocalName));
        });
    }

    [Fact]
    public void NotificationChannelSettingsDoNotRepeatThePageTitle()
    {
        string markup = File.ReadAllText(GetNotificationMarkupPath());

        Assert.DoesNotContain("ChannelTitle", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationMonitorShowsUnspecifiedWhenNoValueIsSelected()
    {
        string markup = File.ReadAllText(GetNotificationMarkupPath());

        Assert.Contains(
            "PlaceholderText=\"{x:Static lsp:Resources.O_Monitor_Unspecified}\"",
            markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationOverrideSectionsExpandOnlyWhenEnabled()
    {
        string markup = File.ReadAllText(GetNotificationMarkupPath());

        Assert.Contains("IsExpanded=\"{Binding OverrideBasicSettings, Mode=OneWay}\"", markup, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{Binding OverrideNotificationWindowSettings, Mode=OneWay}\"", markup, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"{Binding OverrideServiceSettings, Mode=OneWay}\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultNotificationSectionsDoNotExposeCollapseControls()
    {
        var document = System.Xml.Linq.XDocument.Load(GetDefaultNotificationMarkupPath());
        var pageContainer = document.Descendants().Single(element =>
            element.Name.LocalName == "StackPanel"
            && ((string?)element.Attribute("Classes"))?.Contains("page-container", StringComparison.Ordinal) == true);
        var rows = pageContainer.Elements()
            .Where(element => element.Name.LocalName == "FASettingsExpander")
            .ToList();

        Assert.Equal(11, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Null(row.Attribute("IsExpanded"));
            Assert.DoesNotContain(row.Elements(), child => child.Name.LocalName == "FASettingsExpanderItem");
        });
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "NotificationChannelSettingsContent");
    }

    [Fact]
    public void NotificationMonitorRefreshDoesNotClearTheCurrentSelection()
    {
        string overrideMarkup = File.ReadAllText(GetNotificationMarkupPath());
        string defaultMarkup = File.ReadAllText(GetDefaultNotificationMarkupPath());
        string source = File.ReadAllText(GetRepositoryPath(
            "SecRandom/Views/SettingsPages/Notification/NotificationChannelSettingsPageBase.cs"));

        const string binding = "SelectedItem=\"{Binding SelectedMonitor, Mode=TwoWay}\"";
        Assert.Contains(binding, overrideMarkup, StringComparison.Ordinal);
        Assert.Contains(binding, defaultMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=", overrideMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=", defaultMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged=", overrideMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged=", defaultMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("MonitorOptions.Clear()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MonitorOptions.Remove", source, StringComparison.Ordinal);
        Assert.Contains("MonitorOptions.Add(new MonitorOption(ChannelSettings.EnabledMonitor", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationMainWindowFallbackStopsFurtherDelivery()
    {
        string source = File.ReadAllText(GetRepositoryPath(
            "SecRandom/Services/Notification/NotificationService.cs"));
        int fallbackStart = source.IndexOf("if (useMainWindow)", StringComparison.Ordinal);
        int backendDeliveryStart = source.IndexOf("var useBuiltIn", fallbackStart, StringComparison.Ordinal);

        Assert.True(fallbackStart >= 0 && backendDeliveryStart > fallbackStart);
        Assert.Contains("return;", source[fallbackStart..backendDeliveryStart], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "SecRandom/Views/SettingsPages/Picking/RollCallDrawSettingsPage.axaml",
        "OverrideDisplaySettings,OverrideAnimationSettings,OverrideColorSettings,OverrideStudentImageSettings,OverrideMusicSettings,OverrideReminderSettings")]
    [InlineData(
        "SecRandom/Views/SettingsPages/Picking/QuickDrawSettingsPage.axaml",
        "OverrideDisplaySettings,OverrideAnimationSettings,OverrideColorSettings,OverrideStudentImageSettings,OverrideMusicSettings")]
    [InlineData(
        "SecRandom/Views/SettingsPages/Picking/LotteryDrawSettingsPage.axaml",
        "OverrideDisplaySettings,OverrideAnimationSettings,OverrideColorSettings,OverrideStudentImageSettings,OverrideMusicSettings,OverrideReminderSettings")]
    public void DrawOverrideSectionsUseSettingsExpanderItems(string relativePath, string overrideNames)
    {
        var document = System.Xml.Linq.XDocument.Load(GetRepositoryPath(relativePath));

        foreach (string overrideName in overrideNames.Split(','))
        {
            string expandedBinding = $"{{Binding Settings.{overrideName}, Mode=OneWay}}";
            var section = document.Descendants().SingleOrDefault(element =>
                element.Name.LocalName == "FASettingsExpander"
                && (string?)element.Attribute("IsExpanded") == expandedBinding);
            Assert.True(section is not null, $"{relativePath} is missing the {overrideName} override expander.");

            var rows = section!.Elements()
                .Where(element => element.Name.LocalName != "FASettingsExpander.Footer")
                .ToList();
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.Equal("FASettingsExpanderItem", row.Name.LocalName));
        }
    }

    [Theory]
    [InlineData("SecRandom/Views/SettingsPages/Picking/RollCallDrawSettingsPage.axaml.cs")]
    [InlineData("SecRandom/Views/SettingsPages/Picking/QuickDrawSettingsPage.axaml.cs")]
    [InlineData("SecRandom/Views/SettingsPages/Picking/LotteryDrawSettingsPage.axaml.cs")]
    public void DrawSettingsPagesSubscribeBeforeNormalizing(string relativePath)
    {
        string source = File.ReadAllText(GetRepositoryPath(relativePath));
        int constructorStart = source.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        int subscribe = source.IndexOf("SubscribeSettings();", constructorStart, StringComparison.Ordinal);
        int normalize = source.IndexOf("NormalizeDrawSettings();", constructorStart, StringComparison.Ordinal);

        Assert.True(subscribe >= 0, $"{relativePath} must subscribe to settings in its constructor.");
        Assert.True(normalize >= 0, $"{relativePath} must normalize settings in its constructor.");
        Assert.True(subscribe < normalize, $"{relativePath} must subscribe before normalization so repairs are saved.");
    }

    [Theory]
    [InlineData("SecRandom/Views/SettingsPages/Picking/RollCallDrawSettingsPage.axaml.cs")]
    [InlineData("SecRandom/Views/SettingsPages/Picking/QuickDrawSettingsPage.axaml.cs")]
    [InlineData("SecRandom/Views/SettingsPages/Picking/LotteryDrawSettingsPage.axaml.cs")]
    public void DrawSettingsPagesDoNotNormalizeInReadOnlyPreview(string relativePath)
    {
        string settingsViewSource = File.ReadAllText(GetRepositoryPath("SecRandom/Views/SettingsView.axaml.cs"));
        string source = File.ReadAllText(GetRepositoryPath(relativePath));
        int normalize = source.IndexOf("private void NormalizeDrawSettings()", StringComparison.Ordinal);

        Assert.Contains("public bool IsPreviewMode => _isPreviewMode;", settingsViewSource, StringComparison.Ordinal);
        Assert.True(normalize >= 0, $"{relativePath} must define NormalizeDrawSettings().");
        Assert.Contains(
            "SettingsView.Current?.IsPreviewMode == true",
            source[normalize..],
            StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPreviewCannotRestartTheApplication()
    {
        string source = File.ReadAllText(GetRepositoryPath("SecRandom/Views/SettingsView.axaml.cs"));
        int methodStart = source.IndexOf("private async Task ShowRestartDialog()", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("private void ButtonRestartApp_OnClick", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        string method = source[methodStart..methodEnd];
        Assert.Contains("_isPreviewMode", method, StringComparison.Ordinal);
        Assert.Contains("SecurityOperation.RestartApplication", method, StringComparison.Ordinal);
        Assert.Contains("AuthorizeAsync", method, StringComparison.Ordinal);
    }

    private static string GetNotificationMarkupPath()
    {
        return GetRepositoryPath(
            "SecRandom/Views/SettingsPages/Notification/NotificationChannelSettingsContent.axaml");
    }

    private static string GetDefaultNotificationMarkupPath()
    {
        return GetRepositoryPath(
            "SecRandom/Views/SettingsPages/Notification/DefaultNotificationSettingsPage.axaml");
    }

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
