using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;
using System.Text.Json;
using VerificationDrawKind = SecRandom.Shared.Models.Verification.VerificationDrawKind;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    /// <summary>
    ///     Freezes the same prepared student pool used by the draw page into a deterministic verification request.
    /// </summary>
    public VerificationDrawInput CreateStudentVerificationInput(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        string courseName = "")
    {
        var preparedCandidates = candidates.Where(student => student.IsCandidate).ToList();
        if (preparedCandidates.Count == 0 || count <= 0 || count > preparedCandidates.Count)
            throw new InvalidOperationException("The prepared student pool cannot satisfy this draw.");

        var historyCache = BuildStudentHistoryCache(preparedCandidates, courseName);
        var drawType = GetStudentDrawType(drawSettingsType);
        var drawMode = GetStudentDrawMode(drawSettingsType);
        List<Student> usable;
        try
        {
            usable = FilterPreparedStudents(preparedCandidates, count, historyCache, drawType == DrawType.Fair);
        }
        catch (Exception exception) when (exception is CandidateNotFoundException or RepeatLimitExhaustedException)
        {
            throw new InvalidOperationException("The prepared student pool cannot satisfy this draw.", exception);
        }

        var weighted = drawType == DrawType.Fair
            ? CalculateStudentWeight(usable, historyCache, courseName)
            : usable.Select(student => new WeightedCandidate<Student> { Candidate = student, Weight = 1.0 }).ToList();

        var frozen = FreezeCandidates(weighted);
        if (count > frozen.Count)
            throw new InvalidOperationException("The prepared student pool cannot satisfy this draw.");
        var hasInternalRules = weighted.Any(candidate => GetBehindSceneSettings(candidate.Candidate) is { IsAttachSettingsEnabled: true });
        var algorithmProfile = GetStudentAlgorithmProfile(drawType, drawMode, hasInternalRules);
        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            SamplingMode = drawType == DrawType.Fair
                ? VerificationSamplingMode.HistoryBalancedWeighted
                : VerificationSamplingMode.WeightedWithoutReplacement,
            AlgorithmProfile = algorithmProfile,
            Count = count,
            Candidates = frozen,
            AuditPayload = CreateAuditPayload("student", count, frozen, weighted, historyCache, new
            {
                fairDraw = drawType == DrawType.Fair,
                algorithmProfile = algorithmProfile.ToString(),
                repeatMode = ToAuditName(drawMode),
                halfRepeatLimit = drawMode == DrawMode.HalfRepeat ? GetStudentRepeatThreshold(drawSettingsType) : (int?)null,
                averageGapProtectionApplied = drawType == DrawType.Fair && ConfigData.FairDrawSettings.EnableAvgGapProtection,
                candidateCountBeforeAverageGapProtection = preparedCandidates.Count,
                candidateCountAfterAverageGapProtection = usable.Count
            })
        };
    }

    /// <summary>
    ///     Freezes lottery inventory after the same temporary-record and stock rules used by the lottery page.
    /// </summary>
    public VerificationDrawInput CreatePrizeVerificationInput(
        int count,
        IReadOnlyDictionary<string, int> temporaryCounts)
    {
        var historyCache = BuildPrizeTemporaryHistoryCache(PrizeList.Prizes, temporaryCounts);
        var usable = FilterPrizes(_ => true, count, historyCache);
        var weighted = BuildPrizeCandidates(usable, historyCache);
        if (count <= 0 || count > weighted.Count)
            throw new InvalidOperationException("The prepared prize pool cannot satisfy this draw.");

        var frozen = FreezeCandidates(weighted);
        if (count > frozen.Count)
            throw new InvalidOperationException("The prepared prize pool cannot satisfy this draw.");
        var hasInternalRules = weighted.Any(candidate => GetBehindSceneSettings(candidate.Candidate) is { IsAttachSettingsEnabled: true });
        var lotteryDrawType = ConfigData.LotterySettings.DrawType;
        var lotteryDrawMode = ConfigData.LotterySettings.DrawMode;
        var samplingMode = lotteryDrawType == LotteryDrawType.Count && !hasInternalRules
            ? VerificationSamplingMode.InventoryPermutation
            : VerificationSamplingMode.WeightedWithoutReplacement;
        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Prize,
            SamplingMode = samplingMode,
            AlgorithmProfile = GetLotteryAlgorithmProfile(lotteryDrawType, lotteryDrawMode, hasInternalRules),
            Count = count,
            Candidates = frozen,
            AuditPayload = CreateAuditPayload("prize", count, frozen, weighted, historyCache, new
            {
                samplingAlgorithm = samplingMode == VerificationSamplingMode.InventoryPermutation
                    ? "inventory-partial-permutation"
                    : "weighted-without-replacement",
                inventoryEntriesEqualWeight = samplingMode == VerificationSamplingMode.InventoryPermutation,
                internalRulesRequireWeightedFallback = samplingMode == VerificationSamplingMode.WeightedWithoutReplacement
                    && lotteryDrawType == LotteryDrawType.Count
                    && hasInternalRules,
                algorithmProfile = GetLotteryAlgorithmProfile(lotteryDrawType, lotteryDrawMode, hasInternalRules).ToString(),
                lotteryMode = lotteryDrawType == LotteryDrawType.Count ? "count" : "pan",
                repeatMode = lotteryDrawType == LotteryDrawType.Pan ? ToAuditName(lotteryDrawMode) : null,
                halfRepeatLimit = lotteryDrawType == LotteryDrawType.Pan && lotteryDrawMode == DrawMode.HalfRepeat
                    ? GetLotteryRepeatThreshold()
                    : (int?)null
            })
        };
    }

    private static VerificationAlgorithmProfile GetStudentAlgorithmProfile(
        DrawType drawType,
        DrawMode drawMode,
        bool hasInternalRules)
    {
        if (hasInternalRules)
        {
            return (drawType, drawMode) switch
            {
                (DrawType.Fair, DrawMode.Repeat) => VerificationAlgorithmProfile.StudentFairInternalRuleRepeat,
                (DrawType.Fair, DrawMode.NoRepeat) => VerificationAlgorithmProfile.StudentFairInternalRuleNoRepeat,
                (DrawType.Fair, _) => VerificationAlgorithmProfile.StudentFairInternalRuleHalfRepeat,
                (_, DrawMode.Repeat) => VerificationAlgorithmProfile.StudentRandomInternalRuleRepeat,
                (_, DrawMode.NoRepeat) => VerificationAlgorithmProfile.StudentRandomInternalRuleNoRepeat,
                _ => VerificationAlgorithmProfile.StudentRandomInternalRuleHalfRepeat
            };
        }

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
        DrawMode drawMode,
        bool hasInternalRules)
    {
        if (drawType == LotteryDrawType.Count)
            return hasInternalRules
                ? VerificationAlgorithmProfile.LotteryCountInternalRule
                : VerificationAlgorithmProfile.LotteryInventoryCount;

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

    private static string ToAuditName(DrawMode drawMode) => drawMode switch
    {
        DrawMode.Repeat => "repeat",
        DrawMode.NoRepeat => "no-repeat",
        DrawMode.HalfRepeat => "half-repeat",
        _ => "unknown"
    };

    private static IReadOnlyList<VerificationCandidate> FreezeCandidates<TCandidate>(
        IReadOnlyList<WeightedCandidate<TCandidate>> weightedCandidates)
        where TCandidate : IAttachableSettingsObject
    {
        Dictionary<Guid, uint> occurrences = [];
        HashSet<Guid> guaranteedRecordIds = [];
        List<VerificationCandidate> result = [];
        foreach (var weighted in weightedCandidates)
        {
            var recordId = GetRecordId(weighted.Candidate);
            var occurrence = occurrences.GetValueOrDefault(recordId);
            occurrences[recordId] = checked(occurrence + 1);

            var settings = GetBehindSceneSettings(weighted.Candidate);
            var probability = settings is { IsAttachSettingsEnabled: true }
                ? Math.Clamp(settings.Probability, 0d, 100d)
                : 100d;
            if (probability <= 0)
                continue;

            var guaranteed = settings is { IsAttachSettingsEnabled: true } && probability >= 100d;
            if (guaranteed && !guaranteedRecordIds.Add(recordId))
                continue;

            var effectiveWeight = guaranteed ? 1.0 : weighted.Weight * (probability / 100.0);
            result.Add(new VerificationCandidate(
                recordId,
                occurrence,
                ToWeightMicros(effectiveWeight),
                guaranteed));
        }

        return result;
    }

    private static Guid GetRecordId(IAttachableSettingsObject candidate)
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
        IReadOnlyList<WeightedCandidate<TCandidate>> weightedCandidates,
        IReadOnlyDictionary<TCandidate, History> historyCache,
        object? fairness = null)
        where TCandidate : IAttachableSettingsObject
    {
        var historyByRecordId = historyCache.ToDictionary(pair => GetRecordId(pair.Key), pair => pair.Value);
        Dictionary<Guid, double?> internalSettingsByRecordId = [];
        var internalExcludedCandidateCount = 0;
        foreach (var weighted in weightedCandidates)
        {
            var recordId = GetRecordId(weighted.Candidate);
            var settings = GetBehindSceneSettings(weighted.Candidate);
            if (settings is not { IsAttachSettingsEnabled: true })
            {
                internalSettingsByRecordId[recordId] = null;
                continue;
            }

            var probability = Math.Clamp(settings.Probability, 0d, 100d);
            internalSettingsByRecordId[recordId] = probability;
            if (probability <= 0)
                internalExcludedCandidateCount++;
        }
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
                    internalSettingApplied = internalSettingsByRecordId.GetValueOrDefault(candidate.RecordId) is not null,
                    internalProbability = internalSettingsByRecordId.GetValueOrDefault(candidate.RecordId),
                    historyCount = history?.TotalCount ?? 0,
                    lastDrawnUtc = history is null || history.LastDrawnTime == DateTime.MinValue
                        ? (DateTime?)null
                        : history.LastDrawnTime.ToUniversalTime()
                };
            })
            .ToArray();

        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "secrandom-anonymous-audit/v2",
            operation,
            requestedCount = count,
            candidateCount = ordered.Length,
            internalSettingsApplied = ordered.Any(candidate => candidate.internalSettingApplied)
                                      || internalExcludedCandidateCount > 0,
            internalCandidateCount = ordered.Count(candidate => candidate.internalSettingApplied),
            internalExcludedCandidateCount,
            fairness,
            candidates = ordered
        });
    }
}
