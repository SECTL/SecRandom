using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Profile;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class VerificationDrawCoordinator(
    DrawEngine drawEngine,
    IVerificationKernel kernel,
    IWitnessClient witnessClient,
    WitnessTicketCache ticketCache,
    DrawProofExportService proofExporter,
    ILogger<VerificationDrawCoordinator> logger)
{
    public bool IsEnabled => true;

    public Task<VerificationDrawOutcome<Student>> DrawStudentsAsync(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        DrawProofExportContext exportContext,
        Guid? parentProofId = null,
        string courseName = "",
        CancellationToken cancellationToken = default)
    {
        var input = drawEngine.CreateStudentVerificationInput(count, candidates, drawSettingsType, courseName);
        return DrawAsync(input, candidates, exportContext, parentProofId, cancellationToken);
    }

    public Task<VerificationDrawOutcome<Prize>> DrawPrizesAsync(
        int count,
        IReadOnlyDictionary<string, int> temporaryCounts,
        IReadOnlyCollection<Prize> prizes,
        DrawProofExportContext exportContext,
        CancellationToken cancellationToken = default)
    {
        var input = drawEngine.CreatePrizeVerificationInput(count, temporaryCounts);
        return DrawAsync(input, prizes, exportContext, null, cancellationToken);
    }

    private async Task<VerificationDrawOutcome<TCandidate>> DrawAsync<TCandidate>(
        VerificationDrawInput input,
        IReadOnlyCollection<TCandidate> records,
        DrawProofExportContext exportContext,
        Guid? parentProofId,
        CancellationToken cancellationToken)
        where TCandidate : class
    {
        var recordLookup = records.ToDictionary(GetRecordId);
        var inputHash = VerificationWireCodec.ComputeInputHash(input);
        if (ticketCache.TryTake(out var lease))
        {
            try
            {
            var clientNonce = RandomNumberGenerator.GetBytes(32);
            var seed = VerificationSeedDerivation.DeriveOnline(
                inputHash,
                lease.Ticket.TicketId,
                clientNonce,
                WitnessClient.FromBase64Url(lease.Ticket.ServerNonce));
            var result = kernel.Draw(input, seed);
            var pendingProof = CreateProof(input, inputHash, seed, result, VerificationProofMode.OnlineWitnessed, parentProofId,
                new DrawProofWitness { Challenge = lease.Token, KeyId = lease.Ticket.KeyId });
            var localProof = pendingProof with { Mode = VerificationProofMode.OfflineReproducible, Witness = null };
            var outcome = Complete(records, recordLookup, result, localProof, exportContext);
            _ = FinalizeWitnessAsync(lease, clientNonce, pendingProof, exportContext);
            return outcome;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "服务器见证票据无效，已重新生成本地可重放证明。");
            }
        }

        var offlineSeed = RandomNumberGenerator.GetBytes(32);
        var offlineResult = kernel.Draw(input, offlineSeed);
        var offlineProof = CreateProof(input, inputHash, offlineSeed, offlineResult, VerificationProofMode.OfflineReproducible, parentProofId, null);
        return Complete(records, recordLookup, offlineResult, offlineProof, exportContext);
    }

    private async Task FinalizeWitnessAsync(
        TicketLease lease,
        byte[] clientNonce,
        DrawProof pendingProof,
        DrawProofExportContext exportContext)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var receipt = await witnessClient.FinalizeAsync(lease.Token, clientNonce, pendingProof, cancellation.Token)
                .ConfigureAwait(false);
            proofExporter.Save(pendingProof with
            {
                Witness = new DrawProofWitness
                {
                    Challenge = lease.Token,
                    Receipt = receipt,
                    KeyId = lease.Ticket.KeyId
                }
            }, exportContext);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "服务器见证票据结算失败；保留本地可重放证明。ProofId={ProofId}", pendingProof.ProofId);
        }
    }

    private VerificationDrawOutcome<TCandidate> Complete<TCandidate>(
        IReadOnlyCollection<TCandidate> records,
        IReadOnlyDictionary<Guid, TCandidate> recordLookup,
        VerificationKernelResult result,
        DrawProof proof,
        DrawProofExportContext exportContext)
        where TCandidate : class
    {
        var winners = result.Winners.Select(winner => recordLookup.TryGetValue(winner.RecordId, out var record)
            ? record
            : throw new InvalidDataException("Verification kernel returned a record outside the frozen pool."))
            .ToList();
        var proofPath = proofExporter.Save(proof, exportContext);
        return new VerificationDrawOutcome<TCandidate>(winners, proof, proofPath);
    }

    private static DrawProof CreateProof(
        VerificationDrawInput input,
        byte[] inputHash,
        byte[] seed,
        VerificationKernelResult result,
        VerificationProofMode mode,
        Guid? parentProofId,
        DrawProofWitness? witness)
    {
        var payload = VerificationWireCodec.EncodeProofPayload(input, seed, result.Winners);
        return new DrawProof
        {
            ParentProofId = parentProofId,
            Mode = mode,
            AlgorithmId = VerificationWireCodec.AlgorithmId,
            KernelVersion = VerificationWireCodec.KernelVersion,
            InputHash = WitnessClient.ToBase64Url(inputHash),
            Payload = WitnessClient.ToBase64Url(payload),
            AuditPayload = WitnessClient.ToBase64Url(input.AuditPayload),
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

}

public sealed record VerificationDrawOutcome<TCandidate>(
    IReadOnlyList<TCandidate> Winners,
    DrawProof Proof,
    string ProofPath);
