using System.Reflection;
using Avalonia.Media;
using SecRandom.Core;
using SecRandom.Core.Controls;

namespace SecRandom.Core.Tests;

public class FontComboBoxTests
{
    [Fact]
    public void BuildFontFamilies_SkipsFontFamiliesThatCannotBeValidated()
    {
        var validFont = new FontFamily("Arial");
        var invalidFont = new FontFamily("Invalid Font");

        var buildFontFamilies = typeof(FontComboBox).GetMethod(
            "BuildFontFamilies",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(buildFontFamilies);

        var fontFamilies = Assert.IsType<List<FontFamily>>(buildFontFamilies.Invoke(
            null,
            [
                new[] { validFont, invalidFont },
                (Func<FontFamily, bool>)(fontFamily =>
                {
                    if (ReferenceEquals(fontFamily, invalidFont))
                    {
                        throw new FormatException();
                    }

                    return true;
                })
            ]));

        Assert.Contains(validFont, fontFamilies);
        Assert.DoesNotContain(invalidFont, fontFamilies);
        Assert.Equal(GlobalConstants.DefaultAvaFontFamily, fontFamilies[^1]);
    }
}
