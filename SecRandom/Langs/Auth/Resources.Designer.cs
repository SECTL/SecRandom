using System.Globalization;
using System.Resources;

namespace SecRandom.Langs.Auth;

/// <summary>
///     Texts of the browser page that answers the desktop OAuth loopback redirect. It is not an Avalonia
///     surface, so the page renderer reads its copy from here instead of a ViewModel.
/// </summary>
public static class Resources
{
    private static readonly ResourceManager Manager =
        new("SecRandom.Langs.Auth.Resources", typeof(Resources).Assembly);

    private static string Text(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string C_AuthCallback_Title_Success => Text(nameof(C_AuthCallback_Title_Success));
    public static string C_AuthCallback_Lede_Success => Text(nameof(C_AuthCallback_Lede_Success));
    public static string C_AuthCallback_Title_Denied => Text(nameof(C_AuthCallback_Title_Denied));
    public static string C_AuthCallback_Lede_Denied => Text(nameof(C_AuthCallback_Lede_Denied));
    public static string C_AuthCallback_Title_Failed => Text(nameof(C_AuthCallback_Title_Failed));
    public static string C_AuthCallback_Lede_Failed => Text(nameof(C_AuthCallback_Lede_Failed));
}
