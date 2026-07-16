using Avalonia;
using SecRandom.Services.Notification;

namespace SecRandom.Core.Tests;

public class NotificationMonitorIdentifierTests
{
    [Fact]
    public void UsesDisplayNameWhenThePlatformProvidesOne()
    {
        var bounds = new PixelRect(1920, 0, 2560, 1440);

        Assert.Equal("Display 2", NotificationMonitorIdentifier.Get("Display 2", bounds));
        Assert.True(NotificationMonitorIdentifier.Matches("Display 2", bounds, "display 2"));
    }

    [Fact]
    public void UsesBoundsWhenThePlatformDoesNotProvideADisplayName()
    {
        var bounds = new PixelRect(-1920, 0, 1920, 1080);

        Assert.Equal("bounds:-1920:0:1920:1080", NotificationMonitorIdentifier.Get(null, bounds));
        Assert.True(NotificationMonitorIdentifier.Matches(null, bounds, "bounds:-1920:0:1920:1080"));
    }

    [Fact]
    public void FormatsMonitorLabelsLikeObs()
    {
        var label = NotificationMonitorIdentifier.GetLabel(
            "MNG007DA5-3",
            new PixelRect(0, 0, 2560, 1600),
            true,
            0,
            "{0}: {1}x{2} @ {3},{4}{5}",
            "（主显示器）");

        Assert.EndsWith(": 2560x1600 @ 0,0（主显示器）", label, StringComparison.Ordinal);
        Assert.DoesNotContain("Display 1", label, StringComparison.Ordinal);
    }

    [Fact]
    public void RetainsCompatibilityWithSavedBoundsIdentifier()
    {
        var bounds = new PixelRect(-1920, 0, 1920, 1080);

        Assert.True(NotificationMonitorIdentifier.Matches(
            "MNG007DA5-3",
            bounds,
            "bounds:-1920:0:1920:1080"));
    }
}
