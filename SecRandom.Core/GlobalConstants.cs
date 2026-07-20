using Avalonia.Media;
using System.Reflection;

namespace SecRandom.Core;

public static class GlobalConstants
{
    private static readonly Assembly VersionAssembly = Assembly.GetEntryAssembly() ?? typeof(GlobalConstants).Assembly;
    private static readonly (string Tag, string Branch, string CommitHash) VersionParts = GetVersionParts();

    public static string Tag => VersionParts.Tag;
    public static string Branch => VersionParts.Branch;
    public static string CommitHash => VersionParts.CommitHash[..7];
    public static string FullCommitHash => VersionParts.CommitHash;

    public static string CodeName => @"Nonomi";
    public static string Version => $@"v{Tag}";
    public static string DisplayVersion => $@"{Version} (Codename {CodeName})";
    public static string VersionLong => $@"{Version}-{CodeName}-{CommitHash}({Branch})";

    public static string PlatformExecutableExtension => OperatingSystem.IsWindows() ? @".exe" : "";

    // 桌面与移动端遥测共用的 Sentry DSN；两端各自适配器不得再硬编码副本
    public const string SentryDsn = "https://7614b2b2fd46a451e7cb3ed670279e75@o4510689230192640.ingest.us.sentry.io/4511675887910912";
    public const string BehindSceneAttachedSettings = "F45DFB95-7D20-4BAB-86A3-8864BBDFCE9E";
    public const string SpecificAnnouncementAttachedSettings = "10F2C686-07D7-47E7-9A4F-B7A4724A6A10";
    public const string DrawImageAttachedSettings = "4C88E037-4F69-42D0-A32F-16D2827B7B6D";
    public const string DrawMusicAttachedSettings = "A16F1E84-77E8-4E09-B9EC-8BAF5C148057";

    public const string DefaultThemeColor = "#0078D4"; // 系统自带主题色蓝  66CCFF 天依蓝
    public const string DefaultFontFamily = "avares://SecRandom/Assets/Fonts/MiSans/#MiSans";

#if DEBUG
    public static bool IsDevelopment => true;
#else
    public static bool IsDevelopment => false;
#endif

    public static FontFamily FluentIconsFontFamily { get; } =
        new(@"avares://SecRandom/Assets/Fonts/#FluentSystemIcons-Resizable");

    public static FontFamily DefaultAvaFontFamily { get; } =
        new(@"avares://SecRandom/Assets/Fonts/MiSans/#MiSans");

    private static (string Tag, string Branch, string CommitHash) GetVersionParts()
    {
        var version = VersionAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
            return ("0.0.0.0", "Unknown", "Unknown");

        var separator = version.IndexOf('+');
        var generatedGitInfo = VersionAssembly.GetType("SecRandom.GitInfo");
        var branch = generatedGitInfo?.GetProperty("Branch", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string
                     ?? "Unknown";
        return separator < 0
            ? (version, branch, "Unknown")
            : (version[..separator], branch, version[(separator + 1)..]);
    }
}
