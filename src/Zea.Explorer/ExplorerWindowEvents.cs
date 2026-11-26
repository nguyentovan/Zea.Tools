using System.Runtime.InteropServices;

namespace Zea.Explorer;

internal sealed class ExplorerWindowEvents
{
    private const uint Destroy = 0x8001;
    private const uint Show = 0x8002;
    private const uint SkipOwnProcess = 2;
    private readonly Action<nint, bool> _onChanged;
    private readonly WinEventCallback _callback;
    private nint _hook;

    public ExplorerWindowEvents(Action<nint, bool> onChanged)
    {
        _onChanged = onChanged;
        _callback = OnWinEvent;
    }

    public bool IsRegistered => _hook != 0;

    public void Start()
    {
        if (_hook == 0)
        {
            _hook = SetWinEventHook(Destroy, Show, 0, _callback, 0, 0, SkipOwnProcess);
        }

        ExplorerTrace.Log($"watch.hook registered={IsRegistered}");
    }

    public void Stop()
    {
        if (_hook == 0)
        {
            return;
        }

        var stopped = UnhookWinEvent(_hook);
        if (stopped)
        {
            _hook = 0;
        }

        ExplorerTrace.Log($"watch.hook stopped={stopped}");
    }

    private void OnWinEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time)
    {
        if (window == 0 || objectId != 0 || childId != 0 || hook != _hook)
        {
            return;
        }

        _onChanged(window, eventType == Show);
    }

    private delegate void WinEventCallback(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId, uint time);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint firstEvent, uint lastEvent, nint module, WinEventCallback callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
}
