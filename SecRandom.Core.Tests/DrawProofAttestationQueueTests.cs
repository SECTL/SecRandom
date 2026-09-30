using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Verification;
using SecRandom.Services.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

/// <summary>
///     The submission queue must attest the proof captured at draw time, survive a restart,
///     and never overwrite a proof file that changed while the submission was in flight.
/// </summary>
public sealed class DrawProofAttestationQueueTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "SecRandom",
        "proof-attestation-tests",
        Guid.NewGuid().ToString("N"));

    public DrawProofAttestationQueueTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task SubmissionUsesTheProofCapturedAtDrawTimeInsteadOfTheFileOnDisk()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var witness = new RecordingWitnessClient();
        var service = CreateService(provider, witness);

        var original = CreateProof(Guid.NewGuid());
        var path = PathFor("20260930_190000_000_名单_全部_11111111.srproof.json");
        exporter.SaveAtPath(path, original);

        service.Request(original, path);

        var tampered = original with { Result = new DrawProofResult { WinnerRecordIds = [Guid.NewGuid()] } };
        exporter.SaveAtPath(path, tampered);

        var status = await service.RetryNowAsync(TestContext.Current.CancellationToken);

        var submitted = Assert.Single(witness.Submitted);
        Assert.Equal(original.ProofId, submitted.ProofId);
        Assert.Equal(original.Result.WinnerRecordIds, submitted.Result.WinnerRecordIds);

        Assert.Equal(1, status.ConflictCount);
        Assert.Equal(0, status.PendingCount);

        // The rewritten file stays on disk as evidence; it is not silently replaced by the attested original.
        Assert.True(exporter.TryRead(path, out var onDisk));
        Assert.Equal(tampered.Result.WinnerRecordIds, onDisk!.Result.WinnerRecordIds);
        Assert.Null(onDisk.Witness?.Receipt);
    }

    [Fact]
    public async Task FailedSubmissionIsRetriedAndStoresTheReceiptOnSuccess()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var witness = new RecordingWitnessClient { Handler = _ => throw new HttpRequestException("offline") };
        var service = CreateService(provider, witness);

        var proof = CreateProof(Guid.NewGuid());
        var path = PathFor("draw-a.srproof.json");
        exporter.SaveAtPath(path, proof);
        service.Request(proof, path);

        var failed = await service.RetryNowAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, failed.PendingCount);
        Assert.Equal(0, failed.FailedCount);

        witness.Handler = _ => Task.FromResult(new WitnessAttestationResult("receipt-token", null));
        var succeeded = await service.RetryNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, succeeded.PendingCount);
        Assert.Equal(2, witness.Submitted.Count);
        Assert.True(exporter.TryRead(path, out var attested));
        Assert.Equal("receipt-token", attested!.Witness?.Receipt);
    }

    [Fact]
    public async Task PendingSubmissionSurvivesRestartAndResubmitsFromDisk()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var offlineWitness = new RecordingWitnessClient { Handler = _ => throw new HttpRequestException("offline") };
        var service = CreateService(provider, offlineWitness);

        var proof = CreateProof(Guid.NewGuid());
        var path = PathFor("draw-b.srproof.json");
        exporter.SaveAtPath(path, proof);
        service.Request(proof, path);
        await service.RetryNowAsync(TestContext.Current.CancellationToken);

        var restartedWitness = new RecordingWitnessClient();
        var restarted = CreateService(provider, restartedWitness);
        Assert.Equal(1, restarted.GetStatus().PendingCount);

        var status = await restarted.RetryNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, status.PendingCount);
        Assert.Single(restartedWitness.Submitted);
        Assert.True(exporter.TryRead(path, out var attested));
        Assert.Equal("receipt-token", attested!.Witness?.Receipt);
    }

    [Fact]
    public async Task RestartedSubmissionRefusesAProofFileThatChangedAfterTheDraw()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var offlineWitness = new RecordingWitnessClient { Handler = _ => throw new HttpRequestException("offline") };
        var service = CreateService(provider, offlineWitness);

        var proof = CreateProof(Guid.NewGuid());
        var path = PathFor("draw-c.srproof.json");
        exporter.SaveAtPath(path, proof);
        service.Request(proof, path);
        await service.RetryNowAsync(TestContext.Current.CancellationToken);

        exporter.SaveAtPath(path, proof with { Result = new DrawProofResult { WinnerRecordIds = [Guid.NewGuid()] } });

        var restartedWitness = new RecordingWitnessClient();
        var restarted = CreateService(provider, restartedWitness);
        var status = await restarted.RetryNowAsync(TestContext.Current.CancellationToken);

        Assert.Empty(restartedWitness.Submitted);
        Assert.Equal(1, status.ConflictCount);
    }

    [Fact]
    public void ProofsThatAreNotOfflineReproducibleOrAlreadyAttestedAreNotQueued()
    {
        using var provider = CreateProvider();
        var service = CreateService(provider, new RecordingWitnessClient());

        var proof = CreateProof(Guid.NewGuid());
        service.Request(proof with { Witness = new DrawProofWitness { Receipt = "already-attested" } }, PathFor("draw-d.srproof.json"));
        service.Request(proof with { Mode = VerificationProofMode.OnlineWitnessed }, PathFor("draw-e.srproof.json"));

        var status = service.GetStatus();
        Assert.Equal(0, status.PendingCount);
        Assert.Equal(0, status.FailedCount);
        Assert.Equal(0, status.ConflictCount);
    }

    [Fact]
    public async Task AServiceChainAnchorAlertIsCountedWithoutFailingTheSubmission()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var witness = new RecordingWitnessClient
        {
            Handler = _ => Task.FromResult(new WitnessAttestationResult(
                "receipt-token",
                new WitnessChainAnchor { Status = "behind", HeadIndex = 5, Breaks = 1 }))
        };
        var service = CreateService(provider, witness);

        var proof = CreateProof(Guid.NewGuid());
        var path = PathFor("draw-chain.srproof.json");
        exporter.SaveAtPath(path, proof);
        service.Request(proof, path);

        var status = await service.RetryNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, status.ChainAlertCount);
        Assert.Equal(0, status.PendingCount);
        Assert.True(status.HasIssues);
        Assert.True(exporter.TryRead(path, out var attested));
        Assert.Equal("receipt-token", attested!.Witness?.Receipt);
    }

    [Fact]
    public async Task TimestampFailureIsReportedSeparatelyFromReceiptFailure()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var witness = new RecordingWitnessClient();
        var service = CreateService(provider, witness, new FailingTimestampAuthorityClient());

        var proof = CreateProof(Guid.NewGuid());
        var path = PathFor("draw-timestamp.srproof.json");
        exporter.SaveAtPath(path, proof);
        service.Request(proof, path);

        ProofAttestationStatus? status = null;
        for (var attempt = 0; attempt < 6; attempt++)
            status = await service.RetryNowAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(1, status!.FailedCount);
        Assert.Equal(1, status.TimestampFailedCount);
        Assert.Equal(0, status.ReceiptFailedCount);

        // The receipt is reused across retries, so the service is asked to attest the proof only once.
        Assert.Single(witness.Submitted);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        services.AddSingleton<ProofChainStore>();
        services.AddSingleton<DrawProofExportService>();
        return services.BuildServiceProvider();
    }

    private static DrawProofAttestationService CreateService(
        ServiceProvider provider,
        IWitnessClient witness,
        ITimestampAuthorityClient? timestampClient = null)
    {
        return new DrawProofAttestationService(
            provider.GetRequiredService<MainConfigHandler>(),
            provider.GetRequiredService<DrawProofExportService>(),
            witness,
            timestampClient ?? new DisabledTimestampAuthorityClient(),
            NullLogger<DrawProofAttestationService>.Instance);
    }

    private static string PathFor(string fileName) => Utils.GetFilePath("proofs", fileName);

    private static DrawProof CreateProof(Guid proofId)
    {
        return new DrawProof
        {
            ProofId = proofId,
            Mode = VerificationProofMode.OfflineReproducible,
            AlgorithmId = VerificationWireCodec.AlgorithmId,
            AlgorithmEngineVersion = VerificationWireCodec.AlgorithmEngineVersion,
            InputHash = "input-hash",
            Payload = "payload",
            AuditPayload = "audit-payload",
            Result = new DrawProofResult { WinnerRecordIds = [Guid.NewGuid()] }
        };
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }

    private sealed class RecordingWitnessClient : IWitnessClient
    {
        public Func<DrawProof, Task<WitnessAttestationResult>> Handler { get; set; } =
            _ => Task.FromResult(new WitnessAttestationResult("receipt-token", null));

        public List<DrawProof> Submitted { get; } = [];

        public Task<WitnessAttestationResult> AttestAsync(DrawProof proof, CancellationToken cancellationToken)
        {
            Submitted.Add(proof);
            return Handler(proof);
        }

        public Task<DrawProof> NotarizeAsync(FormalNotarizationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DrawProof());
        }
    }

    internal sealed class DisabledTimestampAuthorityClient : ITimestampAuthorityClient
    {
        public bool IsEnabled => false;

        public Task<string> TimestampAsync(ReadOnlyMemory<byte> hash, CancellationToken cancellationToken)
        {
            return Task.FromResult("timestamp-token");
        }
    }

    internal sealed class FailingTimestampAuthorityClient : ITimestampAuthorityClient
    {
        public bool IsEnabled => true;

        public Task<string> TimestampAsync(ReadOnlyMemory<byte> hash, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("time-stamp authority unavailable");
        }
    }
}
