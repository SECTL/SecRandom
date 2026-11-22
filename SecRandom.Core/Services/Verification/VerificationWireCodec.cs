using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SecRandom.Core.Models.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     Defines the byte-level protocol shared by the managed host, native library, and verifier CLI.
/// </summary>
public static class VerificationWireCodec
{
    public const ushort RequestFormatVersion = 3;
    public const ushort ResponseFormatVersion = 1;
    public const string StudentAlgorithmId = "secrandom-fairdraw-history-balanced-weighted-chacha20/v3";
    public const string InventoryLotteryAlgorithmId = "secrandom-inventory-permutation-chacha20/v3";
    public const string WeightedLotteryAlgorithmId = "secrandom-lottery-weighted-without-replacement-chacha20/v3";
    public const string NumberAlgorithmId = "secrandom-number-permutation-chacha20/v3";
    public const string OrchestratedSequenceAlgorithmId = "secrandom-orchestrated-sequential-order/v3";
    public const string GroupUniformAlgorithmId = "secrandom-group-uniform-then-member-uniform-chacha20/v1";
    public const string AlgorithmId = StudentAlgorithmId;

    /// <summary>
    ///     Engine version committed by the published algorithm specifications used for formal notarization.
    ///     Ordinary offline proofs keep <see cref="AlgorithmEngineVersion"/> for compatibility with deployed
    ///     verifiers; formal v3 requests use this newer publication generation.
    /// </summary>
    public const string FormalAlgorithmEngineVersion = "3.6.0";

    /// <summary>
    ///     Engine version stamped into every new proof.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         3.5.0 adds the number-range kind: a "no roster" draw commits its inclusive integer range and samples
    ///         uniformly without replacement over it. A verifier must know the <c>NumberUniformPermutation</c>
    ///         profile before a build that emits this generation is released, exactly like a new audit field.
    ///         Proofs already stamped with an earlier engine version keep replaying under their own branch.
    ///     </para>
    ///     3.4.0 changes no replay arithmetic: student draws still sample over history-balanced weights and the
    ///     frozen candidate weights are unchanged. It marks the proof protocol that may declare a
    ///     <c>fairness.historyScope</c> — the anonymous audit payload can now say that a candidate's
    ///     <c>historyCount</c>/<c>lastDrawnUtc</c> came from a course, a history record group, or both, instead of the
    ///     whole profile history. A 3.3-era verifier would read those narrowed counts as global history, which is why
    ///     the scope declaration needs its own engine version and why <c>fair.sectl.cn</c> must accept 3.4.0 before a
    ///     build that emits it is released. Proofs already stamped 3.3.0 and earlier keep replaying under their own
    ///     branch.
    /// </remarks>
    public const string AlgorithmEngineVersion = "3.5.0";

    /// <summary>
    ///     Engine version that first understands orchestrated (<see cref="VerificationSamplingMode.SequentialOrder" />)
    ///     proofs. Only orchestrated proofs carry it.
    /// </summary>
    /// <remarks>
    ///     The two families are versioned apart on purpose: an already deployed 3.5.0 verifier rejects any proof
    ///     stamped 3.6.0, so stamping ordinary draws with it would break every ordinary attestation until the
    ///     service ships 3.6. Orchestrated proofs cannot exist on a 3.5 verifier at all — it does not know
    ///     <c>SequentialOrder</c> or profiles 19/20 — so widening the version per sampling mode keeps the ordinary
    ///     channel working through the rollout and makes only the new mode wait for the service deployment.
    /// </remarks>
    public const string OrchestratedAlgorithmEngineVersion = "3.6.0";

    /// <summary>Engine generation that first replays two-stage group/member draws.</summary>
    public const string GroupUniformAlgorithmEngineVersion = "3.7.0";

    /// <summary>Engine version a proof of this sampling mode is stamped with.</summary>
    public static string GetEngineVersion(VerificationSamplingMode samplingMode) =>
        samplingMode switch
        {
            VerificationSamplingMode.SequentialOrder => OrchestratedAlgorithmEngineVersion,
            VerificationSamplingMode.GroupUniform => GroupUniformAlgorithmEngineVersion,
            _ => AlgorithmEngineVersion
        };

