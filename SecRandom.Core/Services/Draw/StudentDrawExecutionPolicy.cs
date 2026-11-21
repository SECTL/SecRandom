using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public sealed record FairDrawPolicySnapshot(
    bool FairDraw,
    bool FairDrawGroup,
    bool FairDrawGender,
    bool FairDrawTime,
    FrequencyFunctionMode FrequencyFunction,
    double FrequencyWeight,
    bool EnableAvgGapProtection,
    int GapThreshold,
    bool ShieldEnabled,
    int ShieldTime,
    ShieldTimeUnit ShieldTimeUnit,
    bool ColdStartEnabled,
    int ColdStartRounds,
    double BaseWeight,
    double MinWeight,
    double MaxWeight,
    double GroupWeight,
    double GenderWeight,
    double TimeWeight)
{
    /// <summary>
    ///     Batch size used to scale dimension horizons. Public callers retain the
    ///     single-draw default; the draw pipeline overwrites this for multi-pick draws.
    /// </summary>
    public int BatchSize { get; init; } = 1;

    /// <summary>
    ///     Fair-draw strategy. Defaults to the historical hard average gate so every caller that only supplies
    ///     the positional fields keeps yesterday's behaviour.
    /// </summary>
    public FairDrawMode Mode { get; init; } = FairDrawMode.UniformFirst;

    /// <summary>
    ///     How many winners the draw has to produce. Only <see cref="FairDrawMode.RandomFirst" /> reads it: the
    ///     zero-probability threshold is relaxed just enough that this many candidates stay drawable, so a
    ///     multi-pick draw never starves on its own average gate. Public callers keep the single-winner default.
    /// </summary>
    public int RequiredCount { get; init; } = 1;

    public static FairDrawPolicySnapshot FromConfig(FairDrawSettingsConfig settings)
    {
        return new FairDrawPolicySnapshot(
            settings.FairDraw,
            settings.FairDrawGroup,
            settings.FairDrawGender,
            settings.FairDrawTime,
            settings.FrequencyFunction,
            settings.FrequencyWeight,
            settings.EnableAvgGapProtection,
            settings.GapThreshold,
            settings.ShieldEnabled,
            settings.ShieldTime,
            settings.ShieldTimeUnit,
            settings.ColdStartEnabled,
            settings.ColdStartRounds,
            settings.BaseWeight,
            settings.MinWeight,
            settings.MaxWeight,
            settings.GroupWeight,
            settings.GenderWeight,
            settings.TimeWeight)
        {
            Mode = settings.Mode
        };
    }

    public static FairDrawPolicySnapshot MobileDesktopDefaultsV1 { get; } = new(
        true,
        true,
        true,
        true,
        FrequencyFunctionMode.SquareRoot,
        1.0,
        true,
        1,
        false,
        0,
        ShieldTimeUnit.Minutes,
        true,
        10,
        1.0,
        0.5,
        5.0,
        0.8,
        0.8,
        0.5);
}

internal sealed record StudentDrawExecutionPolicy(
    string Name,
    int Version,
    DrawType DrawType,
    FairDrawPolicySnapshot FairDrawSettings,
    string? AlgorithmId = null)
{
    public static StudentDrawExecutionPolicy DesktopConfigured(DrawType drawType, FairDrawSettingsConfig settings,
        string? algorithmId = null)
    {
        return new StudentDrawExecutionPolicy("DesktopConfigured", 1, drawType, FairDrawPolicySnapshot.FromConfig(settings), algorithmId);
    }

    public static StudentDrawExecutionPolicy MobileDesktopDefaultsV1(DrawType drawType)
    {
        return new StudentDrawExecutionPolicy("MobileDesktopDefaults", 1, drawType, FairDrawPolicySnapshot.MobileDesktopDefaultsV1);
    }
}

internal sealed record DrawPreparedStudentsSnapshot(
    IReadOnlyList<Student> UsableCandidates,
    IReadOnlyList<WeightedCandidate<Student>> WeightedCandidates,
    IReadOnlyDictionary<Student, History> HistoryCache);
