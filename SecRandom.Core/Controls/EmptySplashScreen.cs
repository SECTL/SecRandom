using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentAvalonia.UI.Windowing;

namespace SecRandom.Core.Controls;

public class EmptySplashScreen : IFAApplicationSplashScreen
{
    public async Task RunTasks(CancellationToken cancellationToken)
    {
    }

    public string AppName => "SecRandom";

    public IImage AppIcon { get; } =
        new Bitmap(AssetLoader.Open(new Uri("avares://SecRandom/Assets/AppLogo.png")));

    public object? SplashScreenContent => null;
    public int MinimumShowTime => 1000;
}