    /// <summary>Returns the published engine generation for a formal v3 request.</summary>
    public static string GetFormalEngineVersion() => FormalAlgorithmEngineVersion;

    public static string GetFormalEngineVersion(VerificationSamplingMode samplingMode) =>
        samplingMode == VerificationSamplingMode.GroupUniform
            ? GroupUniformAlgorithmEngineVersion
            : FormalAlgorithmEngineVersion;

    private static readonly byte[] InputMagic = "SRDI"u8.ToArray();
    private static readonly byte[] RequestMagic = "SRDQ"u8.ToArray();
    private static readonly byte[] ResponseMagic = "SRDR"u8.ToArray();

    public static byte[] ComputeInputHash(VerificationDrawInput input)
    {
        var inputHash = SHA256.HashData(EncodeInput(input, InputMagic, ReadOnlySpan<byte>.Empty));
        if (input.AuditPayload.Length == 0)
            return inputHash;

        var auditHash = SHA256.HashData(input.AuditPayload);
        var commitment = new byte[inputHash.Length + auditHash.Length];
        inputHash.CopyTo(commitment, 0);
        auditHash.CopyTo(commitment, inputHash.Length);
        return SHA256.HashData(commitment);
    }

    public static string GetAlgorithmId(VerificationSamplingMode samplingMode) => samplingMode switch
    {
        VerificationSamplingMode.HistoryBalancedWeighted => StudentAlgorithmId,
        VerificationSamplingMode.InventoryPermutation => InventoryLotteryAlgorithmId,
        VerificationSamplingMode.WeightedWithoutReplacement => WeightedLotteryAlgorithmId,
        VerificationSamplingMode.SequentialOrder => OrchestratedSequenceAlgorithmId,
        VerificationSamplingMode.GroupUniform => GroupUniformAlgorithmId,
        _ => throw new ArgumentOutOfRangeException(nameof(samplingMode), samplingMode, "Verification sampling mode is unsupported.")
    };

    public static string GetAlgorithmId(VerificationAlgorithmProfile profile) => profile switch
    {
        VerificationAlgorithmProfile.StudentFairRepeat => "secrandom-student-fair-repeat/v3",
        VerificationAlgorithmProfile.StudentFairNoRepeat => "secrandom-student-fair-no-repeat/v3",
        VerificationAlgorithmProfile.StudentFairHalfRepeat => "secrandom-student-fair-half-repeat/v3",
        VerificationAlgorithmProfile.StudentRandomRepeat => "secrandom-student-random-repeat/v3",
        VerificationAlgorithmProfile.StudentRandomNoRepeat => "secrandom-student-random-no-repeat/v3",
        VerificationAlgorithmProfile.StudentRandomHalfRepeat => "secrandom-student-random-half-repeat/v3",
        VerificationAlgorithmProfile.LotteryInventoryCount => "secrandom-lottery-inventory-count/v3",
        VerificationAlgorithmProfile.LotteryCountInternalRule => "secrandom-lottery-count-internal-rule/v3",
        VerificationAlgorithmProfile.LotteryPanRepeat => "secrandom-lottery-pan-repeat/v3",
        VerificationAlgorithmProfile.LotteryPanNoRepeat => "secrandom-lottery-pan-no-repeat/v3",
        VerificationAlgorithmProfile.LotteryPanHalfRepeat => "secrandom-lottery-pan-half-repeat/v3",
        VerificationAlgorithmProfile.StudentFairInternalRuleRepeat => "secrandom-student-fair-internal-rule-repeat/v3",
        VerificationAlgorithmProfile.StudentFairInternalRuleNoRepeat => "secrandom-student-fair-internal-rule-no-repeat/v3",
        VerificationAlgorithmProfile.StudentFairInternalRuleHalfRepeat => "secrandom-student-fair-internal-rule-half-repeat/v3",
        VerificationAlgorithmProfile.StudentRandomInternalRuleRepeat => "secrandom-student-random-internal-rule-repeat/v3",
        VerificationAlgorithmProfile.StudentRandomInternalRuleNoRepeat => "secrandom-student-random-internal-rule-no-repeat/v3",
        VerificationAlgorithmProfile.StudentRandomInternalRuleHalfRepeat => "secrandom-student-random-internal-rule-half-repeat/v3",
        VerificationAlgorithmProfile.NumberUniformPermutation => NumberAlgorithmId,
        VerificationAlgorithmProfile.StudentOrchestrated => "secrandom-student-orchestrated/v3",
        VerificationAlgorithmProfile.LotteryOrchestrated => "secrandom-lottery-orchestrated/v3",
        VerificationAlgorithmProfile.StudentGroupUniform => "secrandom-student-group-uniform/v1",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Verification algorithm profile is unsupported.")
    };

