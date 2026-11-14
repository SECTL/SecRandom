namespace SecRandom.Shared.DataRoot;

/// <summary>
///     Parses the desktop launch options that select or migrate the data root. Both options are stripped from the
///     arguments the rest of the app sees, because Avalonia, crash recovery, and plugins must never interpret them.
/// </summary>
public sealed class DataRootLaunchArguments
{
    public const string DataRootOption = "--data-root";
    public const string MigrationOption = "--data-root-migration";

    /// <summary>Value of <c>--data-root</c>, if the launch carried one.</summary>
    public string? DataRoot { get; init; }

    /// <summary>Migration transaction id of <c>--data-root-migration</c>, if the launch carries one.</summary>
    public Guid? MigrationId { get; init; }

    /// <summary>Arguments with the data-root options removed, in their original order.</summary>
    public IReadOnlyList<string> PassThroughArguments { get; init; } = [];

    /// <summary>A malformed option value; startup reports it instead of silently ignoring the request.</summary>
    public string? Error { get; init; }

    public bool IsMigration => MigrationId is not null;
}

public static class DataRootCommandLine
{
    public static DataRootLaunchArguments Parse(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return new DataRootLaunchArguments { PassThroughArguments = [] };

        string? dataRoot = null;
        string? migrationValue = null;
        string? error = null;
        var remaining = new List<string>(arguments.Count);

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (TryReadOption(argument, DataRootLaunchArguments.DataRootOption, arguments, ref index, out var value))
            {
                if (string.IsNullOrWhiteSpace(value))
                    error ??= $"{DataRootLaunchArguments.DataRootOption} requires a directory.";
                else
                    dataRoot = value;
                continue;
            }

            if (TryReadOption(argument, DataRootLaunchArguments.MigrationOption, arguments, ref index, out var migrationText))
            {
                if (Guid.TryParse(migrationText, out var migrationId))
                    migrationValue = migrationId.ToString("N");
                else
                    error ??= $"{DataRootLaunchArguments.MigrationOption} requires a migration transaction id.";
                continue;
            }

            remaining.Add(argument);
        }

        return new DataRootLaunchArguments
        {
            DataRoot = string.IsNullOrWhiteSpace(dataRoot) ? null : dataRoot,
            MigrationId = migrationValue is null ? null : Guid.ParseExact(migrationValue, "N"),
            PassThroughArguments = remaining,
            Error = error
        };
    }

    /// <summary>Writes the resolved custom root to the child-process environment so a relaunch keeps the same root.</summary>
    public const string DataRootEnvironmentVariable = "SECRANDOM_DATA_ROOT";

    private static bool TryReadOption(
        string argument,
        string option,
        IReadOnlyList<string> arguments,
        ref int index,
        out string? value)
    {
        value = null;

        if (string.Equals(argument, option, StringComparison.Ordinal))
        {
            if (index + 1 >= arguments.Count)
                return true;

            value = arguments[++index];
            return true;
        }

        var prefix = option + "=";
        if (!argument.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        value = argument[prefix.Length..];
        return true;
    }
}