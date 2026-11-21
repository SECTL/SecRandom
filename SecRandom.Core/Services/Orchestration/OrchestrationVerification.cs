using System.Text.Json;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Core.Models.Verification;
using SecRandom.Shared.Models.Profile;
using VerificationDrawKind = SecRandom.Shared.Models.Verification.VerificationDrawKind;

namespace SecRandom.Core.Services.Orchestration;

/// <summary>
///     Builds the v3.6.0 verification input for a draw whose order was arranged in advance.
/// </summary>
/// <remarks>
///     <para>
///         The sampled entry points in <c>DrawEngine</c> refuse an orchestrated draw on purpose, because routing it
///         through the weighted pool would produce a proof that replays as a random draw. Here the order <i>is</i>
///         the commitment: every candidate carries its 1-based position in the weight slot, the sampler consumes no
///         random material, and the replay returns exactly those candidates in exactly that order.
///     </para>
///     <para>
///         The anonymous audit payload declares the mode (<c>repeatMode = "orchestrated"</c>) and the round counters
///         only. It never carries the schedule, the positions, the names, or the per-round weights: the service
///         replays positions out of the signed payload, and the fair.sectl.cn allow-list rejects everything else.
///     </para>
/// </remarks>
public static class OrchestrationVerification
{
    public const string AuditOperationStudent = "student";
    public const string AuditOperationPrize = "prize";

    public static VerificationDrawInput CreateStudentInput(
        IReadOnlyList<Student> ordered,
        int round,
        int roundCount,
        IReadOnlyDictionary<Guid, (int Count, DateTime? LastDrawn)>? history = null,
        string rosterDigest = "",
        string courseName = "",
        string? historyGroup = null)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        EnsureOrder(ordered);
        var (safeRound, safeRoundCount) = NormalizeRound(round, roundCount);

