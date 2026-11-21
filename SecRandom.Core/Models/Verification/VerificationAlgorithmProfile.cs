namespace SecRandom.Core.Models.Verification;

/// <summary>
///     Identifies the candidate-pool policy committed by a verifiable draw.
/// </summary>
public enum VerificationAlgorithmProfile : byte
{
    StudentFairRepeat = 1,
    StudentFairNoRepeat = 2,
    StudentFairHalfRepeat = 3,
    StudentRandomRepeat = 4,
    StudentRandomNoRepeat = 5,
    StudentRandomHalfRepeat = 6,
    LotteryInventoryCount = 7,
    LotteryCountInternalRule = 8,
    LotteryPanRepeat = 9,
    LotteryPanNoRepeat = 10,
    LotteryPanHalfRepeat = 11,
    StudentFairInternalRuleRepeat = 12,
    StudentFairInternalRuleNoRepeat = 13,
    StudentFairInternalRuleHalfRepeat = 14,
    StudentRandomInternalRuleRepeat = 15,
    StudentRandomInternalRuleNoRepeat = 16,
    StudentRandomInternalRuleHalfRepeat = 17,
    NumberUniformPermutation = 18,

    /// <summary>
    ///     The round's order was arranged before the draw. Replayed from the committed positions with
    ///     <see cref="VerificationSamplingMode.SequentialOrder" />, and declared to the verifier through
    ///     <c>fairness.repeatMode = "orchestrated"</c> plus the <c>fairness.orchestration</c> counters.
    /// </summary>
    StudentOrchestrated = 19,

    /// <summary>Prize equivalent of <see cref="StudentOrchestrated" />.</summary>
    LotteryOrchestrated = 20
}
