using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.Verification;
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
        var usable = candidates.Where(student => student.IsCandidate).ToList();
        if (usable.Count == 0 || count <= 0 || count > usable.Count)
            throw new InvalidOperationException("The prepared student pool cannot satisfy this draw.");

        var historyCache = BuildStudentHistoryCache(usable, courseName);
        var weighted = GetStudentDrawType(drawSettingsType) == DrawType.Fair
            ? CalculateStudentWeight(usable, historyCache)
            : usable.Select(student => new WeightedCandidate<Student> { Candidate = student, Weight = 1.0 }).ToList();

        var frozen = FreezeCandidates(weighted);
        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            Count = count,
            Candidates = frozen,
            AuditPayload = CreateAuditPayload("student", count, frozen, weighted, historyCache)
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
        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Prize,
            Count = count,
            Candidates = frozen,
            AuditPayload = CreateAuditPayload("prize", count, frozen, weighted, historyCache)
        };
    }

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
        IReadOnlyDictionary<TCandidate, History> historyCache)
        where TCandidate : IAttachableSettingsObject
    {
        var historyByRecordId = historyCache.ToDictionary(pair => GetRecordId(pair.Key), pair => pair.Value);
        Dictionary<Guid, double?> internalSettingsByRecordId = [];
        foreach (var weighted in weightedCandidates)
        {
            var recordId = GetRecordId(weighted.Candidate);
            var settings = GetBehindSceneSettings(weighted.Candidate);
            internalSettingsByRecordId[recordId] = settings is { IsAttachSettingsEnabled: true }
                ? Math.Clamp(settings.Probability, 0d, 100d)
                : null;
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
            format = "secrandom-anonymous-audit/v1",
            operation,
            requestedCount = count,
            candidateCount = ordered.Length,
            internalSettingsApplied = ordered.Any(candidate => candidate.internalSettingApplied),
            internalCandidateCount = ordered.Count(candidate => candidate.internalSettingApplied),
            candidates = ordered
        });
    }
}
