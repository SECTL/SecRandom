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
    public const string AlgorithmId = StudentAlgorithmId;
    public const string AlgorithmEngineVersion = "3.3.0";

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
            InventoryLotteryAlgorithmId => "按剩余数量",
            WeightedLotteryAlgorithmId => "加权无放回",
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
                => samplingMode == VerificationSamplingMode.InventoryPermutation,
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
            _ => false
        };
    }

    public static byte[] EncodeDrawRequest(VerificationDrawInput input, ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32)
            throw new ArgumentException("Verification seeds must contain exactly 32 bytes.", nameof(seed));

        return EncodeInput(input, RequestMagic, seed);
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
