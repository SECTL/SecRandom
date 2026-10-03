using Avalonia.Media;
using System.Reflection;

namespace SecRandom.Core;

public static class GlobalConstants
{
    private static readonly object VersionSourceGate = new();
    private static Assembly? _versionAssembly;
    private static VersionMetadata? _versionMetadata;

    /// <summary>
    ///     Assembly this build's version metadata is read from. The metadata is compiled into the platform head
    ///     assembly (root AssemblyInfo.cs plus the GitInfo generator), and Android has no managed entry point, so
    ///     <see cref="Assembly.GetEntryAssembly" /> returns null there and the head must publish itself through
    ///     <see cref="SetVersionAssembly" />; otherwise the version falls back to this library, which carries no
    ///     build metadata and renders as v0.0.0.0.
    /// </summary>
    private static Assembly VersionAssembly =>
        _versionAssembly ?? Assembly.GetEntryAssembly() ?? typeof(GlobalConstants).Assembly;

    private static VersionMetadata Metadata
    {
        get
        {
            var cached = _versionMetadata;
            if (cached is not null)
                return cached;

            lock (VersionSourceGate)
                return _versionMetadata ??= ReadMetadata(VersionAssembly);
        }
    }

    internal static Assembly VersionSource => VersionAssembly;

    /// <summary>
    ///     Publishes the platform head assembly that carries this build's
    ///     <see cref="AssemblyInformationalVersionAttribute" />. Platform heads call this during startup, before any
    ///     version value is read; re-publishing clears the cached metadata.
    /// </summary>
    public static void SetVersionAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (VersionSourceGate)
        {
            _versionAssembly = assembly;
            _versionMetadata = null;
        }
    }

    public static string Tag => Metadata.Tag;
    public static string Branch => Metadata.Branch;
    public static string CommitHash => Metadata.CommitHash[..Math.Min(7, Metadata.CommitHash.Length)];
    public static string FullCommitHash => Metadata.CommitHash;

    public static string CodeName => @"Nonomi";
    public static string Version => Tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? Tag : $@"v{Tag}";
    public static string AssemblyVersion => VersionSource
        .GetCustomAttribute<AssemblyVersionAttribute>()?.Version ?? "0.0.0.0";
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

    internal static VersionMetadata ReadMetadata(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return new VersionMetadata("0.0.0.0", "Unknown", "Unknown");

        var separator = informationalVersion.IndexOf('+');
        var generatedGitInfo = assembly.GetType("SecRandom.GitInfo");
        var branch = generatedGitInfo?.GetField("Branch", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string
                     ?? generatedGitInfo?.GetProperty("Branch", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string
                     ?? "Unknown";
        return separator < 0
            ? new VersionMetadata(informationalVersion, branch, "Unknown")
            : new VersionMetadata(informationalVersion[..separator], branch, informationalVersion[(separator + 1)..]);
    }
}

internal sealed record VersionMetadata(string Tag, string Branch, string CommitHash);
