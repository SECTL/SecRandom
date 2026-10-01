namespace SecRandom.Services.Security;

public static class SettingsIntegrityCommandGate
{
    public static bool IsAllowed(bool confirmationPending) => !confirmationPending;

    public static bool TryRun(bool confirmationPending, Action command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsAllowed(confirmationPending))
            return false;

        command();
        return true;
    }
}
