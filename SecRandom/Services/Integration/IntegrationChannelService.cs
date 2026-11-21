using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Messaging;
using SecRandom.Shared.Models.Profile;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Shared;

namespace SecRandom.Services.Integration;

/// <summary>
///     Official SDK discovery and result delivery. The service owns only local IPC and persistence;
///     UI approval is raised as an event so the settings shell can show the confirmation dialog.
/// </summary>
public sealed class IntegrationChannelService(
    IPluginEventBus eventBus,
    ILogger<IntegrationChannelService> logger) : IHostedService, IDisposable
{
    private const string OfficialPackageId = "SecRandom.Integration.Client";
    private const string AppId = "secrandom.app";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object _gate = new();
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecRandom", "integrations");
    private readonly string _channelsPath = Utils.GetFilePath("config", "integration-channels.json");
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, IntegrationChannel> _channels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenInstances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkageLeaseRequest> _leases = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private IDisposable? _drawSubscription;
    private Task? _serverTask;
    private Task? _discoveryTask;
    private string? _hostManifestPath;

    public string HostPipeName { get; } = "SecRandom_Integration_Host_" + Guid.NewGuid().ToString("N");
    public event Action? Changed;
    public event Action<IntegrationChannel>? ChannelDiscovered;
    public IReadOnlyList<IntegrationChannel> Channels
    {
        get { lock (_gate) return _channels.Values.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(); }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        LoadChannels();
        WriteHostManifest();
        _drawSubscription = eventBus.Subscribe<HostEvents.DrawCompleted>(OnDrawCompleted);
        _serverTask = Task.Run(() => HostPipeLoopAsync(_cts.Token), CancellationToken.None);
        _discoveryTask = Task.Run(() => DiscoveryLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException) { }
        }
        if (_discoveryTask is not null)
        {
            try { await _discoveryTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException) { }
        }
        _drawSubscription?.Dispose();
        _drawSubscription = null;
        if (_hostManifestPath is not null)
        {
            try { File.Delete(_hostManifestPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        ReleaseAllLeases();
    }

    public void Dispose() => _cts.Dispose();

    public void DiscoverNow()
    {
        var changed = false;
        foreach (var manifest in ReadManifests())
        {
            if (!VerifyOfficialManifest(manifest))
                continue;

            lock (_gate)
            {
                if (_channels.TryGetValue(manifest.AppId, out var approved))
                {
                    changed |= approved.ProcessId != manifest.ProcessId
                               || !string.Equals(approved.InstanceId, manifest.InstanceId, StringComparison.Ordinal)
                               || !string.Equals(approved.Endpoint, manifest.Endpoint, StringComparison.Ordinal)
                               || !string.Equals(approved.DisplayName, manifest.DisplayName, StringComparison.Ordinal)
                               || !string.Equals(approved.SdkVersion, manifest.SdkVersion, StringComparison.Ordinal);
                    _channels[manifest.AppId] = approved with
                    {
                        Endpoint = manifest.Endpoint,
                        ProcessId = manifest.ProcessId,
                        InstanceId = manifest.InstanceId,
                        SdkVersion = manifest.SdkVersion,
                        LastSeen = DateTimeOffset.UtcNow
                    };
                    continue;
                }

                if (!_seenInstances.Add(manifest.AppId + "\n" + manifest.InstanceId))
                    continue;
            }

            var channel = new IntegrationChannel(manifest.AppId, manifest.DisplayName, manifest.SdkVersion,
                manifest.Endpoint, manifest.ProcessId, manifest.InstanceId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            ChannelDiscovered?.Invoke(channel);
            QueueApprovalPrompt(channel);
        }
        if (changed)
        {
            SaveChannels();
            Changed?.Invoke();
        }
    }

    public void Approve(IntegrationChannel channel)
    {
        lock (_gate)
            _channels[channel.AppId] = channel;
        SaveChannels();
        Changed?.Invoke();
    }

    public bool Remove(string appId)
    {
        bool removed;
        lock (_gate) removed = _channels.Remove(appId);
        if (removed)
        {
            SaveChannels();
            Changed?.Invoke();
        }
        return removed;
    }

    private async Task DiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                DiscoverNow();
                RemoveDeadLeases();
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogDebug(exception, "Integration discovery failed."); }
        }
    }

    private async Task HostPipeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(HostPipeName, PipeDirection.InOut, 8,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleHostRequestAsync(server, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (IOException) { }
            catch (Exception exception) { logger.LogDebug(exception, "Integration host pipe failed."); }
        }
    }

    private async Task HandleHostRequestAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(server, StrictUtf8, false, 4096, true);
        await using var writer = new StreamWriter(server, StrictUtf8, 4096, true);
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null || Encoding.UTF8.GetByteCount(line) > IntegrationProtocol.MaxFrameBytes)
        {
            await WriteResponseAsync(writer, null, null, new IntegrationError("invalid_request", "Invalid frame.")).ConfigureAwait(false);
            return;
        }

        IntegrationFrame? request;
        try { request = JsonSerializer.Deserialize<IntegrationFrame>(line, IntegrationJson.Options); }
        catch (JsonException) { request = null; }
        if (request is null || request.Version != IntegrationProtocol.Version || request.Type != "rpc")
        {
            await WriteResponseAsync(writer, request?.Id, null, new IntegrationError("invalid_request", "Invalid request.")).ConfigureAwait(false);
            return;
        }

        if (request.Method is "system.ping")
        {
            await WriteResponseAsync(writer, request.Id, new { pong = true }, null).ConfigureAwait(false);
            return;
        }

        if (request.Method is "linkage.acquire" or "linkage.release"
            && request.Params is { ValueKind: JsonValueKind.Object } parameters)
        {
            var linkage = parameters.Deserialize<LinkageLeaseRequest>(IntegrationJson.Options);
            if (linkage is null || string.IsNullOrWhiteSpace(linkage.LeaseId))
            {
                await WriteResponseAsync(writer, request.Id, null,
                    new IntegrationError("invalid_request", "A linkage lease is required.")).ConfigureAwait(false);
                return;
            }

            if (request.Method == "linkage.acquire")
            {
                if (!IsApprovedRequester(linkage) || !IsPipeClientProcess(server, linkage.ProcessId))
                {
                    await WriteResponseAsync(writer, request.Id, null,
                        new IntegrationError("authorization_denied", "The SDK channel is not approved.")).ConfigureAwait(false);
                    return;
                }

                lock (_gate) _leases[linkage.LeaseId] = linkage;
                App.AcquireIntegrationLinkage(linkage.LeaseId);
            }
            else
            {
                if (!IsOwnedLease(linkage) || !IsPipeClientProcess(server, linkage.ProcessId))
                {
                    await WriteResponseAsync(writer, request.Id, null,
                        new IntegrationError("authorization_denied", "The linkage lease does not belong to this process.")).ConfigureAwait(false);
                    return;
                }
                lock (_gate) _leases.Remove(linkage.LeaseId);
                App.ReleaseIntegrationLinkage(linkage.LeaseId);
            }

            await WriteResponseAsync(writer, request.Id, new { accepted = true }, null).ConfigureAwait(false);
            return;
        }

        await WriteResponseAsync(writer, request.Id, null,
            new IntegrationError("unsupported_method", "The method is not supported.")).ConfigureAwait(false);
    }

    private void OnDrawCompleted(HostEvents.DrawCompleted completed) => _ = ForwardDrawCompletedAsync(completed);

    private async Task ForwardDrawCompletedAsync(HostEvents.DrawCompleted completed)
    {
        IntegrationChannel[] channels;
        lock (_gate) channels = _channels.Values.ToArray();
        var members = completed.Channel == DrawPresentationChannel.Lottery
            ? completed.Prizes.Select(prize => new { id = prize.RecordId.ToString("N"), name = prize.Name,
                gender = (string?)null, group = (string?)null, enabled = prize.Exists, weight = prize.Weight }).ToArray()
            : completed.Students.Select(student => new { id = student.RecordId.ToString("N"), name = student.Name,
                gender = (string?)student.Gender, group = (string?)student.Group, enabled = student.Exists, weight = 1d }).ToArray();
        var result = new
        {
            round_id = completed.RoundId,
            roster_id = completed.Channel == DrawPresentationChannel.Lottery ? completed.PrizeListName : completed.ListName,
            mode = (int?)null,
            members,
            at = new DateTimeOffset(completed.DrawTime).ToUniversalTime(),
            idempotency_key = completed.RoundId,
            source_app_id = AppId,
            roster_kind = completed.Channel == DrawPresentationChannel.Lottery ? 1 : 0
        };

        foreach (var channel in channels)
        {
            try
            {
                var manifest = ReadManifests().FirstOrDefault(x =>
                    string.Equals(x.AppId, channel.AppId, StringComparison.Ordinal)
                    && x.ProcessId == channel.ProcessId
                    && string.Equals(x.InstanceId, channel.InstanceId, StringComparison.Ordinal)
                    && string.Equals(x.Endpoint, channel.Endpoint, StringComparison.Ordinal));
                if (manifest is null || !VerifyOfficialManifest(manifest))
                    continue;
                await SendResultAsync(manifest.Endpoint, manifest.ProcessId, result).ConfigureAwait(false);
            }
            catch (Exception exception) { logger.LogDebug(exception, "Integration result delivery failed for {AppId}.", channel.AppId); }
        }
    }

    private static async Task SendResultAsync(string pipeName, int expectedProcessId, object result)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000).ConfigureAwait(false);
        if (!TryGetPipePeerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), localIsServer: false,
                out var serverProcessId)
            || serverProcessId != (uint)expectedProcessId)
            throw new IOException("The SDK notification pipe belongs to a different process.");
        using var writer = new StreamWriter(pipe, StrictUtf8, 4096, true);
        using var reader = new StreamReader(pipe, StrictUtf8, false, 4096, true);
        var request = new IntegrationFrame(IntegrationProtocol.Version, "rpc", Guid.NewGuid().ToString("N"),
            "notification.result", JsonSerializer.SerializeToElement(result, IntegrationJson.Options));
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, IntegrationJson.Options)).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        _ = await reader.ReadLineAsync().ConfigureAwait(false);
    }

    private void RemoveDeadLeases()
    {
        string[] dead;
        lock (_gate) dead = _leases.Values.Where(x => !IsProcessAlive(x.ProcessId)).Select(x => x.LeaseId).ToArray();
        foreach (var leaseId in dead)
        {
            lock (_gate) _leases.Remove(leaseId);
            App.ReleaseIntegrationLinkage(leaseId);
        }
    }

    private void ReleaseAllLeases()
    {
        string[] leases;
        lock (_gate) { leases = _leases.Keys.ToArray(); _leases.Clear(); }
        foreach (var lease in leases) App.ReleaseIntegrationLinkage(lease);
    }

    private IReadOnlyList<IntegrationManifest> ReadManifests()
    {
        if (!Directory.Exists(_directory)) return [];
        var manifests = new List<IntegrationManifest>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            if (string.Equals(Path.GetFileName(file), "host.json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<IntegrationManifest>(File.ReadAllText(file), IntegrationJson.Options);
                if (manifest is not null) manifests.Add(manifest);
            }
            catch (Exception exception) when (exception is IOException or JsonException) { }
        }
        return manifests;
    }

    private bool VerifyOfficialManifest(IntegrationManifest manifest)
    {
        if (manifest.Protocol != IntegrationProtocol.Name || !IsValidEndpoint(manifest.Endpoint)
            || !IsProcessAlive(manifest.ProcessId) || string.IsNullOrWhiteSpace(manifest.PublisherProof))
            return false;
        try
        {
            var proof = JsonSerializer.Deserialize<OfficialSdkProof>(manifest.PublisherProof, IntegrationJson.Options);
            if (proof is null || proof.PackageId != OfficialPackageId || proof.SdkVersion != manifest.SdkVersion)
                return false;
            var key = typeof(IntegrationChannelService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(x => x.Key == "SecRandomIntegrationPublicKey")?.Value;
            if (!VerifyProof(proof, key)) return false;
            using var process = Process.GetProcessById(manifest.ProcessId);
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))).Equals(
                            proof.AssemblySha256, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException
                                             or InvalidOperationException or NotSupportedException
                                             or System.ComponentModel.Win32Exception)
        {
            logger.LogDebug(exception, "SDK manifest verification failed for {AppId}.", manifest.AppId);
        }
        return false;
    }

    private static bool VerifyProof(OfficialSdkProof proof, string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || proof.AssemblySha256.Length != 64 ||
            proof.AssemblySha256.Any(x => !Uri.IsHexDigit(x))) return false;
        var canonical = $"secrandom.integration-sdk/v1\n{proof.PackageId}\n{proof.SdkVersion}\n{proof.AssemblySha256.ToLowerInvariant()}";
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ecdsa.VerifyData(Encoding.UTF8.GetBytes(canonical), Convert.FromBase64String(proof.Signature), HashAlgorithmName.SHA256);
        }
        catch (FormatException) { return false; }
        catch (CryptographicException) { return false; }
    }

    private void LoadChannels()
    {
        try
        {
            var loaded = File.Exists(_channelsPath)
                ? JsonSerializer.Deserialize<List<IntegrationChannel>>(File.ReadAllText(_channelsPath), IntegrationJson.Options)
                : null;
            if (loaded is null) return;
            lock (_gate) foreach (var channel in loaded.Where(x => !string.IsNullOrWhiteSpace(x.AppId))) _channels[channel.AppId] = channel;
        }
        catch (Exception exception) when (exception is IOException or JsonException) { logger.LogWarning(exception, "Unable to load integration channels."); }
    }

    private void SaveChannels()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_channelsPath)!);
            IntegrationChannel[] channels;
            lock (_gate) channels = _channels.Values.ToArray();
            var temporary = _channelsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(channels, IntegrationJson.Options));
            File.Move(temporary, _channelsPath, true);
        }
        catch (IOException exception) { logger.LogWarning(exception, "Unable to save integration channels."); }
    }

    private void WriteHostManifest()
    {
        var manifest = new { app_id = AppId, endpoint = HostPipeName, pid = Environment.ProcessId,
            instance_id = _instanceId, protocol = IntegrationProtocol.Name };
        _hostManifestPath = Path.Combine(_directory, "host.json");
        var temporary = _hostManifestPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, IntegrationJson.Options));
        File.Move(temporary, _hostManifestPath, true);
    }

    private static bool IsProcessAlive(int processId)
    {
        try { return Process.GetProcessById(processId) is { HasExited: false }; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private bool IsApprovedRequester(LinkageLeaseRequest request)
    {
        lock (_gate)
            return _channels.TryGetValue(request.AppId, out var channel)
                   && channel.ProcessId == request.ProcessId
                   && string.Equals(channel.InstanceId, request.InstanceId, StringComparison.Ordinal)
                   && DateTimeOffset.UtcNow - channel.LastSeen < TimeSpan.FromSeconds(8)
                   && IsProcessAlive(request.ProcessId);
    }

    private bool IsOwnedLease(LinkageLeaseRequest request)
    {
        lock (_gate)
            return _leases.TryGetValue(request.LeaseId, out var lease)
                   && lease.ProcessId == request.ProcessId
                   && string.Equals(lease.AppId, request.AppId, StringComparison.Ordinal)
                   && string.Equals(lease.InstanceId, request.InstanceId, StringComparison.Ordinal);
    }

    private static bool IsPipeClientProcess(NamedPipeServerStream server, int expectedProcessId)
    {
        return TryGetPipePeerProcessId(server.SafePipeHandle.DangerousGetHandle(), localIsServer: true,
                   out var processId)
               && processId == (uint)expectedProcessId;
    }

    private static bool TryGetPipePeerProcessId(IntPtr pipeHandle, bool localIsServer, out uint processId)
    {
        if (OperatingSystem.IsWindows())
            return localIsServer
                ? GetNamedPipeClientProcessId(pipeHandle, out processId)
                : GetNamedPipeServerProcessId(pipeHandle, out processId);

        if (OperatingSystem.IsLinux())
        {
            var length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<UnixPeerCredentials>();
            UnixPeerCredentials credentials;
            if (GetSocketOption(pipeHandle.ToInt32(), 1, 17, out credentials, ref length) == 0
                && credentials.ProcessId > 0)
            {
                processId = (uint)credentials.ProcessId;
                return true;
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var length = (uint)sizeof(int);
            int peerProcessId;
            if (GetSocketOption(pipeHandle.ToInt32(), 0, 2, out peerProcessId, ref length) == 0
                && peerProcessId > 0)
            {
                processId = (uint)peerProcessId;
                return true;
            }
        }

        processId = 0;
        return false;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct UnixPeerCredentials
    {
        public int ProcessId;
        public uint UserId;
        public uint GroupId;
    }

    private void QueueApprovalPrompt(IntegrationChannel channel)
    {
        Dispatcher.UIThread.Post(() => _ = PromptForChannelAsync(channel));
    }

    private async Task PromptForChannelAsync(IntegrationChannel channel)
    {
        try
        {
            var accepted = await Dispatcher.UIThread.InvokeAsync(
                () => App.Current.ConfirmIntegrationChannelAsync(channel));
            if (accepted is true)
                Approve(channel);
            else if (accepted is null)
                lock (_gate) _seenInstances.Remove(channel.AppId + "\n" + channel.InstanceId);
        }
        catch (Exception exception)
        {
            lock (_gate) _seenInstances.Remove(channel.AppId + "\n" + channel.InstanceId);
            logger.LogDebug(exception, "Integration channel confirmation was unavailable.");
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetSocketOption(int socket, int level, int option,
        out UnixPeerCredentials value, ref uint optionLength);

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetSocketOption(int socket, int level, int option,
        out int value, ref uint optionLength);

    private static bool IsValidEndpoint(string endpoint) => endpoint.StartsWith("SecRandom_Integration_", StringComparison.Ordinal)
        && endpoint.Length <= 96
        && endpoint.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static async Task WriteResponseAsync(StreamWriter writer, string? id, object? result, IntegrationError? error)
    {
        var response = new IntegrationFrame(IntegrationProtocol.Version, "rpc", id, null, null, result, error);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, IntegrationJson.Options)).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }
}
