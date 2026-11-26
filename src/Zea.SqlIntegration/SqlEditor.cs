using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Zea.SqlIntegration;

internal sealed record SqlEditor(nint Window, nint Focus, uint ProcessId, string WindowTitle,
    nint TabControl, int TabId, string? TabName, string? Dialect)
{
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const int RolePageTabList = 0x3C;
    private const int RolePageTab = 0x25;
    private const int StateSelected = 0x02;
    private const uint KeyboardInput = 1;
    private const uint KeyUp = 0x02;
    private const ushort Control = 0x11;

    public static SqlEditor Read(nint window, bool format)
    {
        var threadId = GetWindowThreadProcessId(window, out var processId);
        var info = new GuiThreadInfo();
        info.Size = (uint)Marshal.SizeOf<GuiThreadInfo>();
        if (GetForegroundWindow() != window || threadId == 0 || !GetGUIThreadInfo(threadId, ref info) || info.Focus == 0)
        {
            SqlTrace.Log("editor.unavailable reason=focus");
            throw new InvalidOperationException("Cửa sổ soạn thảo đã mất focus.");
        }

        using var process = Process.GetProcessById((int)processId);
        var title = new StringBuilder(512);
        GetWindowText(window, title, title.Capacity);
        if (process.ProcessName.Equals("ssms", StringComparison.OrdinalIgnoreCase))
        {
            var dialect = format ? "tsql" : null;
            var editor = new SqlEditor(window, info.Focus, processId, title.ToString(), 0, 0, null, dialect);
            return editor;
        }

        if (process.ProcessName.Equals("dbeaver", StringComparison.OrdinalIgnoreCase))
        {
            if (format)
            {
                var editor = ReadDBeaverTab(window, info.Focus, processId, title.ToString());
                return editor;
            }

            var editorWithoutDialect = new SqlEditor(window, info.Focus, processId, title.ToString(), 0, 0, null, null);
            return editorWithoutDialect;
        }

        SqlTrace.Log("editor.unavailable reason=unsupportedEditor");
        throw new InvalidOperationException("Lệnh SQL chỉ dùng trong SSMS hoặc DBeaver.");
    }

    public bool IsCurrent()
    {
        try
        {
            var current = Read(Window, Dialect is not null);
            return current == this;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or COMException)
        {
            return false;
        }
    }

    public void SendShortcut(ushort key)
    {
        if (GetForegroundWindow() != Window)
        {
            SqlTrace.Log($"editor.changed stage=sendShortcut key=0x{key:X2}");
            throw new InvalidOperationException("Cửa sổ hoặc trạng thái phím đã thay đổi. Hãy thử lại.");
        }

        if (GetAsyncKeyState(Control) < 0 || GetAsyncKeyState(0x10) < 0 || GetAsyncKeyState(0x12) < 0 ||
            GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0)
        {
            SqlTrace.Log($"editor.shortcutBlocked reason=modifiers key=0x{key:X2}");
            throw new InvalidOperationException("Cửa sổ hoặc trạng thái phím đã thay đổi. Hãy thử lại.");
        }

        var inputs = new Input[]
        {
            CreateInput(Control, 0), CreateInput(key, 0),
            CreateInput(key, KeyUp), CreateInput(Control, KeyUp)
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            SqlTrace.Log($"editor.shortcutFailed key=0x{key:X2} sent={sent} expected={inputs.Length}");
            if (sent != 0)
            {
                var releases = new Input[] { CreateInput(key, KeyUp), CreateInput(Control, KeyUp) };
                SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
            }

            throw new InvalidOperationException("Windows không gửi được phím tới trình soạn thảo. Kiểm tra quyền của hai ứng dụng.");
        }
    }

    public static uint WindowProcess(nint window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    private static SqlEditor ReadDBeaverTab(nint window, nint focus, uint processId, string title)
    {
        var interfaceId = typeof(IAccessible).GUID;
        var className = new StringBuilder(80);
        for (var control = focus; control != 0 && control != window; control = GetParent(control))
        {
            className.Clear();
            GetClassName(control, className, className.Capacity);
            if (!className.ToString().StartsWith("SWT_Window", StringComparison.Ordinal))
            {
                continue;
            }

            var result = AccessibleObjectFromWindow(control, ObjIdClient, in interfaceId, out var tabs);
            if (result != 0 || tabs is null)
            {
                continue;
            }

            try
            {
                if (tabs.Role(0) is not RolePageTabList)
                {
                    continue;
                }

                SqlEditor? selected = null;
                var count = tabs.ChildCount();
                for (var id = 1; id <= count; id++)
                {
                    if (tabs.Role(id) is not RolePageTab || tabs.State(id) is not int state || (state & StateSelected) == 0)
                    {
                        continue;
                    }

                    var help = tabs.Help(id);
                    if (help is null || !help.StartsWith("Script:", StringComparison.Ordinal) && !help.Contains("\nScript:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (selected is not null)
                    {
                        SqlTrace.Log("editor.unavailable reason=multipleDBeaverTabs");
                        throw new InvalidOperationException("Không xác định được duy nhất tab SQL đang mở trong DBeaver.");
                    }

                    var name = tabs.Name(id);
                    var dialect = DBeaverSqlDialect.Parse(help);
                    selected = new SqlEditor(window, focus, processId, title, control, id, name, dialect);
                }

                if (selected is not null)
                {
                    return selected;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(tabs);
            }
        }

        SqlTrace.Log("editor.unavailable reason=noDBeaverTab");
        throw new InvalidOperationException("Không xác định được tab SQL đang mở. Hãy đưa focus về trình soạn thảo DBeaver.");
    }

    private static Input CreateInput(ushort key, uint flags)
    {
        var input = new Input();
        input.Type = KeyboardInput;
        input.Data.Keyboard.Key = key;
        input.Data.Keyboard.Flags = flags;
        return input;
    }

    // INPUT is sized by MOUSEINPUT even when sending keyboard events.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardData Keyboard;
        [FieldOffset(0)] public MouseData Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        // Keep these slots in the IAccessible vtable order.
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
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint window, uint objectId, in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? result);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder title, int capacity);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
