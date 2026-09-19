using Avalonia.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Services.Security;

internal sealed class SecurityDialogHost : Window
{
    public SecurityDialogHost()
    {
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        CanResize = false;
        Width = 1;
        Height = 1;
        Opacity = 0;
        IsVisible = false;
        var featureService = IAppHost.TryGetService<IWindowFeatureService>();
        featureService?.Apply(
            new PlatformWindowHandle(nint.Zero, null),
            new WindowFeatureRequest(WindowFeatures.ToolWindow, true));
    }
}
