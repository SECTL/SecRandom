namespace SecRandom.Shared.DataRoot;

/// <summary>
///     Migration hand-off between the process entry point and the Avalonia application. The entry point parses
///     <c>--data-root-migration</c> before Avalonia exists; the app then draws the migration window instead of the
///     normal shell. Nothing else may set this: a normal launch must never enter migration mode by accident.
/// </summary>
public static class DataRootLaunchState
{
    public static Guid? MigrationId { get; set; }

    public static bool IsMigration => MigrationId is not null;
}