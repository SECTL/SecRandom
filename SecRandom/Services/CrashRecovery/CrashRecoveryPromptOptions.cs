using System.Collections.Generic;

namespace SecRandom.Services.CrashRecovery;

public sealed record CrashRecoveryPromptOptions(string CrashReportPath, bool AutoRestart)
{
    public static CrashRecoveryPromptOptions? Parse(IReadOnlyList<string> args)
    {
        string? crashReportPath = null;
        bool autoRestart = false;
        var hasCrashReportArgument = false;

        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == CrashRecoveryRuntime.CrashReportArgument && index + 1 < args.Count)
            {
                hasCrashReportArgument = true;
                crashReportPath = args[index + 1];
                index++;
                continue;
            }

            if (args[index] == CrashRecoveryRuntime.CrashAutoRestartArgument)
                autoRestart = true;
        }

        return hasCrashReportArgument
            ? new CrashRecoveryPromptOptions(crashReportPath ?? string.Empty, autoRestart)
            : null;
    }

    public static string[] RemoveCrashRecoveryArguments(IReadOnlyList<string> args)
    {
        List<string> startupArgs = [];

        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == CrashRecoveryRuntime.CrashReportArgument)
            {
                if (index + 1 < args.Count)
                    index++;

                continue;
            }

            if (args[index] == CrashRecoveryRuntime.CrashAutoRestartArgument)
                continue;

            startupArgs.Add(args[index]);
        }

        return [..startupArgs];
    }
}
