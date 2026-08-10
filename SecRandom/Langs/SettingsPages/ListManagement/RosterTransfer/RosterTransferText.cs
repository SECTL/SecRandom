using System.Globalization;
using System.Resources;

namespace SecRandom.Langs.SettingsPages.ListManagement.RosterTransfer;

public static class RosterTransferText
{
    private static readonly ResourceManager ResourceManager = new(
        "SecRandom.Langs.SettingsPages.ListManagement.RosterTransfer.Resources",
        typeof(RosterTransferText).Assembly);

    public static string Get(string name) => ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
