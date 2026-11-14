using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.SingleInstance;
using SecRandom.Shared.Models.Ipc;

namespace SecRandom.Services.Ipc;

/// <summary>
/// Long-lived event pipe. Kept separate from the short-lived control pipe so instance
/// forwarding never competes with a subscriber for a connection slot.
/// </summary>
public sealed class AutomationEventServer(
    AutomationEventBus eventBus,
    ILogger<AutomationEventServer> logger) : IHostedService
{
    public const int MaxSubscribers = 16;
    public const int MaxQueuedEvents = 256;
    private const int MaxSubscriptionFrame = 64 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _subscriberGate = new(MaxSubscribers, MaxSubscribers);
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
            {
                // Shutdown is best effort; a stuck subscriber must not block host shutdown.
            }
        }

        _cts.Dispose();
        _cts = null;
        _subscriberGate.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var gateEntered = false;
            NamedPipeServerStream? server = null;
            try
            {
                await _subscriberGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateEntered = true;
                server = new NamedPipeServerStream(
                    SingleInstanceService.EventPipeName,
                    PipeDirection.InOut,
                    MaxSubscribers,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var accepted = server;
                server = null;
                gateEntered = false;
                _ = Task.Run(() => HandleSubscriberAsync(accepted, cancellationToken), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                if (gateEntered)
                    _subscriberGate.Release();
                break;
            }
            catch (Exception exception)
            {
                server?.Dispose();
                if (gateEntered)
                    _subscriberGate.Release();
                logger.LogDebug(exception, "Automation event pipe accept failed.");
                try
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleSubscriberAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        await using var connection = server;
        try
        {
            using var reader = new StreamReader(connection, StrictUtf8, false, 1024, leaveOpen: true);
            await using var writer = new StreamWriter(connection, StrictUtf8, leaveOpen: true);

            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(5));
            var handshake = await ReadLineAsync(reader, MaxSubscriptionFrame, handshakeCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(handshake) || !TryReadSubscription(handshake, out var requestedTypes))
            {
                await WriteLineAsync(writer, JsonSerializer.Serialize(new
                {
                    version = 2,
                    type = "error",
                    ok = false,
                    error = new { code = "invalid_request", message = "事件订阅帧无效。" }
                }, JsonOptions)).ConfigureAwait(false);
                return;
            }

            var subscription = new Subscription(requestedTypes);
            void OnPublished(AutomationEvent automationEvent) => subscription.Enqueue(automationEvent);
            eventBus.Published += OnPublished;
            try
            {
                await WriteLineAsync(writer, JsonSerializer.Serialize(new
                {
                    version = 2,
                    type = "subscribed",
                    seqStart = eventBus.CurrentSequence,
                    types = requestedTypes
                }, JsonOptions)).ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    AutomationEvent? automationEvent;
                    try
                    {
                        await subscription.Signal.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                            .ConfigureAwait(false);
                        automationEvent = subscription.TryDequeue();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (automationEvent is null)
                    {
                        if (!connection.IsConnected)
                            break;
                        continue;
                    }

                    var envelope = new IpcV2EventEnvelope(
                        IpcV2RequestEnvelope.CurrentVersion,
                        "event",
                        automationEvent.Seq,
                        automationEvent.Name,
                        automationEvent.Data);
                    await WriteLineAsync(writer, JsonSerializer.Serialize(envelope, JsonOptions))
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                eventBus.Published -= OnPublished;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Automation event subscriber failed.");
        }
        finally
        {
            _subscriberGate.Release();
        }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, int maxLength, CancellationToken cancellationToken)
    {
        var buffer = new char[maxLength + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(length, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (buffer[length] == '\n')
                break;
            length++;
        }

        return length > maxLength ? null : new string(buffer, 0, length).TrimEnd('\r');
    }

    private static async Task WriteLineAsync(StreamWriter writer, string line)
    {
        await writer.WriteLineAsync(line).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static bool TryReadSubscription(string frame, out string[] requestedTypes)
    {
        requestedTypes = [];
        try
        {
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var type = root.TryGetProperty("type", out var typeElement) &&
                       typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
            if (!string.Equals(type, "subscribe", StringComparison.Ordinal) &&
                !string.Equals(type, "rpc", StringComparison.Ordinal))
                return false;

            JsonElement typesElement = default;
            if (root.TryGetProperty("types", out var directTypes))
                typesElement = directTypes;
            else if (root.TryGetProperty("params", out var parameters) &&
                     parameters.ValueKind == JsonValueKind.Object &&
                     parameters.TryGetProperty("types", out var nestedTypes))
                typesElement = nestedTypes;

            if (typesElement.ValueKind == JsonValueKind.Array)
            {
                requestedTypes = typesElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class Subscription
    {
        private readonly HashSet<string> _types;
        private readonly Queue<AutomationEvent> _queue = new();
        private readonly object _sync = new();
        public readonly SemaphoreSlim Signal = new(0);

        public Subscription(IReadOnlyCollection<string> types)
        {
            _types = types.Count == 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(types, StringComparer.Ordinal);
        }

        public void Enqueue(AutomationEvent automationEvent)
        {
            if (_types.Count > 0 && !_types.Contains(automationEvent.Name))
                return;

            lock (_sync)
            {
                if (_queue.Count >= MaxQueuedEvents)
                    _queue.Dequeue();
                _queue.Enqueue(automationEvent);
            }

            if (Signal.CurrentCount == 0)
                Signal.Release();
        }

        public AutomationEvent? TryDequeue()
        {
            lock (_sync)
            {
                return _queue.Count == 0 ? null : _queue.Dequeue();
            }
        }
    }
}