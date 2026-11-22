using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;
using System.Security.Cryptography;
using System.Text.Json;
using VerificationDrawKind = SecRandom.Shared.Models.Verification.VerificationDrawKind;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    // Compatibility overload retained for plugins compiled against the pre-V3 verification entry point.
    public VerificationDrawInput CreateStudentVerificationInput(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        string courseName,
        bool includeInternalRules,
        string rosterDigest = "",
        string? historyGroup = null,
        DrawMode? drawModeOverride = null) => CreateStudentVerificationInput(
            count, candidates, drawSettingsType, courseName, historyGroup, rosterDigest, drawModeOverride);

    // Compatibility overload retained for plugins compiled against the pre-V3 verification entry point.
    public VerificationDrawInput CreatePrizeVerificationInput(
        int count,
        IReadOnlyDictionary<string, int> temporaryCounts,
        bool includeInternalRules,
        string rosterDigest = "",
        string? historyGroup = null) => CreatePrizeVerificationInput(
            count, temporaryCounts, historyGroup, rosterDigest);

    public bool IsStudentOrchestratedMode(DrawSettingsType drawSettingsType, DrawMode? drawModeOverride = null) =>
        (drawModeOverride ?? GetStudentDrawMode(drawSettingsType)) == DrawMode.Orchestrated;

    public bool IsLotteryOrchestratedMode() => ConfigData.LotterySettings.DrawMode == DrawMode.Orchestrated;

    /// <summary>
    ///     Freezes the same prepared student pool used by the draw page into a deterministic verification request.
    /// </summary>
    public VerificationDrawInput CreateStudentVerificationInput(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        string courseName = "",
        string? historyGroup = null,
        string rosterDigest = "",
        DrawMode? drawModeOverride = null,
        DrawType? drawTypeOverride = null)
    {
        var preparedCandidates = candidates.Where(student => student.IsCandidate).ToList();
        if (preparedCandidates.Count == 0 || count <= 0 || count > preparedCandidates.Count)
            throw new InvalidOperationException("The prepared student pool cannot satisfy this draw.");

        var drawType = drawTypeOverride ?? GetStudentDrawType(drawSettingsType);
        // 覆盖只为一个场景存在：抽奖轮里附带的「分配学生」是这一轮自己的随机分配，点名通道即便配了编排，
        // 也不该把那轮的点名排布塞进抽奖证明里。其它调用方一律沿用配置里的模式。
        var drawMode = drawModeOverride ?? GetStudentDrawMode(drawSettingsType);
        // An orchestrated draw does not sample: the participant comes from the arranged schedule, and its proof
        // must commit those positions (StudentOrchestrated + SequentialOrder) instead of a weighted pool. Routing
        // it through the sampled entry point would produce a proof that replays as a random draw, i.e. exactly the
        // claim orchestration is supposed to avoid, so fail loudly until the orchestration session builds its own.
        if (drawMode == DrawMode.Orchestrated)
            throw new InvalidOperationException(
                "Orchestrated draws must build their verification input from the arranged schedule.");
        var executionPolicy = StudentDrawExecutionPolicy.DesktopConfigured(drawType, ConfigData.FairDrawSettings,
            drawTypeOverride == DrawType.Random ? "builtin.random" : drawSettingsType switch
            {
                DrawSettingsType.RollCall => ConfigData.RollCallSettings.AlgorithmId,
                DrawSettingsType.QuickDraw => ConfigData.QuickDrawSettings.AlgorithmId,
                _ => null
            });
        DrawPreparedStudentsSnapshot prepared;
        try
        {
            prepared = PrepareStudentsForDraw(count, preparedCandidates, executionPolicy, courseName, historyGroup);
        }
        catch (Exception exception) when (exception is CandidateNotFoundException or RepeatLimitExhaustedException)
        {
            // The repeat limit or the mandatory average-gap gate removed every prepared candidate. That is
            // the same "nothing drawable right now" state as an all-zero weight pool, so it must reach
            // callers as a status instead of failing the whole draw.
            throw new NoEligibleCandidatesException("The prepared student pool has no drawable candidate.", exception);
        }

        var frozen = FreezeCandidates(prepared.WeightedCandidates);
        if (count > frozen.Count)
            throw new InvalidOperationException("The prepared student pool cannot satisfy this draw.");
        EnsureDrawableWeight(frozen, count);
        var algorithmProfile = GetStudentAlgorithmProfile(drawType, drawMode);
        var auditPayload = CreateAuditPayload("student", count, frozen, prepared.HistoryCache, new
            {
                fairDraw = drawType == DrawType.Fair,
                algorithmProfile = algorithmProfile.ToString(),
                repeatMode = ToAuditName(drawMode),
                halfRepeatLimit = drawMode == DrawMode.HalfRepeat ? GetStudentRepeatThreshold(drawSettingsType) : (int?)null,
                // Declares which slice of the profile history the per-candidate historyCount/lastDrawnUtc below
                // were taken from. The group name itself never leaves the device: it is user-typed text that can
                // contain a class or a person's name. A narrowed scope is exactly what makes these counts legal to
                // read as "not the whole history", so it ships with the audit payload rather than being assumed.
                historyScope = ToHistoryScopeName(courseName, historyGroup),
                averageGapProtectionApplied = executionPolicy.DrawType == DrawType.Fair
                                              && executionPolicy.FairDrawSettings.Mode == FairDrawMode.UniformFirst,
                candidateCountBeforeAverageGapProtection = preparedCandidates.Count,
                candidateCountAfterAverageGapProtection = prepared.UsableCandidates.Count,
                averageGapProtectionThreshold = AverageGapProtectionThreshold,
                batchSize = count,
                // Retained for wire compatibility with the 3.x replay service. The unified weight math no longer
                // consumes these horizon/floor constants; they stay declared so an older reader still finds its
                // expected keys instead of treating the payload as truncated. The zero-probability policy of the
                // random-first mode is already visible per candidate through weightMicros = 0, which is why this
                // block needed no new field (the service validates a fixed property allow-list).
                shareDebtPersonalHorizonRounds = ShareDebtPersonalHorizonRounds,
                shareDebtDimensionHorizonPerPick = ShareDebtDimensionHorizonPerPick,
                shareDebtRandomFloor = ShareDebtRandomFloor
            }, rosterDigest);
        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            SamplingMode = drawType == DrawType.Fair
                ? VerificationSamplingMode.HistoryBalancedWeighted
                : VerificationSamplingMode.WeightedWithoutReplacement,
            AlgorithmProfile = algorithmProfile,
            Count = count,
            Candidates = frozen,
            AuditPayload = auditPayload,
            DrawModeToken = ToAuditName(drawMode),
            HistoryScope = ToHistoryScopeName(courseName, historyGroup)
        };
    }

    /// <summary>
    ///     Freezes a group draw as anonymous member candidates. The occurrence slot carries a per-proof, randomly
    ///     assigned group bucket, allowing replay to sample groups uniformly and then members uniformly without
    ///     transmitting group names or a reusable group identifier.
    /// </summary>
    public VerificationDrawInput CreateGroupVerificationInput(
        int count,
        IReadOnlyList<GroupDrawCandidates.Group> groups,
        DrawMode drawMode,
        int halfRepeat,
        string courseName = "",
        string? historyGroup = null,
        string rosterDigest = "")
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0 || count <= 0 || count > groups.Count)
            throw new InvalidOperationException("The prepared group pool cannot satisfy this draw.");

        var groupOrdinals = Enumerable.Range(0, groups.Count).Select(index => (uint)index).ToArray();
        for (var index = groupOrdinals.Length - 1; index > 0; index--)
        {
            var selected = RandomNumberGenerator.GetInt32(index + 1);
            (groupOrdinals[index], groupOrdinals[selected]) = (groupOrdinals[selected], groupOrdinals[index]);
        }

        HashSet<Guid> seenRecordIds = [];
        List<VerificationCandidate> frozen = [];
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            if (string.IsNullOrWhiteSpace(group.Name) || group.Members.Count == 0)
                throw new InvalidOperationException("Every drawable group must contain a member.");

            foreach (var student in group.Members)
            {
                var recordId = EnsureRecordId(student);
                if (!seenRecordIds.Add(recordId))
                    throw new InvalidOperationException("A student may belong to only one group in a proof pool.");
                frozen.Add(new VerificationCandidate(recordId, groupOrdinals[groupIndex], 1_000_000, false));
            }
        }

        frozen = frozen.OrderBy(candidate => candidate.RecordId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex).ToList();
        var profile = VerificationAlgorithmProfile.StudentGroupUniform;
        var auditPayload = CreateAuditPayload("group", count, frozen, new Dictionary<Student, History>(), new
        {
            fairDraw = false,
            algorithmProfile = profile.ToString(),
            repeatMode = ToAuditName(drawMode),
            halfRepeatLimit = drawMode == DrawMode.HalfRepeat ? halfRepeat : (int?)null,
            historyScope = ToHistoryScopeName(courseName, historyGroup),
            groupCount = groups.Count
        }, rosterDigest);

        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            SamplingMode = VerificationSamplingMode.GroupUniform,
            AlgorithmProfile = profile,
            Count = count,
            Candidates = frozen,
            AuditPayload = auditPayload,
            DrawModeToken = ToAuditName(drawMode),
            HistoryScope = ToHistoryScopeName(courseName, historyGroup)
        };
    }

    /// <summary>
    ///     Freezes lottery inventory after the same temporary-record and stock rules used by the lottery page.
    /// </summary>
    public VerificationDrawInput CreatePrizeVerificationInput(
        int count,
        IReadOnlyDictionary<string, int> temporaryCounts,
        string? historyGroup = null,
        string rosterDigest = "")
    {
        var historyCache = BuildPrizeTemporaryHistoryCache(PrizeList.Prizes, temporaryCounts);
        List<Prize> usable;
        try
        {
            usable = FilterPrizes(_ => true, count, historyCache);
        }
        catch (Exception exception) when (exception is CandidateNotFoundException or RepeatLimitExhaustedException)
        {
            throw new NoEligibleCandidatesException("The prepared prize pool has no drawable candidate.", exception);
        }

        var weighted = BuildPrizeCandidates(usable, historyCache);
        if (count <= 0 || count > weighted.Count)
            throw new InvalidOperationException("The prepared prize pool cannot satisfy this draw.");

        var frozen = FreezeCandidates(weighted);
        if (count > frozen.Count)
            throw new InvalidOperationException("The prepared prize pool cannot satisfy this draw.");
        EnsureDrawableWeight(frozen, count);
        var lotteryDrawType = ConfigData.LotterySettings.DrawType;
        var lotteryDrawMode = ConfigData.LotterySettings.DrawMode;
        if (lotteryDrawMode == DrawMode.Orchestrated)
            throw new InvalidOperationException(
                "Orchestrated draws must build their verification input from the arranged schedule.");
        var samplingMode = lotteryDrawType == LotteryDrawType.Count
            ? VerificationSamplingMode.InventoryPermutation
            : VerificationSamplingMode.WeightedWithoutReplacement;
        var auditPayload = CreateAuditPayload("prize", count, frozen, historyCache, new
        {
            samplingAlgorithm = samplingMode == VerificationSamplingMode.InventoryPermutation
                ? "inventory-partial-permutation"
                : "weighted-without-replacement",
            inventoryEntriesEqualWeight = samplingMode == VerificationSamplingMode.InventoryPermutation,
            // Reserved compatibility marker; count-mode draws no longer have an insider fallback.
            internalRulesRequireWeightedFallback = false,
            algorithmProfile = GetLotteryAlgorithmProfile(lotteryDrawType, lotteryDrawMode).ToString(),
            lotteryMode = lotteryDrawType == LotteryDrawType.Count ? "count" : "pan",
            repeatMode = lotteryDrawType == LotteryDrawType.Pan ? ToAuditName(lotteryDrawMode) : null,
            halfRepeatLimit = lotteryDrawType == LotteryDrawType.Pan && lotteryDrawMode == DrawMode.HalfRepeat
                ? GetLotteryRepeatThreshold()
                : (int?)null
        }, rosterDigest);
        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Prize,
            SamplingMode = samplingMode,
            AlgorithmProfile = GetLotteryAlgorithmProfile(lotteryDrawType, lotteryDrawMode),
            Count = count,
            Candidates = frozen,
            AuditPayload = auditPayload,
            // Count-mode inventory has no repeat semantics of its own, so it declares the sampling shape instead of
            // borrowing the configured (and unused) repeat mode.
            DrawModeToken = lotteryDrawType == LotteryDrawType.Count ? "inventory" : ToAuditName(lotteryDrawMode),
            HistoryScope = "global"
        };
    }

    /// <summary>
    ///     Freezes an inclusive integer range for the "no roster" number draw. Every integer in the range is one
    ///     equally weighted ticket, so sampling is a uniform partial permutation — the same shape the verifier
    ///     already uses for equal-probability inventory. Only the range leaves the device in the anonymous audit
    ///     payload; there is no roster behind these tickets, so no roster digest is emitted.
    /// </summary>
    public VerificationDrawInput CreateNumberVerificationInput(int count, int rangeStart, int rangeEnd)
    {
        if (!RosterSelection.IsValidRange(rangeStart, rangeEnd))
            throw new InvalidOperationException("The prepared number range cannot satisfy this draw.");
        var total = rangeEnd - rangeStart + 1;
        if (count <= 0 || count > total)
            throw new InvalidOperationException("The prepared number range cannot satisfy this draw.");

        List<VerificationCandidate> candidates = new(total);
        for (var number = rangeStart; number <= rangeEnd; number++)
            candidates.Add(new VerificationCandidate(
                NumberTickets.CreateRecordId(rangeStart, rangeEnd, number), 0, 1_000_000, false));
        candidates.Sort((left, right) =>
        {
            var byRecordId = string.CompareOrdinal(left.RecordId.ToString("N"), right.RecordId.ToString("N"));
            return byRecordId != 0 ? byRecordId : left.OccurrenceIndex.CompareTo(right.OccurrenceIndex);
        });

        var ordered = candidates.Select((candidate, index) => new
        {
            index,
            recordId = candidate.RecordId,
            candidate.OccurrenceIndex,
            candidate.WeightMicros,
            candidate.IsGuaranteed,
            internalSettingApplied = false,
            internalProbability = (double?)null,
            historyCount = 0,
            lastDrawnUtc = (DateTime?)null
        }).ToArray();

        var auditPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "secrandom-anonymous-audit/v2",
            operation = "number",
            requestedCount = count,
            candidateCount = ordered.Length,
            // Reserved compatibility markers; insider settings are no longer generated or applied.
            internalSettingsApplied = false,
            internalCandidateCount = 0,
            internalExcludedCandidateCount = 0,
            fairness = new
            {
                fairDraw = true,
                algorithmProfile = VerificationAlgorithmProfile.NumberUniformPermutation.ToString(),
                repeatMode = "no-repeat",
                samplingAlgorithm = "inventory-partial-permutation",
                inventoryEntriesEqualWeight = true,
                internalRulesRequireWeightedFallback = false
            },
            numberRange = new { start = rangeStart, end = rangeEnd },
            rosterDigest = (string?)null,
            candidates = ordered
        });

        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Number,
            SamplingMode = VerificationSamplingMode.InventoryPermutation,
            AlgorithmProfile = VerificationAlgorithmProfile.NumberUniformPermutation,
            Count = count,
            Candidates = candidates,
            AuditPayload = auditPayload,
            // The audit declares "no-repeat"; the range holds every ticket exactly once, so the readable context
            // repeats that same token rather than inventing a number-specific mode name.
            DrawModeToken = "no-repeat",
            HistoryScope = "global"
        };
    }

    private static VerificationAlgorithmProfile GetStudentAlgorithmProfile(
        DrawType drawType,
        DrawMode drawMode)
    {
        return (drawType, drawMode) switch
        {
            (DrawType.Fair, DrawMode.Repeat) => VerificationAlgorithmProfile.StudentFairRepeat,
            (DrawType.Fair, DrawMode.NoRepeat) => VerificationAlgorithmProfile.StudentFairNoRepeat,
            (DrawType.Fair, DrawMode.HalfRepeat) => VerificationAlgorithmProfile.StudentFairHalfRepeat,
            (_, DrawMode.Repeat) => VerificationAlgorithmProfile.StudentRandomRepeat,
            (_, DrawMode.NoRepeat) => VerificationAlgorithmProfile.StudentRandomNoRepeat,
            _ => VerificationAlgorithmProfile.StudentRandomHalfRepeat
        };
    }

    private static VerificationAlgorithmProfile GetLotteryAlgorithmProfile(
        LotteryDrawType drawType,
        DrawMode drawMode)
    {
        if (drawType == LotteryDrawType.Count)
            return VerificationAlgorithmProfile.LotteryInventoryCount;

        return drawMode switch
        {
            DrawMode.Repeat => VerificationAlgorithmProfile.LotteryPanRepeat,
            DrawMode.NoRepeat => VerificationAlgorithmProfile.LotteryPanNoRepeat,
            _ => VerificationAlgorithmProfile.LotteryPanHalfRepeat
        };
    }

    private DrawMode GetStudentDrawMode(DrawSettingsType drawSettingsType) => drawSettingsType switch
    {
        DrawSettingsType.RollCall => ConfigData.RollCallSettings.DrawMode,
        DrawSettingsType.QuickDraw => ConfigData.QuickDrawSettings.DrawMode,
        _ => ConfigData.RollCallSettings.DrawMode
    };

    private static string ToAuditName(DrawMode drawMode) => DrawModeCatalog.ToToken(drawMode);

    /// <summary>
    ///     Names the history scope a draw's per-candidate history counts came from, for the anonymous audit payload.
    /// </summary>
    /// <remarks>
    ///     Course names are recording metadata and never narrow the history counts. A <see langword="null" /> group
    ///     means no record-group filter was applied; the empty group string is the default record group and therefore
    ///     still narrows the history. Only the scope name leaves the device — never the group name — because it is
    ///     user-typed text that can carry a class or a person's name.
    /// </remarks>
    private static string ToHistoryScopeName(string _, string? historyGroup) =>
        historyGroup is null ? "global" : "history-group";

    /// <summary>
    ///     Rejects a frozen pool that cannot actually produce <paramref name="count"/> winners. Shielded
    ///     students stay in the pool with weight 0 so the anonymous audit payload still lists them, so the
    ///     pool size alone never guarantees a draw; without this check the kernel would be handed an
    ///     all-zero weight set and fail the whole draw.
    /// </summary>
    private static void EnsureDrawableWeight(IReadOnlyList<VerificationCandidate> frozen, int count)
    {
        var drawable = frozen.Count(candidate => candidate.IsGuaranteed || candidate.WeightMicros > 0);
        if (drawable < count)
            throw new NoEligibleCandidatesException("The frozen candidate pool has no drawable weight.");
    }

    private static IReadOnlyList<VerificationCandidate> FreezeCandidates<TCandidate>(
        IReadOnlyList<WeightedCandidate<TCandidate>> weightedCandidates)
    {
        Dictionary<Guid, uint> occurrences = [];
        List<VerificationCandidate> result = [];
        foreach (var weighted in weightedCandidates)
        {
            var recordId = GetRecordId(weighted.Candidate);
            var occurrence = occurrences.GetValueOrDefault(recordId);
            occurrences[recordId] = checked(occurrence + 1);

            result.Add(new VerificationCandidate(
                recordId,
                occurrence,
                ToWeightMicros(weighted.Weight),
                false));
        }

        return result;
    }

    private static Guid GetRecordId<TCandidate>(TCandidate candidate)
    {
        return candidate switch
        {
            Student student => EnsureRecordId(student),
            Prize prize => EnsureRecordId(prize),
            _ => throw new ArgumentException("Verification only supports student and prize records.", nameof(candidate))
        };
    }

    private static Guid EnsureRecordId(Student student)
    {
        ProfileRecordIdentity.EnsureRecordId(student);
        return student.RecordId;
    }

    private static Guid EnsureRecordId(Prize prize)
    {
        ProfileRecordIdentity.EnsureRecordId(prize);
        return prize.RecordId;
    }

    // Midnight UTC of the local day the draw happened on. Written as a real instant rather than a date string so
    // older readers that expect a timestamp keep parsing the audit payload.
    private static DateTime CoarsenLastDrawn(DateTime value) =>
        DateTime.SpecifyKind(value.ToUniversalTime().Date, DateTimeKind.Utc);

    private static long ToWeightMicros(double weight)
    {
        if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0)
            throw new ArgumentException("Verification weights must be finite and non-negative.", nameof(weight));

        var scaled = Math.Round(weight * 1_000_000d, MidpointRounding.ToEven);
        if (scaled > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(weight), "Verification weight exceeds the fixed-point range.");

        return (long)scaled;
    }

    private static byte[] CreateAuditPayload<TCandidate>(
        string operation,
        int count,
        IReadOnlyList<VerificationCandidate> candidates,
        IReadOnlyDictionary<TCandidate, History> historyCache,
        object? fairness,
        string rosterDigest)
    {
        var historyByRecordId = historyCache.ToDictionary(pair => GetRecordId(pair.Key), pair => pair.Value);
        var ordered = candidates
            .OrderBy(candidate => candidate.RecordId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex)
            .Select((candidate, index) =>
            {
                var history = historyByRecordId.GetValueOrDefault(candidate.RecordId);
                return new
                {
                    index,
                    recordId = candidate.RecordId,
                    candidate.OccurrenceIndex,
                    candidate.WeightMicros,
                    candidate.IsGuaranteed,
                    // Keep these nullable fields in the v2 audit shape for verifier compatibility. The insider
                    // feature is removed, so every new proof writes them as disabled/null.
                    internalSettingApplied = false,
                    internalProbability = (double?)null,
                    historyCount = history?.TotalCount ?? 0,
                    // Day precision only: a candidate's exact last-drawn instant is a behavioural fingerprint that
                    // links this record to unrelated proofs (and, with the timestamp field, to a real lesson time).
                    // The audit needs "how stale is this count", not the minute it happened, so the clock time is
                    // dropped here and the day survives.
                    lastDrawnUtc = history is null || history.LastDrawnTime == DateTime.MinValue
                        ? (DateTime?)null
                        : CoarsenLastDrawn(history.LastDrawnTime)
                };
            })
            .ToArray();

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "secrandom-anonymous-audit/v2",
            operation,
            requestedCount = count,
            candidateCount = ordered.Length,
            internalSettingsApplied = false,
            internalCandidateCount = 0,
            internalExcludedCandidateCount = 0,
            fairness,
            // The roster's RecordId-to-display mapping. Anonymous: a digest only, never names or numbers.
            rosterDigest = string.IsNullOrWhiteSpace(rosterDigest) ? null : rosterDigest,
            candidates = ordered
        });
    }
}