        var candidates = new List<VerificationCandidate>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var student = ordered[index];
            var recordId = student.RecordId != Guid.Empty
                ? student.RecordId
                : throw new ArgumentException("Every scheduled student needs a stable RecordId.", nameof(ordered));
            candidates.Add(new VerificationCandidate(recordId, 0, index + 1, false));
        }

        var audit = CreateAuditPayload(
            AuditOperationStudent,
            candidates,
            history,
            new
            {
                fairDraw = false,
                algorithmProfile = VerificationAlgorithmProfile.StudentOrchestrated.ToString(),
                repeatMode = "orchestrated",
                orchestration = new { round = safeRound, roundCount = safeRoundCount },
                historyScope = HistoryScopeName(courseName, historyGroup)
            },
            rosterDigest);

        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            SamplingMode = VerificationSamplingMode.SequentialOrder,
            AlgorithmProfile = VerificationAlgorithmProfile.StudentOrchestrated,
            Count = ordered.Count,
            Candidates = candidates,
            AuditPayload = audit,
            DrawModeToken = "orchestrated",
            HistoryScope = HistoryScopeName(courseName, historyGroup),
            OrchestrationRound = safeRound,
            OrchestrationRoundCount = safeRoundCount
        };
    }

    public static VerificationDrawInput CreatePrizeInput(
        IReadOnlyList<Prize> ordered,
        int round,
        int roundCount,
        IReadOnlyDictionary<Guid, (int Count, DateTime? LastDrawn)>? history = null,
        string rosterDigest = "")
    {
        ArgumentNullException.ThrowIfNull(ordered);
        EnsureOrder(ordered);
        var (safeRound, safeRoundCount) = NormalizeRound(round, roundCount);

        var candidates = new List<VerificationCandidate>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var prize = ordered[index];
            var recordId = prize.RecordId != Guid.Empty
                ? prize.RecordId
                : throw new ArgumentException("Every scheduled prize needs a stable RecordId.", nameof(ordered));
            candidates.Add(new VerificationCandidate(recordId, 0, index + 1, false));
        }

        var audit = CreateAuditPayload(
            AuditOperationPrize,
            candidates,
            history,
            new
            {
                samplingAlgorithm = "sequential-order",
                inventoryEntriesEqualWeight = false,
                internalRulesRequireWeightedFallback = false,
                algorithmProfile = VerificationAlgorithmProfile.LotteryOrchestrated.ToString(),
                lotteryMode = "pan",
                repeatMode = "orchestrated",
                orchestration = new { round = safeRound, roundCount = safeRoundCount }
            },
            rosterDigest);

        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Prize,
            SamplingMode = VerificationSamplingMode.SequentialOrder,
            AlgorithmProfile = VerificationAlgorithmProfile.LotteryOrchestrated,
            Count = ordered.Count,
            Candidates = candidates,
            AuditPayload = audit,
            DrawModeToken = "orchestrated",
            HistoryScope = "global",
            OrchestrationRound = safeRound,
            OrchestrationRoundCount = safeRoundCount
        };
    }

    /// <summary>
    ///     The scheduled order must be dense and unique, because the position itself is the evidence: a gapped or
    ///     repeated position would replay as a different draw than the one the teacher saw.
    /// </summary>
    private static void EnsureOrder<T>(IReadOnlyList<T> ordered)
    {
        if (ordered.Count == 0)
            throw new ArgumentException("An orchestrated draw needs at least one participant.", nameof(ordered));
        if (ordered.Count > 10_000)
            throw new ArgumentOutOfRangeException(nameof(ordered), "An orchestrated draw commits at most 10000 positions.");
    }

    private static (int Round, int RoundCount) NormalizeRound(int round, int roundCount)
    {
        var rounds = Math.Clamp(roundCount, 1, OrchestrationProfile.MaxRounds);
        return (Math.Clamp(round, 1, rounds), rounds);
    }

    // Midnight UTC of the day the draw happened on; see the sampled audit payload for why the clock time is dropped.
    private static DateTime CoarsenLastDrawn(DateTime value) =>
        DateTime.SpecifyKind(value.ToUniversalTime().Date, DateTimeKind.Utc);

    private static byte[] CreateAuditPayload(
        string operation,
        IReadOnlyList<VerificationCandidate> candidates,
        IReadOnlyDictionary<Guid, (int Count, DateTime? LastDrawn)>? history,
        object fairness,
        string rosterDigest)
    {
        var ordered = candidates
            .OrderBy(candidate => candidate.RecordId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex)
            .Select((candidate, index) =>
            {
                var figures = history is not null && history.TryGetValue(candidate.RecordId, out var entry)
                    ? entry
                    : ((int Count, DateTime? LastDrawn))(0, null);
                return new
                {
                    index,
                    recordId = candidate.RecordId,
                    // Same key the sampled audit uses: the server allow-lists `OccurrenceIndex`, and an audit payload
                    // is hashed into the proof commitment, so a stray key name makes the whole proof unverifiable.
                    candidate.OccurrenceIndex,
                    candidate.WeightMicros,
                    candidate.IsGuaranteed,
                    internalSettingApplied = false,
                    internalProbability = (double?)null,
                    historyCount = figures.Count,
                    // Same day-precision rule as the sampled student/prize audit: the exact last-drawn instant is a
                    // behavioural fingerprint, the day is the part the audit question actually needs.
                    lastDrawnUtc = figures.LastDrawn is { } lastDrawn
                        ? (DateTime?)CoarsenLastDrawn(lastDrawn)
                        : null
                };
            })
            .ToArray();

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "secrandom-anonymous-audit/v2",
            operation,
            requestedCount = candidates.Count,
            candidateCount = ordered.Length,
            internalSettingsApplied = false,
            internalCandidateCount = 0,
            internalExcludedCandidateCount = 0,
            fairness,
            rosterDigest = string.IsNullOrWhiteSpace(rosterDigest) ? null : rosterDigest,
            candidates = ordered
        });
    }

    /// <summary>
    ///     Names which slice of profile history the counts came from; the group and course names themselves never
    ///     leave the device because both are user-typed text that can carry a class or a person's name.
    /// </summary>
    private static string HistoryScopeName(string courseName, string? historyGroup) =>
        (string.IsNullOrWhiteSpace(courseName), historyGroup is null) switch
        {
            (true, true) => "global",
            (false, true) => "course",
            (true, false) => "history-group",
            _ => "course-and-history-group"
        };
}