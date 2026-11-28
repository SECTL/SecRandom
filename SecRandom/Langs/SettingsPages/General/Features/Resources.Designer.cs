#nullable enable

namespace SecRandom.Langs.SettingsPages.General.Features;

public class Resources
{
    private static System.Resources.ResourceManager? _resourceManager;
    private static System.Globalization.CultureInfo? _resourceCulture;

    public static System.Resources.ResourceManager ResourceManager =>
        _resourceManager ??= new System.Resources.ResourceManager(
            "SecRandom.Langs.SettingsPages.General.Features.Resources", typeof(Resources).Assembly);

    public static System.Globalization.CultureInfo? Culture
    {
        get => _resourceCulture;
        set => _resourceCulture = value;
    }
}
