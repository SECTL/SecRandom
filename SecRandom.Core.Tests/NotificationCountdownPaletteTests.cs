using System.Text.Json;
using Avalonia.Media;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Tests;

public class NotificationCountdownPaletteTests
{
    [Fact]
    public void DefaultsRunFromBlueToRed()
    {
        Assert.Equal(Color.Parse("#2E8BFF"), NotificationCountdownPalette.DefaultStart);
        Assert.Equal(Color.Parse("#EF4444"), NotificationCountdownPalette.DefaultEnd);
    }

    [Fact]
    public void SampleHitsBothEndsAndTheMiddle()
    {
        var start = Color.FromArgb(255, 0, 0, 0);
        var end = Color.FromArgb(255, 100, 100, 100);

        Assert.Equal(start, NotificationCountdownPalette.Sample(start, end, 0));
        Assert.Equal(end, NotificationCountdownPalette.Sample(start, end, 1));
        Assert.Equal(Color.FromArgb(255, 50, 50, 50), NotificationCountdownPalette.Sample(start, end, 0.5));
        Assert.Equal(start, NotificationCountdownPalette.Sample(start, end, -1));
        Assert.Equal(end, NotificationCountdownPalette.Sample(start, end, 2));
    }

    [Fact]
    public void SampleAlwaysYieldsOpaqueColors()
    {
        var translucent = Color.FromArgb(10, 1, 2, 3);

        var color = NotificationCountdownPalette.Sample(translucent, translucent, 0.5);

        Assert.Equal((byte)255, color.A);
        Assert.Equal((byte)1, color.R);
    }

    [Fact]
    public void SampleWithIdenticalEndsIsThatColor()
    {
        var color = Color.FromArgb(40, 1, 2, 3);
        var expected = NotificationCountdownPalette.ToOpaque(color);

        Assert.Equal(expected, NotificationCountdownPalette.Sample(color, color, 0.4));
        Assert.Equal(expected, NotificationCountdownPalette.Sample(color, color, 1));
    }
}

public class NotificationCountdownConfigTests
{
    [Fact]
    public void CountdownDefaultsToTheEdgeOutline()
    {
        NotificationChannelSettings settings = new();

        Assert.Equal((int)NotificationCountdownStyle.Edge, settings.CountdownStyle);
        Assert.True(settings.UsesEdgeCountdown);
        Assert.Equal((int)NotificationCountdownColorMode.Gradient, settings.CountdownColorMode);
        Assert.Equal(NotificationCountdownPalette.DefaultStart, settings.CountdownGradientStart);
        Assert.Equal(NotificationCountdownPalette.DefaultEnd, settings.CountdownGradientEnd);
        Assert.Equal(NotificationCountdownPalette.DefaultStart, settings.ResolvedCountdownStart);
        Assert.Equal(NotificationCountdownPalette.DefaultEnd, settings.ResolvedCountdownEnd);
    }

    [Fact]
    public void SolidColorModeResolvesBothEndsToTheSingleColor()
    {
        NotificationChannelSettings settings = new()
        {
            CountdownColorMode = (int)NotificationCountdownColorMode.Solid,
            CountdownSolidColor = Color.FromArgb(10, 1, 2, 3)
        };

        Assert.Equal((byte)255, settings.ResolvedCountdownStart.A);
        Assert.Equal((byte)1, settings.ResolvedCountdownStart.R);
        Assert.Equal(settings.ResolvedCountdownStart, settings.ResolvedCountdownEnd);
    }

    [Fact]
    public void GradientEndpointsAreStoredOpaque()
    {
        NotificationChannelSettings settings = new()
        {
            CountdownGradientStart = Color.FromArgb(12, 10, 20, 30),
            CountdownGradientEnd = Color.FromArgb(34, 40, 50, 60)
        };

        Assert.Equal((byte)255, settings.CountdownGradientStart.A);
        Assert.Equal((byte)255, settings.CountdownGradientEnd.A);
    }

    [Fact]
    public void CountdownStyleRejectsUnknownValues()
    {
        NotificationChannelSettings settings = new()
        {
            CountdownStyle = 9,
            CountdownColorMode = 7
        };

        Assert.Equal((int)NotificationCountdownStyle.Edge, settings.CountdownStyle);
        Assert.Equal((int)NotificationCountdownColorMode.Gradient, settings.CountdownColorMode);
    }

    [Fact]
    public void DeserializationMigratesLegacyColorStopListToItsEndpoints()
    {
        const string json = """
                            {
                              "countdown_style": 9,
                              "countdown_color_mode": 7,
                              "countdown_gradient_colors": ["#112233", "#445566", "#778899"]
                            }
                            """;

        NotificationChannelSettings? settings = JsonSerializer.Deserialize<NotificationChannelSettings>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.Equal((int)NotificationCountdownStyle.Edge, settings.CountdownStyle);
        Assert.Equal(Color.Parse("#112233"), settings.CountdownGradientStart);
        Assert.Equal(Color.Parse("#778899"), settings.CountdownGradientEnd);
    }

    [Fact]
    public void CountdownSettingsRoundTripThroughJson()
    {
        NotificationChannelSettings settings = new()
        {
            CountdownStyle = (int)NotificationCountdownStyle.Text,
            CountdownColorMode = (int)NotificationCountdownColorMode.Solid,
            CountdownSolidColor = Colors.Orange,
            CountdownGradientStart = Color.FromArgb(255, 1, 2, 3),
            CountdownGradientEnd = Color.FromArgb(255, 4, 5, 6)
        };

        var json = JsonSerializer.Serialize(settings, ConfigServiceBase.JsonOptions);
        NotificationChannelSettings? restored = JsonSerializer.Deserialize<NotificationChannelSettings>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.Contains("\"countdown_style\"", json, StringComparison.Ordinal);
        Assert.Contains("\"countdown_gradient_start\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("countdown_gradient_colors", json, StringComparison.Ordinal);
        Assert.NotNull(restored);
        Assert.Equal((int)NotificationCountdownStyle.Text, restored.CountdownStyle);
        Assert.False(restored.UsesEdgeCountdown);
        Assert.Equal((int)NotificationCountdownColorMode.Solid, restored.CountdownColorMode);
        Assert.Equal(NotificationCountdownPalette.ToOpaque(Colors.Orange), restored.CountdownSolidColor);
        Assert.Equal(settings.CountdownGradientStart, restored.CountdownGradientStart);
        Assert.Equal(settings.CountdownGradientEnd, restored.CountdownGradientEnd);
    }
}