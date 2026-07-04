using System;

namespace SecRandom.Services.CrashRecovery;

public static class DispatcherCrashRecovery
{
    public static bool TryRecover(
        Exception exception,
        Func<Exception, bool> handleFatalException,
        Action requestShutdown)
    {
        if (!handleFatalException(exception))
            return false;

        requestShutdown();
        return true;
    }

    public static bool TryRecover(
        Exception exception,
        Func<Exception, CrashRecoveryPromptOptions?> createPromptOptions,
        Func<CrashRecoveryPromptOptions, bool> showPrompt,
        Func<Exception, bool> handlePromptDisplayFailure,
        Func<Exception, bool> handleFatalException,
        Action requestShutdown)
    {
        CrashRecoveryPromptOptions? promptOptions = TryCreatePromptOptions(exception, createPromptOptions);
        if (promptOptions is not null)
        {
            try
            {
                if (showPrompt(promptOptions))
                    return true;
            }
            catch
            {
            }

            return TryRecover(exception, handlePromptDisplayFailure, requestShutdown);
        }

        return TryRecover(exception, handleFatalException, requestShutdown);
    }

    private static CrashRecoveryPromptOptions? TryCreatePromptOptions(
        Exception exception,
        Func<Exception, CrashRecoveryPromptOptions?> createPromptOptions)
    {
        try
        {
            return createPromptOptions(exception);
        }
        catch
        {
            return null;
        }
    }
}
