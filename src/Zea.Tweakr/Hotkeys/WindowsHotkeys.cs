using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Zea.SqlIntegration;

namespace Zea.Tweakr;

internal sealed class WindowsHotkeys : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const int SystemKeyDown = 0x0104;
    private const int SystemKeyUp = 0x0105;
    private const uint InjectedFlags = 0x12;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private readonly Action<nint, bool> sqlRequested;
    private readonly Action explorerRequested;
    private readonly Action<uint>? inputObserved;
    private readonly HotkeyChord chord = new HotkeyChord();
    private readonly KeyboardHookProc hookProc;
    private readonly object lifetimeLock = new object();
    private readonly Thread worker;
    private ManualResetEvent? stop = new ManualResetEvent(false);
    private Features requestedFeatures = new Features(false, false, false);
    private Features? appliedFeatures;
    private nint hook;

    public WindowsHotkeys(Action<nint, bool> sqlRequested, Action explorerRequested, Action<uint>? inputObserved = null)
    {
        this.sqlRequested = sqlRequested;
        this.explorerRequested = explorerRequested;
        this.inputObserved = inputObserved;
        hookProc = OnKey;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker = new Thread(() =>
        {
            Listen(started);
        });
        worker.IsBackground = true;
        worker.Name = "Zea.Tweakr keyboard hook";

        try
        {
            worker.Start();
        }
        catch
        {
            stop.Dispose();
            throw;
        }

        try
        {
            started.Task.GetAwaiter().GetResult();
        }
        catch
        {
            worker.Join();
            throw;
        }
    }

    public void SetEnabled(bool formatSql, bool convertLog, bool explorer)
    {
        var current = Volatile.Read(ref requestedFeatures);
        var next = new Features(formatSql, convertLog, explorer);
        if (next != current)
        {
            Volatile.Write(ref requestedFeatures, next);
        }
    }

    public void Dispose()
    {
        lock (lifetimeLock)
        {
            stop?.Set();
        }

        if (Thread.CurrentThread != worker)
        {
            worker.Join();
        }
    }

    private void Listen(TaskCompletionSource started)
    {
        const uint infinite = uint.MaxValue;
        const uint allInput = 0x04FF;
        const uint inputAvailable = 0x0004;
        const uint removeMessage = 1;
        const uint quit = 0x0012;

        try
        {
            // WH_KEYBOARD_LL runs on its installing thread, including while the app UI is busy.
            var module = GetModuleHandleW(null);
            hook = SetWindowsHookExW(LowLevelKeyboardHook, hookProc, module, 0);
            if (hook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Không thể đăng ký phím tắt Windows.");
            }

            var stopSignal = stop!;
            var stopHandle = stopSignal.SafeWaitHandle.DangerousGetHandle();
            started.SetResult();
            while (!stopSignal.WaitOne(0))
            {
                var result = MsgWaitForMultipleObjectsEx(1, in stopHandle, infinite, allInput, inputAvailable);
                if (result == 0)
                {
                    break;
                }

                if (result != 1)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Không thể chờ tin nhắn phím tắt Windows.");
                }

                while (!stopSignal.WaitOne(0) && PeekMessageW(out var message, 0, 0, 0, removeMessage))
                {
                    if (message.Id == quit)
                    {
                        return;
                    }

                    TranslateMessage(in message);
                    DispatchMessageW(in message);
                }
            }
        }
        catch (Exception error)
        {
            if (!started.TrySetException(error))
            {
                Trace.TraceError("Zea.Tweakr keyboard hook message loop failed: {0}", error);
            }
        }
        finally
        {
            if (hook != 0)
            {
                UnhookWindowsHookEx(hook);
                hook = 0;
            }

            lock (lifetimeLock)
            {
                stop?.Dispose();
                stop = null;
            }
        }
    }

    private bool ShouldHandleKey(int key)
    {
        var features = Volatile.Read(ref requestedFeatures);
        if (!ReferenceEquals(appliedFeatures, features))
        {
            chord.SetEnabled(features.FormatSql, features.ConvertLog, features.Explorer);
            appliedFeatures = features;
        }

        var shouldHandle = chord.ShouldHandleKey(key);
        return shouldHandle;
    }

    private nint OnKey(int code, nint message, nint data)
    {
        if (code == 0 && (message == KeyDown || message == KeyUp || message == SystemKeyDown || message == SystemKeyUp))
        {
            var input = Marshal.PtrToStructure<KeyboardInput>(data);
            inputObserved?.Invoke(input.Time);
            var key = (int)input.VirtualKey;
            if ((input.Flags & InjectedFlags) == 0 && ShouldHandleKey(key))
            {
                var isDown = message == KeyDown || message == SystemKeyDown;
                var modifiers = ReadModifiers(key, isDown);
                var isPrefix = isDown && key == HotkeyChord.KeyK && modifiers == HotkeyModifiers.Control && chord.SqlEnabled;
                nint foreground = 0;
                if (chord.HasSqlSequence || isPrefix)
                {
                    foreground = GetForegroundWindow();
                }

                var isSqlEditor = isPrefix && IsSqlEditor(foreground);
                if (isPrefix)
                {
                    SqlTrace.Log($"hotkey.prefix supportedEditor={isSqlEditor}");
                }

                var result = chord.Handle(key, isDown, modifiers, foreground, isSqlEditor, Environment.TickCount64);
                if (result.MaskWindowsKey)
                {
                    // Windows should see a chord so it does not open Start upon Win key-up.
                    const byte unassignedVirtualKey = 0xE8;
                    keybd_event(unassignedVirtualKey, 0, 0, 0);
                    keybd_event(unassignedVirtualKey, 0, 2, 0);
                }

                Dispatch(result);
                if (result.Suppress)
                {
                    return 1;
                }
            }
        }

        var next = CallNextHookEx(hook, code, message, data);
        return next;
    }

    private void Dispatch(HotkeyResult result)
    {
        try
        {
            switch (result.Command)
            {
                case HotkeyCommand.ConvertLog:
                    SqlTrace.Log("hotkey.dispatch command=convertLog");
                    sqlRequested(result.Editor, true);
                    break;
                case HotkeyCommand.FormatSql:
                    SqlTrace.Log("hotkey.dispatch command=format");
                    sqlRequested(result.Editor, false);
                    break;
                case HotkeyCommand.OpenExplorer:
                    explorerRequested();
                    break;
            }
        }
        catch (Exception error)
        {
            Trace.TraceError("Could not dispatch a Zea.Tweakr hotkey: {0}", error);
        }
    }

    private static HotkeyModifiers ReadModifiers(int key, bool isDown)
    {
        var modifiers = HotkeyModifiers.None;
        if (IsPressed(0xA2, key, isDown) || IsPressed(0xA3, key, isDown))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsPressed(0xA0, key, isDown) || IsPressed(0xA1, key, isDown))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsPressed(0xA4, key, isDown) || IsPressed(0xA5, key, isDown))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsPressed(0x5B, key, isDown) || IsPressed(0x5C, key, isDown))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsPressed(int modifier, int key, bool isDown)
    {
        // The low-level callback runs before asynchronous state changes for this event.
        if (key == modifier)
        {
            return isDown;
        }

        var pressed = (GetAsyncKeyState(modifier) & 0x8000) != 0;
        return pressed;
    }

    private static bool IsSqlEditor(nint window)
    {
        if (window == 0 || GetWindowThreadProcessId(window, out var processId) == 0)
        {
            return false;
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return false;
        }

        try
        {
            var path = new StringBuilder(1024);
            var length = (uint)path.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, path, ref length))
            {
                return false;
            }

            var filename = Path.GetFileName(path.ToString());
            var isSqlEditor = string.Equals(filename, "ssms.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filename, "dbeaver.exe", StringComparison.OrdinalIgnoreCase);
            return isSqlEditor;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private sealed record Features(bool FormatSql, bool ConvertLog, bool Explorer);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private delegate nint KeyboardHookProc(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MsgWaitForMultipleObjectsEx(uint count, in nint handle, uint timeout, uint wakeMask, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out NativeMessage message, nint window, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(in NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int hookType, KeyboardHookProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder filename, ref uint length);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scanCode, uint flags, nuint extraInfo);
}
