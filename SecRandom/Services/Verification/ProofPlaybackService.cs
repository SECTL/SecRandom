using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Profile;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed record ProofPlaybackCandidate(Guid RecordId, string Text);

public sealed record ProofPlaybackResult(Guid RecordId, string Text, uint OccurrenceIndex);

public sealed class ProofPlaybackDocument
{
    public required string Path { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required Guid ProofId { get; init; }
    public required string Format { get; init; }
    public required VerificationDrawKind Kind { get; init; }
    public required string MainPageId { get; init; }
    public string? RosterName { get; init; }
    public int RosterEntryCount { get; init; }
    public required string AlgorithmProfile { get; init; }
    public required string AlgorithmId { get; init; }
    public required int RequestedCount { get; init; }
    public required IReadOnlyList<ProofPlaybackCandidate> Candidates { get; init; }
    public required IReadOnlyList<ProofPlaybackResult> Results { get; init; }
    public required bool IsLocallyVerified { get; init; }
    public string? VerificationError { get; init; }
}

public sealed record ProofPlaybackSummary(
    string Path,
    DateTimeOffset CreatedAtUtc,
    Guid ProofId,
    VerificationDrawKind Kind,
    string AlgorithmProfile,
    string AlgorithmId,
    int CandidateCount,
    int ResultCount);

public sealed record ProofPlaybackDateGroup(DateOnly Date, IReadOnlyList<ProofPlaybackSummary> Documents)
{
    public string DateText => Date.ToString("yyyy-MM-dd");
}

/// <summary>
/// Reads proof files and performs the same deterministic replay used by the draw kernel. It deliberately does not
/// write history or alter the active profile; playback is a temporary presentation of an existing proof.
/// </summary>
public sealed class ProofPlaybackService(
    DrawProofExportService proofExporter,
    IVerificationKernel kernel,
    IProfileCatalogManager profileCatalog,
    ILogger<ProofPlaybackService> logger)
{
    /// <summary>
    /// Loads only the data needed to render the proof list. Replay and roster lookups are deliberately deferred
    /// until the user starts playback so opening the page does not run the verification kernel for every file.
    /// </summary>
    public Task<IReadOnlyList<ProofPlaybackDateGroup>> LoadDateGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadDateGroups(cancellationToken), cancellationToken);

