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
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Profile;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class VerificationDrawCoordinator(
    DrawEngine drawEngine,
    IVerificationKernel kernel,
    DrawProofExportService proofExporter,
    DrawProofAttestationService attestationService,
    MainConfigHandler configHandler,
    IWitnessClient witnessClient)
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
        var verificationMode = configHandler.Data.General.Verification.Mode;
        var includeInternalRules = verificationMode != VerificationMode.FormalNotarized;
        var input = drawEngine.CreateStudentVerificationInput(count, candidates, drawSettingsType, courseName, includeInternalRules);
        return DrawAsync(input, candidates, exportContext, parentProofId, verificationMode, cancellationToken);
    }

    public Task<VerificationDrawOutcome<Prize>> DrawPrizesAsync(
        int count,
        IReadOnlyDictionary<string, int> temporaryCounts,
        IReadOnlyCollection<Prize> prizes,
        DrawProofExportContext exportContext,
        CancellationToken cancellationToken = default)
    {
        var verificationMode = configHandler.Data.General.Verification.Mode;
        var includeInternalRules = verificationMode != VerificationMode.FormalNotarized;
        var input = drawEngine.CreatePrizeVerificationInput(count, temporaryCounts, includeInternalRules);
        return DrawAsync(input, prizes, exportContext, null, verificationMode, cancellationToken);
    }

    private async Task<VerificationDrawOutcome<TCandidate>> DrawAsync<TCandidate>(
        VerificationDrawInput input,
        IReadOnlyCollection<TCandidate> records,
        DrawProofExportContext exportContext,
        Guid? parentProofId,
        VerificationMode verificationMode,
        CancellationToken cancellationToken)
        where TCandidate : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recordLookup = records.ToDictionary(GetRecordId);
        var inputHash = VerificationWireCodec.ComputeInputHash(input);
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
                ClientNonce = WitnessClient.ToBase64Url(VerificationSeedDerivation.CreateCsprngNonce())
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
            proof = CreateProof(input, inputHash, seed, result, VerificationProofMode.OfflineReproducible, parentProofId, null);
        }
        var outcome = Complete(records, recordLookup, result, proof, exportContext, FreezeWeights(input));
        if (proof.Mode == VerificationProofMode.OfflineReproducible)
            attestationService.Request(outcome.ProofPath);
        return outcome;
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
        IReadOnlyDictionary<Guid, TCandidate> recordLookup,
        VerificationKernelResult result,
        DrawProof proof,
        DrawProofExportContext exportContext,
        IReadOnlyDictionary<Guid, double> frozenWeights)
        where TCandidate : class
    {
        var winners = result.Winners.Select(winner => recordLookup.TryGetValue(winner.RecordId, out var record)
            ? record
            : throw new InvalidDataException("Verification kernel returned a record outside the frozen pool."))
            .ToList();
        var proofPath = proofExporter.Save(proof, exportContext);
        return new VerificationDrawOutcome<TCandidate>(winners, proof, proofPath, frozenWeights);
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
            AlgorithmId = VerificationWireCodec.GetAlgorithmId(input.AlgorithmProfile),
            AlgorithmEngineVersion = VerificationWireCodec.AlgorithmEngineVersion,
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
    string ProofPath,
    IReadOnlyDictionary<Guid, double> FrozenWeights);
