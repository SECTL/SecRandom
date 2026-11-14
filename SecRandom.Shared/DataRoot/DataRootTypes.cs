namespace SecRandom.Shared.DataRoot;

/// <summary>
///     Where the desktop data root is expected to live. The mode is persisted in the anchor file so a later
///     start does not have to re-derive it from the package layout.
/// </summary>
public enum DataRootMode
{
    /// <summary>Data lives beside the portable payload (<c>&lt;package root&gt;/data</c>).</summary>
    Package = 0,

    /// <summary>Data lives in the platform per-user application data directory.</summary>
    Standard = 1,

    /// <summary>Data lives wherever the user picked.</summary>
    Custom = 2
}

/// <summary>Why this process ended up with the data root it has; shown in diagnostics and the storage page.</summary>
public enum DataRootSource
{
    Unknown = 0,

    /// <summary>A <c>--data-root</c> command-line argument won.</summary>
    CommandLine = 1,

    /// <summary>The <c>SECRANDOM_DATA_ROOT</c> environment variable won.</summary>
    Environment = 2,

    /// <summary>The persisted anchor file won.</summary>
    Anchor = 3,

    /// <summary>An older data location was found and claimed, because the new default was empty.</summary>
    Legacy = 4,

    /// <summary>Data was found inside a versioned payload directory and folded back out before use.</summary>
    RepairedPayload = 5,

    /// <summary>The mode default was used and no older data existed.</summary>
    ModeDefault = 6
}

public static class DataRootModeExtensions
{
    public static string ToStorageValue(this DataRootMode mode)
    {
        return mode switch
        {
            DataRootMode.Package => "package",
            DataRootMode.Standard => "standard",
            _ => "custom"
        };
    }

    public static DataRootMode Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "package" => DataRootMode.Package,
            "standard" => DataRootMode.Standard,
            _ => DataRootMode.Custom
        };
    }

    public static string ToDisplayKey(this DataRootSource source)
    {
        return source switch
        {
            DataRootSource.CommandLine => "command-line",
            DataRootSource.Environment => "environment",
            DataRootSource.Anchor => "anchor",
            DataRootSource.Legacy => "legacy",
            DataRootSource.RepairedPayload => "repaired",
            DataRootSource.ModeDefault => "default",
            _ => "unknown"
        };
    }
}

/// <summary>
///     The persisted data-root anchor. It deliberately lives outside the data directory: it must stay readable
///     before the root is known, and it must never travel inside a backup, export, or cloud archive.
/// </summary>
public sealed class DataRootAnchor
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>One of <c>package</c>, <c>standard</c>, or <c>custom</c>.</summary>
    public string Mode { get; set; } = "standard";

    public string DataRoot { get; set; } = string.Empty;

    /// <summary>The package marker kind that wrote this anchor, when the package carried one.</summary>
    public string? PackageKind { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum DataRootAnchorStatus
{
    Missing = 0,
    Valid = 1,
    Invalid = 2
}

public readonly record struct DataRootAnchorReadResult(
    DataRootAnchorStatus Status,
    DataRootAnchor? Anchor,
    string? ErrorMessage);

/// <summary>Everything the desktop resolver needs; all optional values exist so tests and tools can pin them.</summary>
public sealed record DesktopDataRootOptions
{
    /// <summary>Directory that holds the running payload; defaults to <see cref="AppContext.BaseDirectory" />.</summary>
    public string? AppDirectory { get; init; }

    /// <summary>Package root override (normally <c>SECRANDOM_PACKAGE_ROOT</c>).</summary>
    public string? PackageRoot { get; init; }

    /// <summary>Value of <c>--data-root</c>, when present.</summary>
    public string? CommandLineDataRoot { get; init; }

    /// <summary>Value of <c>SECRANDOM_DATA_ROOT</c>, when present.</summary>
    public string? EnvironmentDataRoot { get; init; }

    /// <summary>Whether this build is a development build; development builds keep data beside the payload.</summary>
    public bool IsDevelopmentBuild { get; init; }

    /// <summary>Test/tool override for the anchor file path.</summary>
    public string? AnchorPath { get; init; }

    /// <summary>Test/tool override for the platform standard data root.</summary>
    public string? StandardDataRoot { get; init; }

    /// <summary>Test/tool override for the platform standard anchor directory.</summary>
    public string? StandardAnchorDirectory { get; init; }

    /// <summary>Disable persistence for tests that must not touch the user profile.</summary>
    public bool WriteAnchor { get; init; } = true;

    /// <summary>Package marker kind, when already known; otherwise the resolver reads it from the payload.</summary>
    public string? PackageKind { get; init; }
}

/// <summary>The pure resolution result, before any directory is created or write-probed.</summary>
public sealed record DesktopDataRootResolution
{
    public required string AppDirectory { get; init; }
    public required string PackageRoot { get; init; }
    public required bool IsPortablePackage { get; init; }
    public required DataRootMode Mode { get; init; }
    public required DataRootSource Source { get; init; }
    public required string DataRoot { get; init; }
    public required string AnchorPath { get; init; }
    public string? PackageKind { get; init; }

    /// <summary>A payload-local data directory that must be folded into <see cref="DataRoot" /> before use.</summary>
    public string? PayloadDataRootToRepair { get; init; }

    /// <summary>Other known data locations that still hold data; never merged automatically.</summary>
    public IReadOnlyList<string> OtherDataRoots { get; init; } = [];

    /// <summary>True when the resolved root is not the location this mode would create today.</summary>
    public bool NeedsMigrationNotice { get; init; }
}