using System.Globalization;
using System.Resources;
using SecRandom.Langs.SettingsPages.Debug;

namespace SecRandom.Core.Tests;

public class DebugLocalizationTests
{
    [Theory]
    [InlineData("zh-Hans", "调试")]
    [InlineData("en-US", "Debug")]
    [InlineData("ja-JP", "デバッグ")]
    public void DebugPageTitleMatchesTheSelectedLanguage(string cultureName, string expectedTitle)
    {
        var resourceManager = new ResourceManager(
            "SecRandom.Langs.SettingsPages.Debug.Resources", typeof(DebugStrings).Assembly);

        string? title = resourceManager.GetString("Page_Title", CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expectedTitle, title);
    }
}