    public IReadOnlyList<ProofPlaybackDateGroup> LoadDateGroups(CancellationToken cancellationToken = default)
    {
        var documents = proofExporter.EnumerateProofPaths()
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return TryLoadSummary(path);
            })
            .Where(document => document is not null)
            .Cast<ProofPlaybackSummary>()
            .OrderByDescending(document => document.CreatedAtUtc)
            .ToArray();

        return documents
            .GroupBy(document => DateOnly.FromDateTime(document.CreatedAtUtc.LocalDateTime))
            .OrderByDescending(group => group.Key)
            .Select(group => new ProofPlaybackDateGroup(group.Key, group.ToArray()))
            .ToArray();
    }

    /// <summary>Loads and verifies one proof for playback after the user has selected it.</summary>
    public ProofPlaybackDocument? TryLoad(string path)
    {
        try
        {
            if (!proofExporter.TryRead(path, out var proof) || proof is null)
                return null;

            var payload = WitnessClient.FromBase64Url(proof.Payload);
            var auditPayload = WitnessClient.FromBase64Url(proof.AuditPayload);
            var decoded = VerificationWireCodec.DecodeProofPayload(payload, auditPayload);
            var replay = kernel.Draw(decoded.Input, decoded.Seed);
            var payloadMatches = replay.Winners.SequenceEqual(decoded.Result.Winners);
            var proofMatches = replay.Winners.Select(winner => winner.RecordId)
                .SequenceEqual(proof.Result.WinnerRecordIds);
            var inputHashMatches = WitnessClient.ToBase64Url(
                VerificationWireCodec.ComputeInputHash(decoded.Input)) == proof.InputHash;
            var poolMatches = proof.CandidatePoolCommitment is null
                || proof.CandidatePoolCommitment == VerificationWireCodec.ComputeCandidatePoolCommitment(decoded.Input);
            var signed = ValidateWitnessBinding(proof, payload, auditPayload);
            var verified = payloadMatches && proofMatches && inputHashMatches && poolMatches && signed;
            var (displayNames, rosterName) = verified
                ? LoadDisplayData(decoded.Input.Kind, decoded.Input.Candidates)
                : (new Dictionary<Guid, string>(), null);

            return new ProofPlaybackDocument
            {
                Path = path,
                CreatedAtUtc = proof.CreatedAtUtc,
                ProofId = proof.ProofId,
                Format = proof.Format,
                Kind = decoded.Input.Kind,
                MainPageId = decoded.Input.Kind == VerificationDrawKind.Prize ? "main.lottery" : "main.rollCall",
                RosterName = rosterName,
                RosterEntryCount = proof.Context?.Roster?.EntryCount ?? 0,
                AlgorithmProfile = proof.Context?.AlgorithmProfile ?? string.Empty,
                AlgorithmId = proof.AlgorithmId,
                RequestedCount = proof.Result.WinnerRecordIds.Count,
                Candidates = decoded.Input.Candidates
                    .Select(candidate => new ProofPlaybackCandidate(
                        candidate.RecordId,
                        displayNames.TryGetValue(candidate.RecordId, out var text)
                            ? text
                            : candidate.RecordId.ToString("N")))
                    .ToArray(),
                Results = replay.Winners
                    .Select(winner => new ProofPlaybackResult(
                        winner.RecordId,
                        displayNames.TryGetValue(winner.RecordId, out var text)
                            ? text
                            : winner.RecordId.ToString("N"),
                        winner.OccurrenceIndex))
                    .ToArray(),
                IsLocallyVerified = verified,
                VerificationError = verified
                    ? null
                    : "证明中的输入、候选池、种子、结果或签名与本地验证不一致"
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取证明演示文件失败：文件={Path}。", path);
            return null;
        }
    }

    private ProofPlaybackSummary? TryLoadSummary(string path)
    {
        try
        {
            if (!proofExporter.TryRead(path, out var proof) || proof is null)
                return null;

            var payload = WitnessClient.FromBase64Url(proof.Payload);
            var decoded = VerificationWireCodec.DecodeProofPayload(payload, []);

            return new ProofPlaybackSummary(path, proof.CreatedAtUtc, proof.ProofId, decoded.Input.Kind,
                proof.Context?.AlgorithmProfile ?? string.Empty, proof.AlgorithmId,
                decoded.Input.Candidates.Count, decoded.Result.Winners.Count);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取证明摘要失败：文件={Path}。", path);
            return null;
        }
    }

    private static bool ValidateWitnessBinding(DrawProof proof, byte[] payload, byte[] auditPayload)
    {
        if (proof.Witness?.Receipt is not { Length: > 0 } receiptToken)
            return proof.Mode == VerificationProofMode.OfflineReproducible;

        try
        {
            var receipt = WitnessClient.VerifyToken<WitnessReceipt>(receiptToken);
            return receipt.ProofId == proof.ProofId
                && receipt.InputHash == proof.InputHash
                && receipt.PayloadHash == WitnessClient.ToBase64Url(SHA256.HashData(payload))
                && receipt.AuditPayloadHash == WitnessClient.ToBase64Url(SHA256.HashData(auditPayload))
                && receipt.ProofHash == WitnessClient.ComputeAttestedProofHash(proof)
                && (proof.Format != DrawProof.FormatV3 || proof.Witness.Challenge is { Length: > 0 });
        }
        catch (Exception)
        {
            return false;
        }
    }

    private (Dictionary<Guid, string> DisplayNames, string? RosterName) LoadDisplayData(
        VerificationDrawKind kind,
        IReadOnlyList<VerificationCandidate> candidates)
    {
        var displayNames = new Dictionary<Guid, string>();
        var candidateIds = candidates.Select(candidate => candidate.RecordId).ToHashSet();
        string? rosterName = null;

        if (kind == VerificationDrawKind.Student)
        {
            foreach (var name in profileCatalog.GetStudentListNames())
            {
                var list = profileCatalog.LoadStudentList(name);
                if (list is null)
                    continue;

                foreach (var student in list.Students)
                    displayNames.TryAdd(student.RecordId, string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name);

                if (rosterName is null && candidateIds.All(id => list.Students.Any(student => student.RecordId == id)))
                    rosterName = name;
            }
        }
        else if (kind == VerificationDrawKind.Prize)
        {
            foreach (var name in profileCatalog.GetPrizeListNames())
            {
                var list = profileCatalog.LoadPrizeList(name);
                if (list is null)
                    continue;

                foreach (var prize in list.Prizes)
                    displayNames.TryAdd(prize.RecordId, string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name);

                if (rosterName is null && candidateIds.All(id => list.Prizes.Any(prize => prize.RecordId == id)))
                    rosterName = name;
            }
        }

        return (displayNames, rosterName);
    }

}