    public static string GetAlgorithmLabel(VerificationAlgorithmProfile profile) => profile switch
    {
        VerificationAlgorithmProfile.StudentFairRepeat => "点名：公平权重，允许重复",
        VerificationAlgorithmProfile.StudentFairNoRepeat => "点名：公平权重，不重复",
        VerificationAlgorithmProfile.StudentFairHalfRepeat => "点名：公平权重，半重复",
        VerificationAlgorithmProfile.StudentRandomRepeat => "点名：随机，允许重复",
        VerificationAlgorithmProfile.StudentRandomNoRepeat => "点名：随机，不重复",
        VerificationAlgorithmProfile.StudentRandomHalfRepeat => "点名：随机，半重复",
        VerificationAlgorithmProfile.LotteryInventoryCount => "抽奖：按剩余数量，等概率库存置换",
        VerificationAlgorithmProfile.LotteryCountInternalRule => "抽奖：按剩余数量，内幕规则加权回退",
        VerificationAlgorithmProfile.LotteryPanRepeat => "抽奖：奖盘加权，允许重复",
        VerificationAlgorithmProfile.LotteryPanNoRepeat => "抽奖：奖盘加权，不重复",
        VerificationAlgorithmProfile.LotteryPanHalfRepeat => "抽奖：奖盘加权，半重复",
        VerificationAlgorithmProfile.StudentFairInternalRuleRepeat => "点名：公平权重，内幕规则加权，允许重复",
        VerificationAlgorithmProfile.StudentFairInternalRuleNoRepeat => "点名：公平权重，内幕规则加权，不重复",
        VerificationAlgorithmProfile.StudentFairInternalRuleHalfRepeat => "点名：公平权重，内幕规则加权，半重复",
        VerificationAlgorithmProfile.StudentRandomInternalRuleRepeat => "点名：随机，内幕规则加权，允许重复",
        VerificationAlgorithmProfile.StudentRandomInternalRuleNoRepeat => "点名：随机，内幕规则加权，不重复",
        VerificationAlgorithmProfile.StudentRandomInternalRuleHalfRepeat => "点名：随机，内幕规则加权，半重复",
        VerificationAlgorithmProfile.NumberUniformPermutation => "数字：范围内等概率不放回",
        VerificationAlgorithmProfile.StudentOrchestrated => "点名：编排抽取（顺序已事先排定）",
        VerificationAlgorithmProfile.LotteryOrchestrated => "抽奖：编排抽取（顺序已事先排定）",
        VerificationAlgorithmProfile.StudentGroupUniform => "点名：小组等概率，组内成员等概率",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Verification algorithm profile is unsupported.")
    };

