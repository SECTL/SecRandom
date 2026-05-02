using Avalonia.Media;

namespace SecRandom.Core;

public static class GlobalConstants
{
    public static string Tag => GitInfo.Tag;
    public static string Branch => GitInfo.Branch;
    public static string CommitHash => GitInfo.CommitHash[..7];
    public static string FullCommitHash => GitInfo.CommitHash;
    
    public static string CodeName => "Nonomi";
    public static string Version => $"v{Tag}";
    public static string DisplayVersion => $"{Version} (Codename {CodeName})";
    public static string VersionLong => $"{Version}-{CodeName}-{CommitHash}({Branch})";

    public static string PlatformExecutableExtension => OperatingSystem.IsWindows() ? ".exe" : "";

    public const string BehindSceneAttachedSettings = "F45DFB95-7D20-4BAB-86A3-8864BBDFCE9E";

    public const string DefaultThemeColor = "#66CCFF";  // 天依蓝
    public const string DefaultFontFamily = "MiSans";
    
#if DEBUG
    public static bool IsDevelopment => true;
#else
    public static bool IsDevelopment => false;
#endif
    
    public static FontFamily FluentIconsFontFamily =
        new("avares://SecRandom/Assets/Fonts/#FluentSystemIcons-Resizable");
}
