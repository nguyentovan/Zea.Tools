namespace Zea.Explorer;

internal sealed class ExplorerInput
{
    private const int HookTickHistorySize = 16;
    private readonly object _sync = new();
    private readonly Queue<uint> _recentHookTicks = new(HookTickHistorySize);
    private uint? _lastReportedTick;
    private uint? _lastHookTick;
    private uint _generation;

    public static ExplorerInput Shared { get; } = new();

    public uint RecordAction(uint tick)
    {
        lock (_sync)
        {
            RememberHookTick(tick);
            _generation++;
            return _generation;
        }
    }

    public void RecordMove(uint tick)
    {
        lock (_sync)
        {
            RememberHookTick(tick);
        }
    }

    public uint Observe(uint tick)
    {
        lock (_sync)
        {
            // The system timestamp can lag behind notifications from the low-level hooks.
            // Unknown changes remain significant, even if a hook stops receiving events.
            if (_lastReportedTick is uint previous && tick != previous && !_recentHookTicks.Contains(tick))
            {
                _generation++;
            }

            _lastReportedTick = tick;
            return _generation;
        }
    }

    private void RememberHookTick(uint tick)
    {
        if (_lastHookTick == tick)
        {
            return;
        }

        _lastHookTick = tick;
        if (_recentHookTicks.Count == HookTickHistorySize)
        {
            _recentHookTicks.Dequeue();
        }

        _recentHookTicks.Enqueue(tick);
    }
}