    public static bool TryGetAlgorithmLabel(string algorithmId, out string label)
    {
        label = algorithmId switch
        {
            "secrandom-student-fair-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentFairRepeat),
            "secrandom-student-fair-no-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentFairNoRepeat),
            "secrandom-student-fair-half-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentFairHalfRepeat),
            "secrandom-student-random-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentRandomRepeat),
            "secrandom-student-random-no-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentRandomNoRepeat),
            "secrandom-student-random-half-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentRandomHalfRepeat),
            "secrandom-lottery-inventory-count/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.LotteryInventoryCount),
            "secrandom-lottery-count-internal-rule/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.LotteryCountInternalRule),
            "secrandom-lottery-pan-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.LotteryPanRepeat),
            "secrandom-lottery-pan-no-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.LotteryPanNoRepeat),
            "secrandom-lottery-pan-half-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.LotteryPanHalfRepeat),
            "secrandom-student-fair-internal-rule-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentFairInternalRuleRepeat),
            "secrandom-student-fair-internal-rule-no-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentFairInternalRuleNoRepeat),
            "secrandom-student-fair-internal-rule-half-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentFairInternalRuleHalfRepeat),
            "secrandom-student-random-internal-rule-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentRandomInternalRuleRepeat),
            "secrandom-student-random-internal-rule-no-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentRandomInternalRuleNoRepeat),
            "secrandom-student-random-internal-rule-half-repeat/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentRandomInternalRuleHalfRepeat),
            NumberAlgorithmId => GetAlgorithmLabel(VerificationAlgorithmProfile.NumberUniformPermutation),
            "secrandom-student-orchestrated/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentOrchestrated),
            "secrandom-lottery-orchestrated/v3" => GetAlgorithmLabel(VerificationAlgorithmProfile.LotteryOrchestrated),
            "secrandom-student-group-uniform/v1" => GetAlgorithmLabel(VerificationAlgorithmProfile.StudentGroupUniform),
            InventoryLotteryAlgorithmId => "按剩余数量",
            WeightedLotteryAlgorithmId => "加权无放回",
            OrchestratedSequenceAlgorithmId => "按预定顺序取出（不消耗随机数）",
            _ => string.Empty
        };
        return !string.IsNullOrEmpty(label);
    }

    public static bool IsSamplingModeCompatible(
        VerificationAlgorithmProfile profile,
        VerificationSamplingMode samplingMode)
    {
        return profile switch
        {
            VerificationAlgorithmProfile.StudentFairRepeat
                or VerificationAlgorithmProfile.StudentFairNoRepeat
                or VerificationAlgorithmProfile.StudentFairHalfRepeat
                or VerificationAlgorithmProfile.StudentFairInternalRuleRepeat
                or VerificationAlgorithmProfile.StudentFairInternalRuleNoRepeat
                or VerificationAlgorithmProfile.StudentFairInternalRuleHalfRepeat
                => samplingMode == VerificationSamplingMode.HistoryBalancedWeighted,
            VerificationAlgorithmProfile.StudentRandomRepeat
                or VerificationAlgorithmProfile.StudentRandomNoRepeat
                or VerificationAlgorithmProfile.StudentRandomHalfRepeat
                or VerificationAlgorithmProfile.StudentRandomInternalRuleRepeat
                or VerificationAlgorithmProfile.StudentRandomInternalRuleNoRepeat
                or VerificationAlgorithmProfile.StudentRandomInternalRuleHalfRepeat
                or VerificationAlgorithmProfile.LotteryCountInternalRule
                or VerificationAlgorithmProfile.LotteryPanRepeat
                or VerificationAlgorithmProfile.LotteryPanNoRepeat
                or VerificationAlgorithmProfile.LotteryPanHalfRepeat
                => samplingMode == VerificationSamplingMode.WeightedWithoutReplacement,
            VerificationAlgorithmProfile.LotteryInventoryCount
                or VerificationAlgorithmProfile.NumberUniformPermutation
                => samplingMode == VerificationSamplingMode.InventoryPermutation,
            // An orchestrated draw replays from the committed positions alone, so it only accepts the sequential
            // sampler: pairing it with a weighted or history sampler would let an arranged order replay as a
            // random draw, which is exactly the claim this profile exists to prevent.
            VerificationAlgorithmProfile.StudentOrchestrated
                or VerificationAlgorithmProfile.LotteryOrchestrated
                => samplingMode == VerificationSamplingMode.SequentialOrder,
            VerificationAlgorithmProfile.StudentGroupUniform
                => samplingMode == VerificationSamplingMode.GroupUniform,
            _ => false
        };
    }

