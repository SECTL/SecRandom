using SecRandom.Langs.SettingsPages.General.Verification;

namespace SecRandom.Services.Verification;

public sealed class FormalNotarizationException(string errorCode, string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;

    public static FormalNotarizationException UnsupportedForOrchestration() => new(
        "formal_notarization_unsupported_for_orchestration",
        Resources.M_FormalNotarizationUnsupportedForOrchestration);

    public static FormalNotarizationException RosterDigestMissing() => new(
        "formal_notarization_roster_digest_missing",
        Resources.M_FormalNotarizationRosterMissing);

    public static FormalNotarizationException AlgorithmUnsupported() => new(
        "formal_notarization_algorithm_unsupported",
        Resources.M_FormalNotarizationAlgorithmUnsupported);
}
