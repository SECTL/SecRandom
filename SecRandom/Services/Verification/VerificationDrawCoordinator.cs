using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Core.Services.Orchestration;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Profile;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class VerificationDrawCoordinator(
    DrawEngine drawEngine,
    IVerificationKernel kernel,
    DrawProofExportService proofExporter,
    DrawProofAttestationService attestationService,
    IProfileService profileService,
    MainConfigHandler configHandler,
    IWitnessClient witnessClient,
    ILogger<VerificationDrawCoordinator> logger)
{
    public bool IsEnabled => true;

    /// <summary>
    ///     Returns <see langword="null"/> when the frozen pool has no drawable weight at all (for example
    ///     when every remaining member is still inside the post-draw shield window). That is a normal
    ///     outcome, not an error: callers surface it as <c>DrawStatus.NoEligibleCandidates</c>.
    /// </summary>
    public async Task<VerificationDrawOutcome<Student>?> DrawStudentsAsync(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        DrawProofExportContext exportContext,
        Guid? parentProofId = null,
        string courseName = "",
        string? historyGroup = null,
        CancellationToken cancellationToken = default,
        DrawMode? drawModeOverride = null,
        string? channel = null)
    {
        var verificationMode = configHandler.Data.General.Verification.Mode;
        if (verificationMode == VerificationMode.FormalNotarized
            && drawEngine.IsStudentOrchestratedMode(drawSettingsType, drawModeOverride))
            throw FormalNotarizationException.UnsupportedForOrchestration();
        var roster = CommitStudentRoster(exportContext.ListName);
        VerificationDrawInput input;
        try
        {
            input = drawEngine.CreateStudentVerificationInput(
                count,
                candidates,
                drawSettingsType,
                courseName,
                historyGroup,
                roster.Digest,
                drawModeOverride);
        }
        catch (NoEligibleCandidatesException exception)
        {
            logger.LogWarning(exception,
                "点名抽取没有可抽取的成员：请求数量={Count}，名单={ListName}。",
                count, exportContext.ListName);
            return null;
        }

        return await DrawAsync(input, candidates, exportContext, parentProofId, verificationMode,
                new DrawProofSealContext(
                    channel ?? ResolveStudentChannel(drawSettingsType),
                    ResolveDrawSettingsTypeToken(drawSettingsType),
                    roster.EntryCount,
                    roster.Digest,
                    roster.Salt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Draws from a frozen group pool. The verification request contains every member once with an anonymous
    ///     group bucket; the kernel samples buckets uniformly and then one member uniformly inside the winning bucket.
    /// </summary>
    public async Task<VerificationDrawOutcome<Student>?> DrawGroupsAsync(
        int count,
        IReadOnlyList<GroupDrawCandidates.Group> groups,
        DrawSettingsType drawSettingsType,
        DrawProofExportContext exportContext,
        Guid? parentProofId = null,
        string courseName = "",
        string? historyGroup = null,
        DrawMode drawMode = DrawMode.Repeat,
        int halfRepeat = 1,
        CancellationToken cancellationToken = default,
        string? channel = null)
    {
        var verificationMode = configHandler.Data.General.Verification.Mode;
        var roster = CommitStudentRoster(exportContext.ListName);
        VerificationDrawInput input;
        try
        {
            input = drawEngine.CreateGroupVerificationInput(count, groups, drawMode, halfRepeat, courseName,
                historyGroup, roster.Digest);
        }
        catch (NoEligibleCandidatesException exception)
        {
            logger.LogWarning(exception, "小组抽取没有可抽取的候选组：请求数量={Count}，名单={ListName}。", count, exportContext.ListName);
            return null;
        }

        var records = groups.SelectMany(group => group.Members).ToArray();
        return await DrawAsync(input, records, exportContext, parentProofId, verificationMode,
                new DrawProofSealContext(channel ?? ResolveStudentChannel(drawSettingsType),
                    ResolveDrawSettingsTypeToken(drawSettingsType), roster.EntryCount, roster.Digest, roster.Salt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns <see langword="null"/> when the frozen prize pool has no drawable weight at all.
    /// </summary>
    public async Task<VerificationDrawOutcome<Prize>?> DrawPrizesAsync(
        int count,
        IReadOnlyDictionary<string, int> temporaryCounts,
        IReadOnlyCollection<Prize> prizes,
        DrawProofExportContext exportContext,
        string? historyGroup = null,
        CancellationToken cancellationToken = default)
    {
        var verificationMode = configHandler.Data.General.Verification.Mode;
        if (verificationMode == VerificationMode.FormalNotarized && drawEngine.IsLotteryOrchestratedMode())
            throw FormalNotarizationException.UnsupportedForOrchestration();
        var roster = CommitPrizeRoster(exportContext.ListName);
        VerificationDrawInput input;
        try
        {
            input = drawEngine.CreatePrizeVerificationInput(count, temporaryCounts, historyGroup, roster.Digest);
        }
        catch (NoEligibleCandidatesException exception)
        {
            logger.LogWarning(exception,
                "抽奖没有可抽取的奖品：请求数量={Count}，奖池={ListName}。",
                count, exportContext.ListName);
            return null;
        }

        return await DrawAsync(input, prizes, exportContext, null, verificationMode,
                new DrawProofSealContext("lottery", "lottery", roster.EntryCount, roster.Digest, roster.Salt), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies an arranged round: the drawn order is the schedule itself, so the proof commits positions
    ///     (student orchestrated + sequential order) instead of sampling a weighted pool.
    /// </summary>
    public async Task<VerificationDrawOutcome<Student>?> DrawOrchestratedStudentsAsync(
        IReadOnlyList<Student> ordered,
        int round,
        int roundCount,
        DrawProofExportContext exportContext,
        string courseName = "",
        string? historyGroup = null,
        CancellationToken cancellationToken = default,
        string channel = "roll-call")
    {
        ArgumentNullException.ThrowIfNull(ordered);
        if (configHandler.Data.General.Verification.Mode == VerificationMode.FormalNotarized)
            throw FormalNotarizationException.UnsupportedForOrchestration();
        if (ordered.Count == 0)
            return null;

        var verificationMode = configHandler.Data.General.Verification.Mode;
        var roster = CommitStudentRoster(exportContext.ListName);
        var history = new Dictionary<Guid, (int Count, DateTime? LastDrawn)>(ordered.Count);
        foreach (var entry in drawEngine.GetStudentHistoryFigures(ordered, courseName, historyGroup))
            history[entry.Key] = (entry.Value.Count, entry.Value.LastDrawn);
        var input = OrchestrationVerification.CreateStudentInput(
            ordered, round, roundCount, history, roster.Digest, courseName, historyGroup);
        return await DrawAsync(input, ordered, exportContext, null, verificationMode,
                new DrawProofSealContext(channel, ResolveOrchestratedSettingsType(channel), roster.EntryCount, roster.Digest, roster.Salt),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Prize-side counterpart of <see cref="DrawOrchestratedStudentsAsync" />.
    /// </summary>
    public async Task<VerificationDrawOutcome<Prize>?> DrawOrchestratedPrizesAsync(
        IReadOnlyList<Prize> ordered,
        int round,
        int roundCount,
        DrawProofExportContext exportContext,
        CancellationToken cancellationToken = default,
        bool countMode = false)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        if (configHandler.Data.General.Verification.Mode == VerificationMode.FormalNotarized)
            throw FormalNotarizationException.UnsupportedForOrchestration();
        if (ordered.Count == 0)
            return null;

        var verificationMode = configHandler.Data.General.Verification.Mode;
        var roster = CommitPrizeRoster(exportContext.ListName);
        var input = OrchestrationVerification.CreatePrizeInput(
            ordered,
            round,
            roundCount,
            rosterDigest: roster.Digest,
            countMode: countMode);
        return await DrawAsync(input, ordered, exportContext, null, verificationMode,
                new DrawProofSealContext("lottery", "lottery", roster.EntryCount, roster.Digest, roster.Salt), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies a "no roster" number draw. The tickets are synthetic records generated from the committed
    ///     inclusive range, so there is no roster behind them and no roster digest is emitted.
    /// </summary>
    public async Task<VerificationDrawOutcome<Student>?> DrawNumberStudentsAsync(
        int count,
        int rangeStart,
        int rangeEnd,
        IReadOnlyCollection<Student> tickets,
        DrawProofExportContext exportContext,
        CancellationToken cancellationToken = default)
    {
        var verificationMode = configHandler.Data.General.Verification.Mode;
        var input = drawEngine.CreateNumberVerificationInput(count, rangeStart, rangeEnd);
        return await DrawAsync(input, tickets, exportContext, null, verificationMode,
                new DrawProofSealContext("number", "number", 0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Prize-side counterpart of <see cref="DrawNumberStudentsAsync" /> for the lottery page's number mode.
    /// </summary>
    public async Task<VerificationDrawOutcome<Prize>?> DrawNumberPrizesAsync(
        int count,
        int rangeStart,
        int rangeEnd,
        IReadOnlyCollection<Prize> tickets,
        DrawProofExportContext exportContext,
        CancellationToken cancellationToken = default)
    {
        var verificationMode = configHandler.Data.General.Verification.Mode;
        var input = drawEngine.CreateNumberVerificationInput(count, rangeStart, rangeEnd);
        return await DrawAsync(input, tickets, exportContext, null, verificationMode,
                new DrawProofSealContext("number", "number", 0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Commits the roster the draw was taken from so that renaming someone afterwards cannot silently
    ///     re-point a winning record id at a different person. The digest is omitted when the active profile
    ///     is not the list the caller drew from, so a stale profile can never produce a wrong commitment —
    ///     and an omission is always logged, because a proof without a digest cannot be told apart from one
    ///     that was never meant to carry it.
    /// </summary>
    /// <summary>
    ///     Projects the frozen input into the readable, de-identified context a v2 proof commits. Everything here
    ///     is an enumeration member, a count, a digest, or a coarse environment fact: no candidate display data,
    ///     no list/course/group name, no device or user identity ever enters this block.
    /// </summary>
    private static DrawProofContext CreateProofContext(
        VerificationDrawInput input,
        DrawProofExportContext exportContext,
        DrawProofSealContext seal)
    {
        var candidates = input.Candidates;
        return new DrawProofContext
        {
            Channel = seal.Channel,
            SettingsType = seal.SettingsType,
            DrawMode = input.DrawModeToken,
            SamplingMode = ResolveSamplingModeToken(input.SamplingMode),
            AlgorithmProfile = input.AlgorithmProfile.ToString(),
            RequestedCount = input.Count,
            Pool = new DrawProofPool
            {
                CandidateCount = candidates.Count,
                DrawableCount = candidates.Count(candidate => candidate.IsGuaranteed || candidate.WeightMicros > 0),
                ZeroWeightCount = candidates.Count(candidate => !candidate.IsGuaranteed && candidate.WeightMicros == 0),
                GuaranteedCount = candidates.Count(candidate => candidate.IsGuaranteed),
                ExcludedCount = input.ExcludedCandidateCount
            },
            Scope = new DrawProofScope
            {
                History = input.HistoryScope,
                FilterKinds = DeriveFilterKinds(exportContext.FilterLabels)
            },
            Orchestration = input.OrchestrationRound is { } round
                ? new DrawProofOrchestration
                {
                    Round = round,
                    RoundCount = input.OrchestrationRoundCount ?? round
                }
                : null,
            Roster = new DrawProofRoster
            {
                Kind = input.Kind switch
                {
                    VerificationDrawKind.Student => "student",
                    VerificationDrawKind.Prize => "prize",
                    _ => "number"
                },
                EntryCount = seal.RosterEntryCount,
                DigestFormat = string.IsNullOrWhiteSpace(seal.RosterDigest)
                    ? string.Empty
                    : string.IsNullOrWhiteSpace(seal.RosterDigestSalt) ? RosterDigest.FormatV3 : RosterDigest.FormatV4,
                Digest = seal.RosterDigest,
                // Published so the digest stays recomputable by someone who holds the roster; absent on v3 proofs,
                // which hashed the same material without it.
                DigestSalt = string.IsNullOrWhiteSpace(seal.RosterDigestSalt) ? null : seal.RosterDigestSalt
            },
            Environment = new DrawProofEnvironment
            {
                AppVersion = GlobalConstants.Version,
                PlatformFamily = ResolvePlatformFamily(),
                TimeZoneOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes
            },
            SessionId = Guid.NewGuid()
        };
    }

    private static string ResolveStudentChannel(DrawSettingsType drawSettingsType) => drawSettingsType switch
    {
        DrawSettingsType.QuickDraw => "quick-draw",
        DrawSettingsType.Lottery => "lottery",
        _ => "roll-call"
    };

    private static string ResolveDrawSettingsTypeToken(DrawSettingsType drawSettingsType) => drawSettingsType switch
    {
        DrawSettingsType.QuickDraw => "quick-draw",
        DrawSettingsType.Lottery => "lottery",
        _ => "roll-call"
    };

    /// <summary>
    ///     An arranged draw has no <see cref="DrawSettingsType" /> at the call site — the round belongs to a
    ///     channel, and the channel is what names the settings it was arranged by.
    /// </summary>
    private static string ResolveOrchestratedSettingsType(string channel) => channel switch
    {
        "quick-draw" => "quick-draw",
        "lottery" => "lottery",
        _ => "roll-call"
    };

    private static string ResolveSamplingModeToken(VerificationSamplingMode samplingMode) => samplingMode switch
    {
        VerificationSamplingMode.HistoryBalancedWeighted => "history-balanced-weighted",
        VerificationSamplingMode.InventoryPermutation => "inventory-permutation",
        VerificationSamplingMode.WeightedWithoutReplacement => "weighted-without-replacement",
        VerificationSamplingMode.SequentialOrder => "sequential-order",
        VerificationSamplingMode.GroupUniform => "group-uniform",
        _ => "unknown"
    };

    /// <summary>
    ///     Maps the display filter labels a caller already builds for the file name onto their kinds. The values are
    ///     discarded here on purpose: a group, a gender, and a course name are user-typed text, and only the fact
    ///     that such a filter ran is safe to publish.
    /// </summary>
    private static IReadOnlyList<string> DeriveFilterKinds(IReadOnlyList<string> filterLabels)
    {
        List<string> kinds = [];
        foreach (var label in filterLabels)
        {
            var kind = label switch
            {
                _ when label.StartsWith("组别=", StringComparison.Ordinal) => "group",
                _ when label.StartsWith("性别=", StringComparison.Ordinal) => "gender",
                _ when label.StartsWith("课程=", StringComparison.Ordinal) => "course",
                _ when label.StartsWith("状态=", StringComparison.Ordinal) => "status",
                _ => null
            };
            if (kind is not null && !kinds.Contains(kind))
                kinds.Add(kind);
        }

        kinds.Sort(StringComparer.Ordinal);
        return kinds;
    }

    private static string ResolvePlatformFamily() => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsAndroid()
            ? "android"
            : OperatingSystem.IsIOS()
                ? "ios"
                : OperatingSystem.IsMacOS()
                    ? "macos"
                    : "linux";

    /// <summary>
    ///     A roster commitment carries the digest together with the per-proof salt that produced it. Both are
    ///     published; the salt exists so two proofs taken from the same list do not share a digest and therefore
    ///     cannot be joined into a larger picture of one class.
    /// </summary>
    private sealed record RosterCommitment(string Digest, string Salt, int EntryCount)
    {
        /// <summary>No commitment: there is no active list, or it is not the list this draw came from.</summary>
        public static readonly RosterCommitment None = new(string.Empty, string.Empty, 0);
    }

    private RosterCommitment CommitStudentRoster(string listName)
    {
        var config = profileService.StudentListConfig;
        if (config is null || string.IsNullOrWhiteSpace(listName)
            || !string.Equals(config.Name, listName, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "抽取证明未绑定名单摘要：活动名单={ActiveListName}，本次抽取名单={DrawnListName}。该证明无法把中奖记录编号还原到具体成员。",
                config?.Name, listName);
            return RosterCommitment.None;
        }

        return Commit(config.Data.Students.Count, salt => RosterDigest.Compute(config.Data, salt));
    }

    private RosterCommitment CommitPrizeRoster(string listName)
    {
        var config = profileService.PrizeListConfig;
        if (config is null || string.IsNullOrWhiteSpace(listName)
            || !string.Equals(config.Name, listName, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "抽取证明未绑定奖池摘要：活动奖池={ActiveListName}，本次抽取奖池={DrawnListName}。该证明无法把中奖记录编号还原到具体奖品。",
                config?.Name, listName);
            return RosterCommitment.None;
        }

        return Commit(config.Data.Prizes.Count, salt => RosterDigest.Compute(config.Data, salt));
    }

    private static RosterCommitment Commit(int entryCount, Func<byte[], string> digest)
    {
        var salt = RandomNumberGenerator.GetBytes(RosterDigest.SaltLength);
        return new RosterCommitment(digest(salt), WitnessClient.ToBase64Url(salt), entryCount);
    }

    private async Task<VerificationDrawOutcome<TCandidate>> DrawAsync<TCandidate>(
        VerificationDrawInput input,
        IReadOnlyCollection<TCandidate> records,
        DrawProofExportContext exportContext,
        Guid? parentProofId,
        VerificationMode verificationMode,
        DrawProofSealContext seal,
        CancellationToken cancellationToken)
        where TCandidate : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFormalNotarizationSupported(input, verificationMode);
        var inputHash = VerificationWireCodec.ComputeInputHash(input);
        var candidatePoolCommitment = VerificationWireCodec.ComputeCandidatePoolCommitment(input);
        // Built once for both evidence paths: the offline proof embeds it, and the formal request forwards it so the
        // service can embed it into the document it signs. A single instance keeps the session id consistent.
        if (verificationMode == VerificationMode.FormalNotarized
            && input.Kind is VerificationDrawKind.Student or VerificationDrawKind.Prize
            && string.IsNullOrWhiteSpace(seal.RosterDigest))
            throw FormalNotarizationException.RosterDigestMissing();

        var context = CreateProofContext(input, exportContext, seal);
        if (verificationMode == VerificationMode.FormalNotarized)
            context = context with { CandidatePoolCommitment = candidatePoolCommitment };
        DrawProof proof;
        VerificationKernelResult result;
        if (verificationMode == VerificationMode.FormalNotarized)
        {
            var request = new FormalNotarizationRequest
            {
                ProofId = Guid.NewGuid(),
                ParentProofId = parentProofId,
                InputHash = WitnessClient.ToBase64Url(inputHash),
                ZeroSeedRequest = WitnessClient.ToBase64Url(VerificationWireCodec.EncodeDrawRequest(input, new byte[32])),
                AuditPayload = WitnessClient.ToBase64Url(input.AuditPayload),
                ClientNonce = WitnessClient.ToBase64Url(VerificationSeedDerivation.CreateCsprngNonce()),
                Context = context,
                AlgorithmId = VerificationWireCodec.GetAlgorithmId(input.AlgorithmProfile),
                AlgorithmSpecId = GetAlgorithmSpecification(input).Id,
                AlgorithmSpecHash = GetAlgorithmSpecification(input).Hash,
                // Formal v3 is tied to the published algorithm manifest. Offline proofs retain their per-mode
                // compatibility ladder, while every server-locked request uses the manifest generation.
                AlgorithmEngineVersion = VerificationWireCodec.GetFormalEngineVersion(input.SamplingMode),
                CandidatePoolCommitment = candidatePoolCommitment,
                RosterDigest = seal.RosterDigest
            };
            proof = await witnessClient.NotarizeAsync(request, cancellationToken).ConfigureAwait(false);
            result = VerificationWireCodec.DecodeDrawResponse(GetResponse(proof));
            var replay = kernel.Draw(input, WitnessClient.DeriveFormalSeed(proof));
            if (!replay.Winners.SequenceEqual(result.Winners)
                || !result.Winners.Select(winner => winner.RecordId).SequenceEqual(proof.Result.WinnerRecordIds))
                throw new InvalidDataException("Formal notarization result does not replay from its locked evidence.");
        }
        else
        {
            var seed = VerificationSeedDerivation.CreateCsprngSeed();
            result = kernel.Draw(input, seed);
            proof = CreateProof(input, inputHash, seed, result, VerificationProofMode.OfflineReproducible, parentProofId, null,
                context);
        }
        return Complete(records, input.Candidates, input.Count, result, proof, exportContext, FreezeWeights(input));
    }

    private static void EnsureFormalNotarizationSupported(
        VerificationDrawInput input,
        VerificationMode verificationMode)
    {
        if (verificationMode != VerificationMode.FormalNotarized)
            return;

        if (input.SamplingMode == VerificationSamplingMode.SequentialOrder
            || input.AlgorithmProfile is VerificationAlgorithmProfile.StudentOrchestrated
                or VerificationAlgorithmProfile.LotteryOrchestrated
            || string.Equals(input.DrawModeToken, "orchestrated", StringComparison.Ordinal))
            throw FormalNotarizationException.UnsupportedForOrchestration();

        if (!VerificationAlgorithmSpecificationRegistry.TryResolve(input.Kind, input.SamplingMode,
                input.AlgorithmProfile, out _))
            throw FormalNotarizationException.AlgorithmUnsupported();
    }

    private static VerificationAlgorithmSpecification GetAlgorithmSpecification(VerificationDrawInput input) =>
        VerificationAlgorithmSpecificationRegistry.TryResolve(input.Kind, input.SamplingMode, input.AlgorithmProfile,
            out var specification)
            ? specification
            : throw FormalNotarizationException.AlgorithmUnsupported();

    public void Publish<TCandidate>(VerificationDrawOutcome<TCandidate> outcome)
        where TCandidate : class
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var exported = proofExporter.Save(outcome.Proof, outcome.ExportContext);
        if (outcome.Proof.Mode == VerificationProofMode.OfflineReproducible)
            attestationService.Request(exported.Proof, exported.Path);
    }

    private static IReadOnlyDictionary<Guid, double> FreezeWeights(VerificationDrawInput input)
    {
        // 提交侧的权重快照必须取自 proof 冻结输入，避免与证明分叉；同一记录多次出现（奖品库存）取首个权重。
        return input.Candidates
            .GroupBy(candidate => candidate.RecordId)
            .ToDictionary(group => group.Key, group => group.First().WeightMicros / 1_000_000d);
    }

    private VerificationDrawOutcome<TCandidate> Complete<TCandidate>(
        IReadOnlyCollection<TCandidate> records,
        IReadOnlyList<VerificationCandidate> candidates,
        int requestedCount,
        VerificationKernelResult result,
        DrawProof proof,
        DrawProofExportContext exportContext,
        IReadOnlyDictionary<Guid, double> frozenWeights)
        where TCandidate : class
    {
        if (requestedCount < 1 || requestedCount > candidates.Count || result.Winners.Count != requestedCount)
            throw new InvalidDataException("Verification kernel returned an unexpected winner count.");

        var recordLookup = records
            .GroupBy(GetRecordId)
            .ToDictionary(group => group.Key, group => group.First());
        var candidateSlots = candidates
            .Select(candidate => (candidate.RecordId, candidate.OccurrenceIndex))
            .ToHashSet();
        var consumedSlots = new HashSet<(Guid RecordId, uint OccurrenceIndex)>();
        var winners = result.Winners.Select(winner =>
            {
                var slot = (winner.RecordId, winner.OccurrenceIndex);
                if (!candidateSlots.Contains(slot) || !consumedSlots.Add(slot))
                    throw new InvalidDataException("Verification kernel returned an invalid or repeated inventory slot.");
                return recordLookup.TryGetValue(winner.RecordId, out var record)
                    ? record
                    : throw new InvalidDataException("Verification kernel returned a record outside the frozen pool.");
            })
            .ToList();
        return new VerificationDrawOutcome<TCandidate>(winners, proof, frozenWeights, exportContext);
    }

    private static DrawProof CreateProof(
        VerificationDrawInput input,
        byte[] inputHash,
        byte[] seed,
        VerificationKernelResult result,
        VerificationProofMode mode,
        Guid? parentProofId,
        DrawProofWitness? witness,
        DrawProofContext? context)
    {
        var payload = VerificationWireCodec.EncodeProofPayload(input, seed, result.Winners);
        return new DrawProof
        {
            ParentProofId = parentProofId,
            Mode = mode,
            AlgorithmId = VerificationWireCodec.GetAlgorithmId(input.AlgorithmProfile),
            AlgorithmEngineVersion = VerificationWireCodec.GetEngineVersion(input.SamplingMode),
            InputHash = WitnessClient.ToBase64Url(inputHash),
            Payload = WitnessClient.ToBase64Url(payload),
            AuditPayload = WitnessClient.ToBase64Url(input.AuditPayload),
            Context = context,
            Result = new DrawProofResult { WinnerRecordIds = result.Winners.Select(winner => winner.RecordId).ToList() },
            Witness = witness
        };
    }

    private static Guid GetRecordId<TCandidate>(TCandidate candidate) where TCandidate : class
    {
        return candidate switch
        {
            Student student when student.RecordId != Guid.Empty => student.RecordId,
            Prize prize when prize.RecordId != Guid.Empty => prize.RecordId,
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

    private static byte[] GetResponse(DrawProof proof)
    {
        var payload = WitnessClient.FromBase64Url(proof.Payload);
        if (payload.Length < 49 || !payload.AsSpan(0, 4).SequenceEqual("SRDQ"u8))
            throw new InvalidDataException("Formal notarization payload has an invalid request frame.");

        var candidateCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(13, 4));
        var requestLength = checked(49 + (int)candidateCount * 45);
        if (requestLength >= payload.Length)
            throw new InvalidDataException("Formal notarization payload has no response frame.");

        return payload[requestLength..];
    }

}

public sealed record VerificationDrawOutcome<TCandidate>(
    IReadOnlyList<TCandidate> Winners,
    DrawProof Proof,
    IReadOnlyDictionary<Guid, double> FrozenWeights,
    DrawProofExportContext ExportContext);

/// <summary>
///     The channel-specific facts the caller knows and the frozen input does not: which page drew, which settings
///     page governs it, and the roster commitment to publish beside it.
/// </summary>
public sealed record DrawProofSealContext(
    string Channel,
    string SettingsType,
    int RosterEntryCount,
    string RosterDigest = "",
    /// <summary>Per-proof roster digest salt; empty for a v3 digest or when there is no commitment at all.</summary>
    string RosterDigestSalt = "");
