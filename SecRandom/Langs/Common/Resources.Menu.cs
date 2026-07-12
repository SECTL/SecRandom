namespace SecRandom.Langs.Common;

public partial class Resources
{
    private static string Text(string key) => ResourceManager.GetString(key, Culture) ?? key;

    public static string Menu_ToggleMainWindow => Text(nameof(Menu_ToggleMainWindow));
    public static string Menu_ToggleFloatingWindow => Text(nameof(Menu_ToggleFloatingWindow));
    public static string Menu_ShowMainWindow => Text(nameof(Menu_ShowMainWindow));
    public static string Menu_ShowFloatingWindow => Text(nameof(Menu_ShowFloatingWindow));
    public static string Menu_HideFloatingWindow => Text(nameof(Menu_HideFloatingWindow));
}
