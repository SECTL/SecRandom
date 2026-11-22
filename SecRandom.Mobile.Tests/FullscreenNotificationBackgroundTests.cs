using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Services.Config;
using SecRandom.Views;

namespace SecRandom.Mobile.Tests;

public sealed class FullscreenNotificationBackgroundTests
{
    [AvaloniaFact]
    public void SwitchingBackgroundModesKeepsTheirOpacitySeparateFromTheContent()
    {
        var config = new MainConfigModel();
        var settings = config.NotificationSettings.Fullscreen;
        settings.ThemeBackgroundOpacity = 70;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        var seed = new FullscreenPresentationSeed(NotificationSettingsType.RollCall, [], 0, 0);
        var window = new FullscreenNotificationWindow(handler, seed.Kind);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            ISolidColorBrush Apply(FullscreenBackgroundMode mode)
            {
                settings.BackgroundMode = mode;
                window.BeginIdle(seed);
                return Assert.IsAssignableFrom<ISolidColorBrush>(
                    window.FindControl<Border>("FallbackBackground")!.Background);
            }

            Assert.Equal(0.70, Apply(FullscreenBackgroundMode.Theme).Opacity, 3);
            Assert.Equal(1, Apply(FullscreenBackgroundMode.Image).Opacity);
            Assert.Equal(1, window.FindControl<Border>("RootBorder")!.Opacity);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
