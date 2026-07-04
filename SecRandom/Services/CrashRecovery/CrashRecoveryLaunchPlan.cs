using System;
using System.Collections.Generic;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Services.CrashRecovery;

public sealed class CrashRecoveryLaunchPlan
{
    private CrashRecoveryLaunchPlan(IReadOnlyList<string> arguments)
    {
        Arguments = arguments;
    }

    public IReadOnlyList<string> Arguments { get; }

    public static CrashRecoveryLaunchPlan? Create(
        CrashRecoveryMode mode,
        string crashReportPath,
        bool allowAutomaticRestart = true)
    {
        return mode switch
        {
            CrashRecoveryMode.RestartOnly => allowAutomaticRestart ? new CrashRecoveryLaunchPlan([]) : null,
            CrashRecoveryMode.PromptThenRestart => new CrashRecoveryLaunchPlan(
                [CrashRecoveryRuntime.CrashReportArgument, crashReportPath]),
            CrashRecoveryMode.PromptAndRestart => allowAutomaticRestart
                ? new CrashRecoveryLaunchPlan(
                    [CrashRecoveryRuntime.CrashReportArgument, crashReportPath, CrashRecoveryRuntime.CrashAutoRestartArgument])
                : new CrashRecoveryLaunchPlan([CrashRecoveryRuntime.CrashReportArgument, crashReportPath]),
            CrashRecoveryMode.None => null,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
