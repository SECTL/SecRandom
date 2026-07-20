using System;

namespace SecRandom.Services.CrashRecovery;

/// <summary>
/// Supplies the next runtime crash prompt to the MVE-created view without exposing the
/// application lifetime through the Core view-engine contracts.
/// </summary>
public sealed class CrashRecoveryViewState
{
    private CrashRecoveryPromptOptions _options = new(string.Empty, false);
    private Func<bool> _restartApp = static () => false;

    public void Configure(CrashRecoveryPromptOptions options, Func<bool> restartApp, bool canIgnore)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(restartApp);
        _options = options;
        _restartApp = restartApp;
        CanIgnore = canIgnore;
    }

    public CrashRecoveryPromptOptions Options => _options;
    public Func<bool> RestartApp => _restartApp;
    public bool CanIgnore { get; private set; } = true;
}