    public static bool IsKindCompatible(VerificationAlgorithmProfile profile, VerificationDrawKind kind)
    {
        return profile switch
        {
            VerificationAlgorithmProfile.StudentFairRepeat
                or VerificationAlgorithmProfile.StudentFairNoRepeat
                or VerificationAlgorithmProfile.StudentFairHalfRepeat
                or VerificationAlgorithmProfile.StudentFairInternalRuleRepeat
                or VerificationAlgorithmProfile.StudentFairInternalRuleNoRepeat
                or VerificationAlgorithmProfile.StudentFairInternalRuleHalfRepeat
                or VerificationAlgorithmProfile.StudentRandomRepeat
                or VerificationAlgorithmProfile.StudentRandomNoRepeat
                or VerificationAlgorithmProfile.StudentRandomHalfRepeat
                or VerificationAlgorithmProfile.StudentRandomInternalRuleRepeat
                or VerificationAlgorithmProfile.StudentRandomInternalRuleNoRepeat
                or VerificationAlgorithmProfile.StudentRandomInternalRuleHalfRepeat
                => kind == VerificationDrawKind.Student,
            VerificationAlgorithmProfile.LotteryInventoryCount
                or VerificationAlgorithmProfile.LotteryCountInternalRule
                or VerificationAlgorithmProfile.LotteryPanRepeat
                or VerificationAlgorithmProfile.LotteryPanNoRepeat
                or VerificationAlgorithmProfile.LotteryPanHalfRepeat
                => kind == VerificationDrawKind.Prize,
            VerificationAlgorithmProfile.NumberUniformPermutation
                => kind == VerificationDrawKind.Number,
            VerificationAlgorithmProfile.StudentOrchestrated
                => kind == VerificationDrawKind.Student,
            VerificationAlgorithmProfile.LotteryOrchestrated
                => kind == VerificationDrawKind.Prize,
            VerificationAlgorithmProfile.StudentGroupUniform
                => kind == VerificationDrawKind.Student,
            _ => false
        };
    }

    public static byte[] EncodeDrawRequest(VerificationDrawInput input, ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32)
            throw new ArgumentException("Verification seeds must contain exactly 32 bytes.", nameof(seed));

