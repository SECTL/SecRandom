using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SecRandom.Core.Models.Verification;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     Defines the byte-level protocol shared by the managed host, native library, and verifier CLI.
/// </summary>
public static class VerificationWireCodec
{
    public const ushort FormatVersion = 1;
    public const string AlgorithmId = "secrandom-fairdraw-history-balanced-weighted-chacha20/v3";
    public const string KernelVersion = "3.0.0";

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
        if (version != FormatVersion)
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
        WriteUInt16(bytes, FormatVersion);
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
        var bytes = new List<byte>(checked(43 + candidates.Count * 45));
        bytes.AddRange(magic);
        WriteUInt16(bytes, FormatVersion);
        bytes.Add((byte)input.Kind);
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
