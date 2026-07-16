using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class DrawProofAttestationService(
    MainConfigHandler configHandler,
    DrawProofExportService proofExporter,
    IWitnessClient witnessClient,
    ILogger<DrawProofAttestationService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, byte> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Request(string proofPath)
    {
        if (!IsEnabled)
            return;

        Queue(proofPath);
    }

    private bool IsEnabled => configHandler.Data.General.Verification.Mode == VerificationMode.Ordinary;

    private void Queue(string proofPath)
    {
        if (_pendingPaths.TryAdd(proofPath, 0))
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // A retry pass is already scheduled.
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _wake.WaitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            await ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
            return;

        foreach (var path in _pendingPaths.Keys)
        {
            if (!proofExporter.TryRead(path, out var proof) || proof is null)
            {
                _pendingPaths.TryRemove(path, out _);
                continue;
            }

            if (proof.Mode != VerificationProofMode.OfflineReproducible
                || !string.IsNullOrWhiteSpace(proof.Witness?.Receipt))
            {
                _pendingPaths.TryRemove(path, out _);
                continue;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var receipt = await witnessClient.AttestAsync(proof, timeout.Token).ConfigureAwait(false);
                proofExporter.SaveAtPath(path, proof with { Witness = new DrawProofWitness { Receipt = receipt } });
                _pendingPaths.TryRemove(path, out _);
                logger.LogInformation("服务端已完成抽取证明重放验证。ProofId={ProofId}", proof.ProofId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _pendingPaths.TryRemove(path, out _);
                logger.LogDebug(exception, "服务端抽取证明重放验证失败，不会自动重试。ProofPath={ProofPath}", path);
            }
        }
    }
}
