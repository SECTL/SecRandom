using System.Reflection;
using Avalonia.Media;
using ClassIsland;

namespace SecRandom.Core;

public static class GlobalConstants
{
    public static string Tag => GitInfo.Tag;
    public static string Branch => GitInfo.Branch;
    public static string CommitHash => GitInfo.CommitHash[..7];
    public static string FullCommitHash => GitInfo.CommitHash;
    
    public static string CodeName => "Nonomi";
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version!.ToString();
    public static string DisplayVersion => $"{Version} (Codename {CodeName})";
    public static string VersionLong => $"{Version}-{CodeName}-{CommitHash}({Branch})";

    public static string PlatformExecutableExtension => System.OperatingSystem.IsWindows() ? ".exe" : "";
    
#if DEBUG
    public static bool IsDevelopment => true;
#else
    public static bool IsDevelopment => false;
#endif
    
    public static FontFamily FluentIconsFontFamily =
        new("avares://SecRandom/Assets/Fonts/#FluentSystemIcons-Resizable");
}
