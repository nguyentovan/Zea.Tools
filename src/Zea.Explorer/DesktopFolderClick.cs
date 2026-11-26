using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Zea.Explorer;

internal sealed class DesktopFolderClick : IDisposable
{
    private const uint MouseMove = 0x0200;
    private const uint LeftButtonDown = 0x0201;
    private const uint LeftButtonUp = 0x0202;
    private const int MouseHook = 14;
    private const uint InjectedInput = 1;
    private readonly Action<DesktopClick> _clicked;
    private readonly MouseCallback _callback;
    private nint _hook;
    private Press? _first;
    private Press? _second;

    public DesktopFolderClick(Action<DesktopClick> clicked)
    {
        _clicked = clicked;
        _callback = OnMouseInput;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && _hook == 0)
        {
            _hook = SetWindowsHookEx(MouseHook, _callback, GetModuleHandle(null), 0);
            ExplorerTrace.Log($"desktop.mouseHook installed={_hook != 0}");
        }
        else if (!enabled)
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
        _first = null;
        _second = null;
    }

    public static string? ReadItemName(DesktopClick click)
    {
        if (!IsDesktopControl(click.Control))
        {
            ExplorerTrace.Log($"desktop.msaa control is no longer on the Desktop hwnd={click.Control}");
            return null;
        }

        var iid = typeof(IAccessible).GUID;
        IAccessible? list = null;
        IAccessible? item = null;
        try
        {
            var result = AccessibleObjectFromWindow(click.Control, 0xFFFFFFFC, in iid, out list);
            if (result < 0 || list is null)
            {
                ExplorerTrace.Log($"desktop.msaa AccessibleObjectFromWindow hresult=0x{result:X8} hwnd={click.Control}");
                return null;
            }

            var hit = list.HitTest(click.Point.X, click.Point.Y);
            object child;
            if (hit is IAccessible candidate)
            {
                item = candidate;
                child = 0;
            }
            else if (hit is int index && index > 0)
            {
                item = list;
                child = index;
            }
            else
            {
                return null;
            }

            const int roleListItem = 0x22;
            var itemRole = item.Role(child);
            if (itemRole is not int role || role != roleListItem)
            {
                ExplorerTrace.Log($"desktop.msaa hit is not a list item hwnd={click.Control} role={itemRole}");
                return null;
            }

            var name = item.Name(child);
            return name;
        }
        catch (COMException exception)
        {
            ExplorerTrace.Log($"desktop.msaa exception hresult=0x{exception.HResult:X8}");
            return null;
        }
        finally
        {
            if (item is not null && !ReferenceEquals(item, list))
            {
                Marshal.ReleaseComObject(item);
            }
            if (list is not null)
            {
                Marshal.ReleaseComObject(list);
            }
        }
    }

    private nint OnMouseInput(int code, nuint message, nint data)
    {
        if (code == 0)
        {
            var mouse = Marshal.PtrToStructure<MouseInput>(data);
            if (message == MouseMove)
            {
                ExplorerInput.Shared.RecordMove(mouse.Time);
            }
            else
            {
                var input = ExplorerInput.Shared.RecordAction(mouse.Time);
                if ((mouse.Flags & InjectedInput) == 0)
                {
                    if (message == LeftButtonDown)
                    {
                        TrackPress(mouse);
                    }
                    else if (message == LeftButtonUp && _second is { } second)
                    {
                        _second = null;
                        if (IsWithinClickArea(mouse.Point, second.Point))
                        {
                            var click = new DesktopClick(second.Control, second.Point, input);
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (_hook != 0)
                                {
                                    _clicked(click);
                                }
                            });
                        }
                    }
                }
            }
        }

        var result = CallNextHookEx(0, code, message, data);
        return result;
    }

    private void TrackPress(MouseInput mouse)
    {
        _second = null;
        var control = WindowFromPoint(mouse.Point);
        if (!IsDesktopControl(control))
        {
            _first = null;
            return;
        }

        var press = new Press(control, mouse.Point, mouse.Time);
        if (_first is { } first && first.Control == control && unchecked(mouse.Time - first.Time) <= GetDoubleClickTime()
            && IsWithinClickArea(mouse.Point, first.Point))
        {
            TraceDesktopPress(control, _first?.Time, mouse.Time, matched: true);
            _first = null;
            _second = press;
        }
        else
        {
            TraceDesktopPress(control, _first?.Time, mouse.Time, matched: false);
            _first = press;
        }
    }

    [Conditional("DEBUG")]
    private static void TraceDesktopPress(nint control, uint? previousTime, uint time, bool matched)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ExplorerTrace.Log($"desktop.press hwnd={control} previousTick={previousTime} tick={time} matchedDoubleClick={matched}");
        });
    }

    private static bool IsDesktopControl(nint control)
    {
        if (!ExplorerShell.HasClass(control, "SysListView32"))
        {
            return false;
        }

        var root = GetAncestor(control, 2);
        var desktop = ExplorerShell.IsDesktopWindow(root);
        return desktop;
    }

    private static bool IsWithinClickArea(ScreenPoint first, ScreenPoint second)
    {
        var within = 2L * Math.Abs((long)first.X - second.X) < GetSystemMetrics(36)
            && 2L * Math.Abs((long)first.Y - second.Y) < GetSystemMetrics(37);
        return within;
    }

    internal readonly record struct DesktopClick(nint Control, ScreenPoint Point, uint Input);

    private readonly record struct Press(nint Control, ScreenPoint Point, uint Time);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ScreenPoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public ScreenPoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        // Order matches the native IAccessible vtable.
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object Parent();
        int ChildCount();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object Child([MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.BStr)]
        string? Name([MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.BStr)]
        string? Value([MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.BStr)]
        string? Description([MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.Struct)]
        object Role([MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.Struct)]
        object State([MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.BStr)]
        string? Help([MarshalAs(UnmanagedType.Struct)] object id);
        int HelpTopic([MarshalAs(UnmanagedType.BStr)] out string helpFile, [MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.BStr)]
        string? KeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.Struct)]
        object Focus();
        [return: MarshalAs(UnmanagedType.Struct)]
        object Selection();
        [return: MarshalAs(UnmanagedType.BStr)]
        string? DefaultAction([MarshalAs(UnmanagedType.Struct)] object id);
        void Select(int flags, [MarshalAs(UnmanagedType.Struct)] object id);
        void Location(out int x, out int y, out int width, out int height, [MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.Struct)]
        object Navigate(int direction, [MarshalAs(UnmanagedType.Struct)] object id);
        [return: MarshalAs(UnmanagedType.Struct)]
        object HitTest(int x, int y);
    }

    private delegate nint MouseCallback(int code, nuint message, nint data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    private static extern nint SetWindowsHookEx(int id, MouseCallback callback, nint module, uint thread);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? module);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flag);



    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint window, uint id, in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? result);
}
