using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

/// <summary>
///     Submits completed ordinary proofs for server replay attestation.
///     A submission always uses the proof captured at draw time instead of reading the proof file again:
///     a file rewritten between saving and submitting would otherwise be attested in place of the real draw.
///     Failed submissions stay queued with backoff, survive a restart, and remain visible to the user.
/// </summary>
public sealed class DrawProofAttestationService(
    MainConfigHandler configHandler,
    DrawProofExportService proofExporter,
    IWitnessClient witnessClient,
    ITimestampAuthorityClient timestampClient,
    ILogger<DrawProofAttestationService> logger) : BackgroundService
{
    private const string QueueFileName = "attestation-queue.json";
    private const int MaximumQueueLength = 200;
    private const int MaximumAttempts = 6;
    private const int MaximumErrorMessageLength = 200;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DisabledModePollInterval = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, ProofAttestationQueueItem> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private string? _queueFilePath;
    private bool _queueLoaded;
    private string? _lastError;
    private int _chainAlertCount;

    /// <summary>
    ///     Raised whenever the pending/attested state of a proof changes so the settings surface can refresh.
    /// </summary>
    public event EventHandler? StatusChanged;

    public void Request(DrawProof proof, string proofPath)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (string.IsNullOrWhiteSpace(proofPath) || !IsEnabled)
            return;
        if (proof.Mode != VerificationProofMode.OfflineReproducible || !NeedsSubmission(proof))
            return;

        lock (_gate)
        {
            EnsureQueueLoadedLocked();
            _pending[proofPath] = new ProofAttestationQueueItem
            {
                Path = proofPath,
                ProofId = proof.ProofId,
                ProofHash = WitnessClient.ComputeAttestedProofHash(proof),
                Proof = proof
            };
            TrimQueueLocked();
            PersistQueueLocked();
        }

        Wake();
        RaiseStatusChanged();
    }

    public ProofAttestationStatus GetStatus()
    {
        lock (_gate)
        {
            EnsureQueueLoadedLocked();
            var pending = 0;
            var failed = 0;
            var timestampFailed = 0;
            var conflict = 0;
            foreach (var item in _pending.Values)
            {
                switch (item.State)
                {
                    case ProofAttestationState.Pending:
                        pending++;
                        break;
                    case ProofAttestationState.Failed:
                        failed++;
                        if (item.FailedStage == ProofSubmissionStage.Timestamp)
                            timestampFailed++;
                        break;
                    case ProofAttestationState.Conflict:
                        conflict++;
                        break;
                }
            }

            return new ProofAttestationStatus(pending, failed, timestampFailed, conflict, _chainAlertCount, _lastError);
        }
    }

    /// <summary>
    ///     Re-arms proofs whose automatic retries were exhausted and submits everything that is due now.
    /// </summary>
    public async Task<ProofAttestationStatus> RetryNowAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureQueueLoadedLocked();
            foreach (var item in _pending.Values.Where(item => item.State == ProofAttestationState.Failed))
            {
                item.State = ProofAttestationState.Pending;
                item.Attempts = 0;
                item.NextAttemptUtc = DateTimeOffset.UtcNow;
            }

            PersistQueueLocked();
        }

        RaiseStatusChanged();
        await ProcessDueAsync(force: true, cancellationToken).ConfigureAwait(false);
        return GetStatus();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        lock (_gate)
        {
            EnsureQueueLoadedLocked();
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetNextDelay();
            try
            {
                await _wake.WaitAsync(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            await ProcessDueAsync(force: false, stoppingToken).ConfigureAwait(false);
        }
    }

    private bool IsEnabled => configHandler.Data.General.Verification.Mode == VerificationMode.Ordinary;

    private TimeSpan GetNextDelay()
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
                return Timeout.InfiniteTimeSpan;

            // A proof may have been queued while ordinary mode was active; poll slowly so switching back resumes it.
            if (!IsEnabled)
                return DisabledModePollInterval;

            var delay = _pending.Values.Min(item => item.NextAttemptUtc) - DateTimeOffset.UtcNow;
            return delay <= TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
    }

    private async Task ProcessDueAsync(bool force, CancellationToken stoppingToken)
    {
        if (!IsEnabled)
            return;

        ProofAttestationQueueItem[] items;
        lock (_gate)
        {
            EnsureQueueLoadedLocked();
            var now = DateTimeOffset.UtcNow;
            items = _pending.Values
                .Where(item => item.State != ProofAttestationState.Conflict)
                .Where(item => force || item.NextAttemptUtc <= now)
                .OrderBy(item => item.CreatedAtUtc)
                .ToArray();
        }

        foreach (var item in items)
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            await ProcessItemAsync(item, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessItemAsync(ProofAttestationQueueItem item, CancellationToken stoppingToken)
    {
        var proof = ResolveProof(item);
        if (proof is null)
            return;

        var receipt = proof.Witness?.Receipt ?? item.Receipt;
        var timestampToken = proof.Witness?.TimestampToken;
        var requestedReceipt = string.IsNullOrWhiteSpace(receipt);
        var requestedTimestamp = timestampClient.IsEnabled && string.IsNullOrWhiteSpace(timestampToken);
        var stage = requestedReceipt ? ProofSubmissionStage.Receipt : ProofSubmissionStage.Timestamp;

        try
        {
            if (requestedReceipt)
            {
                using var receiptTimeout = CreateRequestTimeout(stoppingToken);
                var attestation = await witnessClient.AttestAsync(proof, receiptTimeout.Token).ConfigureAwait(false);
                receipt = attestation.Receipt;
                // Kept in memory so a later timestamp failure does not ask the service to re-attest the same proof.
                item.Receipt = receipt;
                RecordChainAnchor(proof, attestation.Chain);
            }

            if (requestedTimestamp)
            {
                stage = ProofSubmissionStage.Timestamp;
                using var timestampTimeout = CreateRequestTimeout(stoppingToken);
                var digest = WitnessClient.FromBase64Url(WitnessClient.ComputeAttestedProofHash(proof));
                timestampToken = await timestampClient.TimestampAsync(digest, timestampTimeout.Token).ConfigureAwait(false);
            }

            CompleteItem(item, proof, receipt, timestampToken);
            if (requestedReceipt)
                logger.LogInformation("服务端已完成抽取证明重放验证。ProofId={ProofId}", proof.ProofId);
            if (requestedTimestamp)
                logger.LogInformation("时间戳服务已完成抽取证明盖章。ProofId={ProofId}", proof.ProofId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown is not a submission failure; the queue entry stays pending for the next run.
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                RecordFailureLocked(item, exception, stage);
                PersistQueueLocked();
            }

            RaiseStatusChanged();
        }
    }

    /// <summary>
    ///     Surfaces a chain position the service does not consider continuous with what it recorded before.
    ///     "behind" is the interesting one: the submitted chain is older than the anchor the service holds,
    ///     which is what a deleted-and-rebuilt local chain looks like from the outside.
    /// </summary>
    private void RecordChainAnchor(DrawProof proof, WitnessChainAnchor? anchor)
    {
        if (anchor is null || anchor.Status is not ("broken" or "gap" or "behind" or "conflict"))
            return;

        lock (_gate)
        {
            _chainAlertCount++;
        }

        logger.LogWarning(
            "服务端证明链锚定报告异常。ProofId={ProofId}，状态={Status}，服务端链头={HeadIndex}",
            proof.ProofId, anchor.Status, anchor.HeadIndex);
        RaiseStatusChanged();
    }

    /// <summary>
    ///     Each stage gets its own budget: a slow attestation must not consume the time-stamp request's window.
    /// </summary>
    private static CancellationTokenSource CreateRequestTimeout(CancellationToken stoppingToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        source.CancelAfter(RequestTimeout);
        return source;
    }

    private bool NeedsSubmission(DrawProof proof)
    {
        if (string.IsNullOrWhiteSpace(proof.Witness?.Receipt))
            return true;

        return timestampClient.IsEnabled && string.IsNullOrWhiteSpace(proof.Witness?.TimestampToken);
    }

    private DrawProof? ResolveProof(ProofAttestationQueueItem item)
    {
        if (item.Proof is { } queued)
            return queued;

        // The in-memory proof does not survive a restart, so the file is the only copy left.
        // It is submitted only while it still matches the hash recorded when the draw completed.
        if (!proofExporter.TryRead(item.Path, out var onDisk) || onDisk is null)
        {
            RemoveItem(item, "证明文件不存在");
            return null;
        }

        if (!string.Equals(WitnessClient.ComputeAttestedProofHash(onDisk), item.ProofHash, StringComparison.Ordinal))
        {
            MarkConflict(item, "证明文件内容与生成时不一致，已拒绝提交");
            return null;
        }

        return onDisk;
    }

    private void CompleteItem(ProofAttestationQueueItem item, DrawProof proof, string? receipt, string? timestampToken)
    {
        var witness = proof.Witness;
        var attested = proof with
        {
            Witness = new DrawProofWitness
            {
                Challenge = witness?.Challenge,
                Receipt = receipt,
                TimestampToken = timestampToken,
                KeyId = witness?.KeyId
            }
        };

        lock (_gate)
        {
            try
            {
                if (!FileMatchesLocked(item))
                {
                    item.State = ProofAttestationState.Conflict;
                    item.Receipt = receipt;
                    item.LastError = "提交期间证明文件被改动，已保留服务端回执但未覆盖该文件";
                    item.NextAttemptUtc = DateTimeOffset.MaxValue;
                    _lastError = item.LastError;
                    logger.LogWarning("抽取证明在提交期间被改动，已保留回执但未覆盖文件。ProofPath={ProofPath}", item.Path);
                }
                else
                {
                    proofExporter.SaveAtPath(item.Path, attested);
                    _pending.Remove(item.Path);
                    _lastError = null;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The anchors were obtained; only writing them back to the proof file failed.
                RecordFailureLocked(item, exception, item.FailedStage);
            }

            PersistQueueLocked();
        }

        RaiseStatusChanged();
    }

    private bool FileMatchesLocked(ProofAttestationQueueItem item)
    {
        return proofExporter.TryRead(item.Path, out var current)
               && current is not null
               && string.Equals(WitnessClient.ComputeAttestedProofHash(current), item.ProofHash, StringComparison.Ordinal);
    }

    private void RecordFailureLocked(ProofAttestationQueueItem item, Exception exception, ProofSubmissionStage stage)
    {
        item.Attempts++;
        item.FailedStage = stage;
        item.LastError = Truncate(exception.Message, MaximumErrorMessageLength);
        _lastError = item.LastError;
        if (item.Attempts >= MaximumAttempts)
        {
            item.State = ProofAttestationState.Failed;
            item.NextAttemptUtc = DateTimeOffset.MaxValue;
            logger.LogWarning(exception, "抽取证明提交达到重试上限，已停止自动重试。ProofPath={ProofPath}", item.Path);
            return;
        }

        item.NextAttemptUtc = DateTimeOffset.UtcNow + GetRetryDelay(item.Attempts);
        logger.LogDebug(exception, "服务端抽取证明重放验证失败，稍后重试。ProofPath={ProofPath}，尝试次数={Attempts}", item.Path, item.Attempts);
    }

    private void MarkConflict(ProofAttestationQueueItem item, string reason)
    {
        lock (_gate)
        {
            item.State = ProofAttestationState.Conflict;
            item.LastError = reason;
            item.NextAttemptUtc = DateTimeOffset.MaxValue;
            _lastError = reason;
            PersistQueueLocked();
        }

        logger.LogWarning("抽取证明提交被拒绝：{Reason}。ProofPath={ProofPath}", reason, item.Path);
        RaiseStatusChanged();
    }

    private void RemoveItem(ProofAttestationQueueItem item, string reason)
    {
        lock (_gate)
        {
            _pending.Remove(item.Path);
            PersistQueueLocked();
        }

        logger.LogDebug("已移除抽取证明提交任务：{Reason}。ProofPath={ProofPath}", reason, item.Path);
        RaiseStatusChanged();
    }

    private void TrimQueueLocked()
    {
        var surplus = _pending.Count - MaximumQueueLength;
        if (surplus <= 0)
            return;

        foreach (var stale in _pending.Values.OrderBy(item => item.CreatedAtUtc).Take(surplus).ToArray())
        {
            _pending.Remove(stale.Path);
            logger.LogWarning("待提交抽取证明超过上限，已放弃最早的提交任务。ProofPath={ProofPath}", stale.Path);
        }
    }

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A processing pass is already scheduled.
        }
    }

    private void RaiseStatusChanged()
    {
        try
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "抽取证明状态订阅者处理失败。");
        }
    }

    private void EnsureQueueLoadedLocked()
    {
        if (_queueLoaded)
            return;

        _queueLoaded = true;
        var path = ResolveQueueFilePath();
        if (path is null || !File.Exists(path))
            return;

        try
        {
            var document = JsonSerializer.Deserialize<ProofAttestationQueueDocument>(File.ReadAllText(path), JsonOptions);
            foreach (var item in document?.Items ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Path))
                    continue;

                // The proof object itself is never persisted; a restarted app re-reads and re-checks the file.
                item.Proof = null;
                _pending[item.Path] = item;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "抽取证明待提交队列读取失败，已按空队列继续。");
        }
    }

    private void PersistQueueLocked()
    {
        var path = ResolveQueueFilePath();
        if (path is null)
            return;

        try
        {
            if (_pending.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var document = new ProofAttestationQueueDocument { Items = _pending.Values.ToList() };
            var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            logger.LogWarning(exception, "抽取证明待提交队列写入失败。");
        }
    }

    private string? ResolveQueueFilePath()
    {
        if (_queueFilePath is not null)
            return _queueFilePath;

        try
        {
            _queueFilePath = Utils.GetFilePath("proofs", QueueFileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogDebug(exception, "无法解析抽取证明待提交队列路径。");
        }

        return _queueFilePath;
    }

    private static TimeSpan GetRetryDelay(int attempts)
    {
        var factor = 1L << Math.Min(Math.Max(attempts - 1, 0), 10);
        return TimeSpan.FromTicks(Math.Min(InitialRetryDelay.Ticks * factor, MaximumRetryDelay.Ticks));
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

public sealed record ProofAttestationStatus(
    int PendingCount,
    int FailedCount,
    int TimestampFailedCount,
    int ConflictCount,
    int ChainAlertCount,
    string? LastError)
{
    /// <summary>
    ///     Failures that happened while submitting the receipt. Timestamp failures are counted separately so a
    ///     third-party time-stamp authority being unavailable never reads as a failed draw attestation.
    /// </summary>
    public int ReceiptFailedCount => Math.Max(0, FailedCount - TimestampFailedCount);

    public bool HasOutstandingWork => PendingCount > 0 || FailedCount > 0;

    public bool HasIssues => FailedCount > 0 || ConflictCount > 0 || ChainAlertCount > 0;
}

internal enum ProofSubmissionStage
{
    Receipt,
    Timestamp
}

internal enum ProofAttestationState
{
    Pending,
    Failed,
    Conflict
}

internal sealed class ProofAttestationQueueDocument
{
    public int FormatVersion { get; set; } = 1;

    public List<ProofAttestationQueueItem> Items { get; set; } = [];
}

internal sealed class ProofAttestationQueueItem
{
    public string Path { get; set; } = string.Empty;

    public Guid ProofId { get; set; }

    public string ProofHash { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public ProofAttestationState State { get; set; } = ProofAttestationState.Pending;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset NextAttemptUtc { get; set; } = DateTimeOffset.UtcNow;

    public ProofSubmissionStage FailedStage { get; set; } = ProofSubmissionStage.Receipt;

    public string? LastError { get; set; }

    public string? Receipt { get; set; }

    [JsonIgnore]
    public DrawProof? Proof { get; set; }
}
