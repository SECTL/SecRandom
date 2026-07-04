using System;
using System.IO;
using System.Text.Json;
using SecRandom.Core.Abstraction;

namespace SecRandom.Services.CrashRecovery;

public sealed record CrashRecoveryGuardState(DateTimeOffset LastAutomaticRestartAt);

public static class CrashRecoveryRestartGuard
{
    public static readonly TimeSpan RestartBackoff = TimeSpan.FromMinutes(2);

    public static bool TryBeginAutomaticRestart(string statePath, DateTimeOffset now)
    {
        try
        {
            if (!CanBeginAutomaticRestart(statePath, now))
                return false;

            string? directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                statePath,
                JsonSerializer.Serialize(new CrashRecoveryGuardState(now), ConfigServiceBase.JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool CanBeginAutomaticRestart(string statePath, DateTimeOffset now)
    {
        try
        {
            CrashRecoveryGuardState? state = ReadState(statePath);
            return state is null || now - state.LastAutomaticRestartAt >= RestartBackoff;
        }
        catch
        {
            return false;
        }
    }

    private static CrashRecoveryGuardState? ReadState(string statePath)
    {
        if (!File.Exists(statePath))
            return null;

        string json = File.ReadAllText(statePath);
        return JsonSerializer.Deserialize<CrashRecoveryGuardState>(json, ConfigServiceBase.JsonOptions);
    }
}
