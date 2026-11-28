using System.Globalization;
using SecRandom.Langs.SettingsPages.General.Features;

namespace SecRandom.Views;

internal static class FeatureConfigurationText
{
    public static string Get(string key) => Resources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
