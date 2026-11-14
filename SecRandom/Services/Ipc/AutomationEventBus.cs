using System;
using System.Threading;

namespace SecRandom.Services.Ipc;

public sealed record AutomationEvent(long Seq, string Name, object? Data, DateTimeOffset At);

/// <summary>
/// Process-local event source for the automation event pipe. Core does not reference this:
/// only app-layer aggregation points publish here.
/// </summary>
public sealed class AutomationEventBus
{
    private long _sequence;
    private Action<AutomationEvent>? _published;

    public long CurrentSequence => Interlocked.Read(ref _sequence);

    public event Action<AutomationEvent>? Published
    {
        add => _published += value;
        remove => _published -= value;
    }

    public AutomationEvent Publish(string name, object? data = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var sequence = Interlocked.Increment(ref _sequence);
        var automationEvent = new AutomationEvent(sequence, name, data, DateTimeOffset.Now);
        try
        {
            Volatile.Read(ref _published)?.Invoke(automationEvent);
        }
        catch
        {
            // An event subscriber must never break the operation that published the event.
        }

        return automationEvent;
    }
}