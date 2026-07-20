namespace SecRandom.Mobile.Views;

public static class MobileRoutes
{
    public const string Draw = "main.rollCall";
    public const string History = "main.history";
    public const string Overview = "main.overview";
    public const string Settings = "settings.mobile";
    public const string General = "settings.mobile.general";
    public const string Personalization = "settings.mobile.personalization";
    public const string ListManagement = "settings.mobile.listManagement";
    public const string DrawSettings = "settings.mobile.draw";
    public const string Backup = "settings.mobile.backup";
    public const string Update = "settings.mobile.update";
    public const string About = "settings.mobile.about";

    public static IReadOnlyList<string> All { get; } =
    [
        Draw,
        History,
        Overview,
        Settings,
        General,
        Personalization,
        ListManagement,
        DrawSettings,
        Backup,
        Update,
        About
    ];
}

public enum MobileDestination
{
    Draw,
    History,
    Overview,
    Settings
}

internal enum DrawSurface
{
    RollCall,
    Lottery
}
