using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     Owns the append-only evidence chain over <c>data/proofs</c>. Each new proof records the previous
///     proof's chain hash, and the running head lives in <c>data/proofs/chain-head.json</c>.
///     Deleting proofs is recorded explicitly, because the app itself expires proofs by age and by storage
///     limit — an unrecorded deletion is what the chain is supposed to make visible.
/// </summary>
public sealed class ProofChainStore(ILogger<ProofChainStore> logger)
{
    public const int CurrentFormatVersion = 1;
    public const string HeadFileName = "chain-head.json";

    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SecRandomProof/v3/chain");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    private readonly object _gate = new();
    private ProofChainHeadDocument? _head;
    private bool _headLoaded;

    public static string ComputeSelfHash(long index, string previousHash, string proofHash)
    {
        var previous = Encoding.ASCII.GetBytes(previousHash ?? string.Empty);
        var current = Encoding.ASCII.GetBytes(proofHash ?? string.Empty);
        var material = new byte[DomainSeparator.Length + sizeof(long) + previous.Length + current.Length];
        var offset = 0;
        DomainSeparator.CopyTo(material, offset);
        offset += DomainSeparator.Length;
        BitConverter.TryWriteBytes(material.AsSpan(offset), index);
        offset += sizeof(long);
        previous.CopyTo(material, offset);
        offset += previous.Length;
        current.CopyTo(material, offset);
        return WitnessClient.ToBase64Url(SHA256.HashData(material));
    }

    /// <summary>
    ///     Reserves the next chain position and advances the persisted head. The head is written before the
    ///     proof file, so a crash can leave at most one missing tail entry, which the verifier reports
    ///     separately from an interior gap.
    /// </summary>
    public DrawProofChain NextChain(DrawProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        lock (_gate)
        {
            var head = EnsureHeadLocked();
            var index = head.HeadIndex + 1;
            var previousHash = head.HeadHash;
            var proofHash = WitnessClient.ComputeAttestedProofHash(proof);
            var selfHash = ComputeSelfHash(index, previousHash, proofHash);
            head.HeadIndex = index;
            head.HeadHash = selfHash;
            PersistLocked(ProofChainEvent.ProofAppended);
            return new DrawProofChain
            {
                FormatVersion = CurrentFormatVersion,
                Index = index,
                PrevHash = previousHash,
                SelfHash = selfHash
            };
        }
    }

    /// <summary>
    ///     Records proofs the app itself removed (retention or storage-limit cleanup) so their absence is
    ///     explained instead of looking like tampering. Only a contiguous run starting right above the
    ///     current floor may advance <see cref="ProofChainHead.RetainedFromIndex"/>: a sparse deletion such
    ///     as index 10 while 1-9 still exist must not turn those surviving gaps into "expired" files.
    /// </summary>
    public void RecordRemovedIndices(IEnumerable<long> indices, ProofChainEvent reason)
    {
        ArgumentNullException.ThrowIfNull(indices);

        var removed = indices.Where(index => index > 0).ToArray();
        if (removed.Length == 0)
            return;

        lock (_gate)
        {
            var head = EnsureHeadLocked();
            if (head.RemovedIndices is null)
                head.RemovedIndices = [];

            var changed = false;
            foreach (var index in removed)
            {
                if (index <= head.RetainedFromIndex || head.RemovedIndices.Contains(index))
                    continue;

                head.RemovedIndices.Add(index);
                changed = true;
            }

            while (head.RemovedIndices.Remove(head.RetainedFromIndex + 1))
            {
                head.RetainedFromIndex++;
                changed = true;
            }

            // Anything at or below the floor is already explained by the floor itself.
            changed |= head.RemovedIndices.RemoveAll(index => index <= head.RetainedFromIndex) > 0;
            if (!changed)
                return;

            head.RemovedIndices.Sort();
            PersistLocked(reason);
        }
    }

    public ProofChainHead Read()
    {
        lock (_gate)
        {
            var head = EnsureHeadLocked();
            return new ProofChainHead(
                head.FormatVersion,
                head.HeadIndex,
                head.HeadHash,
                head.RetainedFromIndex,
                head.LastEvent,
                head.LastEventAtUtc);
        }
    }

    private ProofChainHeadDocument EnsureHeadLocked()
    {
        if (_headLoaded && _head is not null)
            return _head;

        _headLoaded = true;
        _head = new ProofChainHeadDocument();
        var path = TryResolveHeadPath();
        if (path is null || !File.Exists(path))
            return _head;

        try
        {
            var loaded = JsonSerializer.Deserialize<ProofChainHeadDocument>(File.ReadAllText(path), JsonOptions);
            if (loaded is not null)
                _head = loaded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "证明链头读取失败，已从空链头继续。");
        }

        return _head;
    }

    private void PersistLocked(ProofChainEvent reason)
    {
        var path = TryResolveHeadPath();
        if (path is null || _head is null)
            return;

        try
        {
            _head.FormatVersion = CurrentFormatVersion;
            _head.LastEvent = reason.ToString();
            _head.LastEventAtUtc = DateTimeOffset.UtcNow;
            var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_head, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "证明链头写入失败，证明文件仍会照常保存。");
        }
    }

    private string? TryResolveHeadPath()
    {
        try
        {
            return Utils.GetFilePath("proofs", HeadFileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogDebug(exception, "无法解析证明链头路径。");
            return null;
        }
    }
}

public enum ProofChainEvent
{
    ProofAppended,
    RetentionCleanup,
    StorageLimitCleanup,
    Restore
}

public sealed record ProofChainHead(
    int FormatVersion,
    long HeadIndex,
    string HeadHash,
    long RetainedFromIndex,
    string? LastEvent,
    DateTimeOffset LastEventAtUtc);

internal sealed class ProofChainHeadDocument
{
    public int FormatVersion { get; set; } = ProofChainStore.CurrentFormatVersion;

    public long HeadIndex { get; set; }

    public string HeadHash { get; set; } = string.Empty;

    public long RetainedFromIndex { get; set; }

    /// <summary>
    ///     Removed indices above the floor that are not yet a contiguous prefix. They are what allows a later
    ///     deletion to close the hole and advance the floor without ever over-claiming.
    /// </summary>
    public List<long> RemovedIndices { get; set; } = [];

    public string? LastEvent { get; set; }

    public DateTimeOffset LastEventAtUtc { get; set; }
}
