using Avalonia.Styling;
using SecRandom.Core.Enums.Configs;
using AvaloniaApplication = Avalonia.Application;

namespace SecRandom.Mobile.Views;

internal static class MobileResources
{
    internal static void Apply(ThemeMode theme)
    {
        AvaloniaApplication.Current!.RequestedThemeVariant = theme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

}
