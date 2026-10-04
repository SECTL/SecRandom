namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     最近处理过的命令 ID，用于满足"投递语义是 at-least-once"下的幂等要求。
/// </summary>
/// <remarks>
///     同一条 <c>command_id</c> 可能到达两次（重连补投、网络重发）。重复到达时**不重复执行**，
///     直接回上次的回执。这里只需要一个带过期时间的有界记录：命令 ID 不会长期复用，
///     也用不着落盘——重启后待执行命令本来就应当丢弃（与"不补执行过期命令"一致）。
/// </remarks>
internal sealed class ControlCommandLedger(int capacity, TimeSpan timeToLive, TimeProvider timeProvider)
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly object _gate = new();

    public bool TryGet(string commandId, out ControlCommandRecord record)
    {
        lock (_gate)
        {
            Prune();
            if (_entries.TryGetValue(commandId, out var entry))
            {
                record = entry.Record;
                return true;
            }

            record = null!;
            return false;
        }
    }

    public void Remember(string commandId, ControlCommandRecord record)
    {
        lock (_gate)
        {
            Prune();

            if (_entries.ContainsKey(commandId))
                return;

            _entries[commandId] = new Entry(record, timeProvider.GetUtcNow());
            _order.Enqueue(commandId);

            while (_order.Count > capacity)
                _entries.Remove(_order.Dequeue());
        }
    }

    private void Prune()
    {
        var deadline = timeProvider.GetUtcNow() - timeToLive;
        while (_order.Count > 0 && _entries.TryGetValue(_order.Peek(), out var oldest) && oldest.At <= deadline)
            _entries.Remove(_order.Dequeue());
    }

    private readonly record struct Entry(ControlCommandRecord Record, DateTimeOffset At);
}