        return EncodeInput(input, RequestMagic, seed);
    }

    public static string ComputeCandidatePoolCommitment(VerificationDrawInput input)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("SecRandomCandidatePool/v1\0"u8);
        AppendInt32(hash, CanonicalizeCandidates(input).Count);
        foreach (var candidate in CanonicalizeCandidates(input))
        {
            hash.AppendData(Encoding.ASCII.GetBytes(candidate.RecordId.ToString("N")));
            AppendUInt32(hash, candidate.OccurrenceIndex);
            hash.AppendData([candidate.IsGuaranteed ? (byte)1 : (byte)0]);
            AppendInt64(hash, candidate.WeightMicros);
        }

        return Convert.ToBase64String(hash.GetHashAndReset()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string ComputeCandidatePoolCommitmentFromRequest(ReadOnlySpan<byte> request)
    {
        if (request.Length < 49 || !request[..4].SequenceEqual(RequestMagic)
            || BinaryPrimitives.ReadUInt16LittleEndian(request[4..]) != RequestFormatVersion)
            throw new InvalidDataException("Verification request frame is invalid.");
        var candidateCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(request[13..]));
        if (request.Length != checked(49 + candidateCount * 45))
            throw new InvalidDataException("Verification candidate pool frame length is invalid.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("SecRandomCandidatePool/v1\0"u8);
        AppendInt32(hash, candidateCount);
        for (var index = 0; index < candidateCount; index++)
        {
            var offset = 49 + index * 45;
            hash.AppendData(request.Slice(offset, 32));
            hash.AppendData(request.Slice(offset + 32, 4));
            hash.AppendData(request.Slice(offset + 36, 1));
            hash.AppendData(request.Slice(offset + 37, 8));
        }

        return Convert.ToBase64String(hash.GetHashAndReset()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] EncodeProofPayload(
        VerificationDrawInput input,
        ReadOnlySpan<byte> seed,
        IReadOnlyList<VerificationWinner> winners)
    {
        var request = EncodeDrawRequest(input, seed);
        var result = EncodeDrawResponse(winners);
        var payload = new byte[request.Length + result.Length];
        request.CopyTo(payload, 0);
        result.CopyTo(payload, request.Length);
        return payload;
    }

    public static VerificationKernelResult DecodeDrawResponse(ReadOnlySpan<byte> response)
    {
        var offset = 0;
        EnsureMagic(response, ref offset, ResponseMagic);
        var version = ReadUInt16(response, ref offset);
        if (version != ResponseFormatVersion)
            throw new InvalidDataException($"Unsupported verification response version {version}.");

        var winnerCount = ReadUInt32(response, ref offset);
        if (winnerCount > int.MaxValue)
            throw new InvalidDataException("Verification response winner count is too large.");

        var expectedLength = checked(offset + (int)winnerCount * 36);
        if (response.Length != expectedLength)
            throw new InvalidDataException("Verification response length does not match the winner count.");

        var winners = new VerificationWinner[(int)winnerCount];
        for (var index = 0; index < winners.Length; index++)
        {
            winners[index] = new VerificationWinner(ReadGuid(response, ref offset), ReadUInt32(response, ref offset));
        }

        return new VerificationKernelResult { Winners = winners };
    }

    /// <summary>
    /// Decodes the request and response frames stored in a proof payload for offline replay.
    /// </summary>
    public static VerificationProofPayload DecodeProofPayload(ReadOnlySpan<byte> payload, byte[] auditPayload)
    {
        if (payload.Length < 49 + 10 || !payload[..4].SequenceEqual(RequestMagic))
            throw new InvalidDataException("Verification proof payload has an invalid request frame.");

        var version = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
        if (version != RequestFormatVersion)
            throw new InvalidDataException($"Unsupported verification request version {version}.");

        var kind = (VerificationDrawKind)payload[6];
        var samplingMode = (VerificationSamplingMode)payload[7];
        var profile = (VerificationAlgorithmProfile)payload[8];
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[9..]));
        var candidateCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload[13..]));
        var requestLength = checked(49 + candidateCount * 45);
        if (count <= 0 || candidateCount <= 0 || count > candidateCount || payload.Length <= requestLength)
            throw new InvalidDataException("Verification proof payload has invalid request counts.");

        if (!Enum.IsDefined(kind) || !Enum.IsDefined(samplingMode) || !Enum.IsDefined(profile)
            || !IsKindCompatible(profile, kind) || !IsSamplingModeCompatible(profile, samplingMode))
            throw new InvalidDataException("Verification proof payload declares an incompatible algorithm.");

        var candidates = new VerificationCandidate[candidateCount];
        for (var index = 0; index < candidateCount; index++)
        {
            var offset = 49 + index * 45;
            var recordIdText = Encoding.ASCII.GetString(payload.Slice(offset, 32));
            if (!Guid.TryParseExact(recordIdText, "N", out var recordId))
                throw new InvalidDataException("Verification proof payload contains an invalid record ID.");

            var occurrence = BinaryPrimitives.ReadUInt32LittleEndian(payload[(offset + 32)..]);
            var guaranteed = payload[offset + 36];
            var weight = BinaryPrimitives.ReadInt64LittleEndian(payload[(offset + 37)..]);
            if (guaranteed > 1 || weight < 0)
                throw new InvalidDataException("Verification proof payload contains an invalid candidate.");

            candidates[index] = new VerificationCandidate(recordId, occurrence, weight, guaranteed == 1);
        }

        var seed = payload.Slice(17, 32).ToArray();
        var response = DecodeDrawResponse(payload[requestLength..]);
        var input = new VerificationDrawInput
        {
            Kind = kind,
            SamplingMode = samplingMode,
            AlgorithmProfile = profile,
            Count = count,
            Candidates = candidates,
            AuditPayload = auditPayload ?? []
        };
        return new VerificationProofPayload(input, seed, response);
    }

    public static byte[] EncodeDrawResponse(IReadOnlyList<VerificationWinner> winners)
    {
        var bytes = new List<byte>(checked(10 + winners.Count * 36));
        bytes.AddRange(ResponseMagic);
        WriteUInt16(bytes, ResponseFormatVersion);
        WriteUInt32(bytes, checked((uint)winners.Count));
        foreach (var winner in winners)
        {
            WriteGuid(bytes, winner.RecordId);
            WriteUInt32(bytes, winner.OccurrenceIndex);
        }

        return bytes.ToArray();
    }

    public static IReadOnlyList<VerificationCandidate> CanonicalizeCandidates(VerificationDrawInput input)
    {
        if (input.Count <= 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Draw count must be positive.");

        return input.Candidates
            .OrderBy(candidate => candidate.RecordId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex)
            .Select(candidate =>
            {
                if (candidate.RecordId == Guid.Empty)
                    throw new ArgumentException("Verification candidates require a stable RecordId.", nameof(input));
                if (candidate.WeightMicros < 0)
                    throw new ArgumentException("Verification candidate weights cannot be negative.", nameof(input));
                return candidate;
            })
            .ToArray();
    }

    private static byte[] EncodeInput(VerificationDrawInput input, byte[] magic, ReadOnlySpan<byte> seed)
    {
        var candidates = CanonicalizeCandidates(input);
        if (!Enum.IsDefined(input.Kind) || !Enum.IsDefined(input.SamplingMode) || !Enum.IsDefined(input.AlgorithmProfile)
            || !IsKindCompatible(input.AlgorithmProfile, input.Kind)
            || !IsSamplingModeCompatible(input.AlgorithmProfile, input.SamplingMode))
            throw new ArgumentOutOfRangeException(nameof(input), "Verification algorithm profile is unsupported.");

        var bytes = new List<byte>(checked(49 + candidates.Count * 45));
        bytes.AddRange(magic);
        WriteUInt16(bytes, RequestFormatVersion);
        bytes.Add((byte)input.Kind);
        bytes.Add((byte)input.SamplingMode);
        bytes.Add((byte)input.AlgorithmProfile);
        WriteUInt32(bytes, checked((uint)input.Count));
        WriteUInt32(bytes, checked((uint)candidates.Count));
        if (!seed.IsEmpty)
            bytes.AddRange(seed.ToArray());

        foreach (var candidate in candidates)
        {
            WriteGuid(bytes, candidate.RecordId);
            WriteUInt32(bytes, candidate.OccurrenceIndex);
            bytes.Add(candidate.IsGuaranteed ? (byte)1 : (byte)0);
            WriteInt64(bytes, candidate.WeightMicros);
        }

        return bytes.ToArray();
    }

    private static void WriteGuid(List<byte> bytes, Guid value)
    {
        var text = value.ToString("N");
        bytes.AddRange(Encoding.ASCII.GetBytes(text));
    }

    private static Guid ReadGuid(ReadOnlySpan<byte> source, ref int offset)
    {
        if (offset > source.Length - 32)
            throw new InvalidDataException("Verification response ended while reading a record ID.");

        var value = Encoding.ASCII.GetString(source.Slice(offset, 32));
        offset += 32;
        return Guid.TryParseExact(value, "N", out var result)
            ? result
            : throw new InvalidDataException("Verification response contains an invalid record ID.");
    }

    private static void EnsureMagic(ReadOnlySpan<byte> source, ref int offset, byte[] magic)
    {
        if (source.Length < magic.Length || !source[..magic.Length].SequenceEqual(magic))
            throw new InvalidDataException("Verification frame has an invalid magic value.");
        offset += magic.Length;
    }

    private static void WriteUInt16(List<byte> bytes, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        bytes.AddRange(buffer.ToArray());
    }

    private static void WriteUInt32(List<byte> bytes, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        bytes.AddRange(buffer.ToArray());
    }

    private static void WriteInt64(List<byte> bytes, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        bytes.AddRange(buffer.ToArray());
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, ref int offset)
    {
        if (offset > source.Length - 2)
            throw new InvalidDataException("Verification response ended while reading an integer.");
        var result = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
        offset += 2;
        return result;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, ref int offset)
    {
        if (offset > source.Length - 4)
            throw new InvalidDataException("Verification response ended while reading an integer.");
        var result = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
        offset += 4;
        return result;
    }
}
