using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace Zea.Explorer;

internal sealed class ExplorerShell : IDisposable
{
    private const string ExplorerWindowClass = "CabinetWClass";
    private const string ExplorerTabClass = "ShellTabWindowClass";
    private const uint CommandMessage = 0x0111;
    private const uint NewTabCommand = 0xA21B;
    private const uint SelectTabCommand = 0xA221;
    private const uint CloseTabCommand = 0xA021;
    private const uint CloseMessage = 0x0010;
    private const int ClassNameCapacity = 64;
    private static readonly Guid ShellBrowserId = new("000214E2-0000-0000-C000-000000000046");
    [ThreadStatic]
    private static char[]? _classNameBuffer;
    private readonly List<nint> _windows = new();
    private readonly EnumerateWindow _collectWindow;
    private object? _shell;

    public ExplorerShell()
    {
        _collectWindow = CollectWindow;
    }

    // Borrowed until the next scan on the UI thread.
    public List<nint> FindWindows()
    {
        _windows.Clear();
        EnumWindows(_collectWindow, 0);
        return _windows;
    }

    private bool CollectWindow(nint window, nint state)
    {
        if (IsExplorerWindow(window))
        {
            _windows.Add(window);
        }

        return true;
    }

    public static bool IsExplorerWindow(nint window)
    {
        if (!IsWindowVisible(window) || !HasClass(window, ExplorerWindowClass))
        {
            return false;
        }

        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var explorer = process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
            return explorer;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static nint ForegroundWindow()
    {
        var foreground = GetForegroundWindow();
        var root = GetAncestor(foreground, 2);
        return root;
    }

    public static string DescribeWindow(nint window)
    {
        if (window == 0)
        {
            return "none";
        }

        var buffer = _classNameBuffer ??= new char[ClassNameCapacity];
        var length = GetClassName(window, buffer, buffer.Length);
        var className = new string(buffer, 0, length);
        var threadId = GetWindowThreadProcessId(window, out uint processId);
        var description = $"0x{window:X}[class={className},pid={processId},tid={threadId}]";
        return description;
    }

    public static bool IsDesktopWindow(nint window)
    {
        if (!HasClass(window, "Progman") && !HasClass(window, "WorkerW"))
        {
            return false;
        }

        GetWindowThreadProcessId(window, out uint windowProcess);
        GetWindowThreadProcessId(GetShellWindow(), out uint shellProcess);
        var desktop = windowProcess != 0 && windowProcess == shellProcess;
        return desktop;
    }

    public static nint ActiveTab(nint window)
    {
        // Explorer puts the active ShellTabWindowClass first in its child window order.
        // Inactive tabs can also report WS_VISIBLE.
        var active = FindWindowEx(window, 0, ExplorerTabClass, null);
        if (!IsWindowVisible(active))
        {
            return 0;
        }

        return active;
    }

    public static bool IsMinimized(nint window)
    {
        var minimized = IsIconic(window);
        return minimized;
    }

    public static void ActivateWindow(nint window)
    {
        if (IsIconic(window))
        {
            ShowWindowAsync(window, 9);
        }
        SetForegroundWindow(window);
    }

    public static void ActivateMergeTarget(nint window, nint expectedForeground, uint input)
    {
        if (IsIconic(window))
        {
            var restored = ShowWindowAsync(window, 9);
            ExplorerTrace.Log($"activate.restore target={DescribeWindow(window)} accepted={restored}");
        }

        var accepted = SetForegroundWindow(window);
        bool? raised = null;
        if (!accepted && ForegroundWindow() == expectedForeground && LastInput() == input)
        {
            raised = BringWindowToTop(window);
        }

        ExplorerTrace.Log($"activate.request target={DescribeWindow(window)} setForeground={accepted} bringToTop={raised} expected={DescribeWindow(expectedForeground)} actual={DescribeWindow(ForegroundWindow())} targetMinimized={IsIconic(window)} expectedInput={input} lastInput={LastInput()}");
    }

    public static void OpenWindow()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var start = new ProcessStartInfo(path);
        start.UseShellExecute = true;
        using var process = Process.Start(start);
    }

    public static bool NewTab(nint activeTab)
    {
        var sent = SendCommand(activeTab, NewTabCommand, 0);
        return sent;
    }

    public static bool SelectTabByIndex(ExplorerWindowState snapshot, int index, uint input)
    {
        if (index < 1 || index > snapshot.Tabs.Length || !IsCurrentWindow(snapshot) || !IsExplorerWindow(snapshot.Handle)
            || ActiveTab(snapshot.Handle) != snapshot.ActiveTab
            || !snapshot.Tabs.Select(tab => tab.Handle).ToHashSet().SetEquals(TabHandles(snapshot.Handle)) || LastInput() != input)
        {
            return false;
        }

        var sent = SendCommand(snapshot.Handle, SelectTabCommand, index);
        return sent;
    }

    public static bool CloseTab(ExplorerWindowState snapshot, nint tab, uint input)
    {
        if (snapshot.Tabs.Length < 2 || !IsCurrentWindow(snapshot) || ActiveTab(snapshot.Handle) != tab
            || !snapshot.Tabs.Select(item => item.Handle).ToHashSet().SetEquals(TabHandles(snapshot.Handle)) || LastInput() != input)
        {
            return false;
        }

        var sent = SendCommand(tab, CloseTabCommand, 1);
        return sent;
    }

    public static bool CloseWindow(ExplorerWindowState snapshot, uint input)
    {
        if (!IsCurrentWindow(snapshot) || !IsExplorerWindow(snapshot.Handle) || snapshot.Tabs.Length != 1
            || !snapshot.Tabs.Select(tab => tab.Handle).ToHashSet().SetEquals(TabHandles(snapshot.Handle)) || LastInput() != input)
        {
            return false;
        }

        var sent = PostMessage(snapshot.Handle, CloseMessage, 0, 0);
        return sent;
    }

    private static bool IsCurrentWindow(ExplorerWindowState snapshot)
    {
        var threadId = GetWindowThreadProcessId(snapshot.Handle, out uint processId);
        var current = threadId == snapshot.ThreadId && processId == snapshot.ProcessId;
        return current;
    }

    public static uint? LastInput()
    {
        // A held mouse button may mean a drag, even when subsequent input is only movement.
        if (GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0 || GetAsyncKeyState(0x04) < 0
            || GetAsyncKeyState(0x05) < 0 || GetAsyncKeyState(0x06) < 0)
        {
            return null;
        }

        var info = new LastInputInfo();
        info.Size = (uint)Marshal.SizeOf<LastInputInfo>();
        if (GetLastInputInfo(ref info))
        {
            var input = ExplorerInput.Shared.Observe(info.Tick);
            return input;
        }

        return null;
    }

    public ExplorerWindowState? ReadWindow(nint window)
    {
        if (!IsExplorerWindow(window))
        {
            return null;
        }

        var threadId = GetWindowThreadProcessId(window, out uint processId);
        var handles = TabHandles(window);
        if (threadId == 0 || handles.Length == 0)
        {
            return null;
        }

        var active = ActiveTab(window);
        if (active == 0)
        {
            return null;
        }

        var tabs = new List<ExplorerTabState>();
        VisitBrowsers(window, handles, (handle, browser) =>
        {
            if (!tabs.Exists(tab => tab.Handle == handle))
            {
                var tab = ReadTab(handle, browser);
                tabs.Add(tab);
            }
            return false;
        });

        if (tabs.Count != handles.Length || !handles.ToHashSet().SetEquals(TabHandles(window)) || ActiveTab(window) != active)
        {
            return null;
        }

        var state = new ExplorerWindowState(window, processId, threadId, active, tabs.ToArray());
        return state;
    }

    public bool Navigate(nint window, nint tab, string path, uint input)
    {
        if (ActiveTab(window) != tab || !ExplorerMergePolicy.IsSupportedPath(path))
        {
            return false;
        }

        var navigated = VisitBrowsers(window, new[] { tab }, (handle, browser) =>
        {
            object location = path;
            if (path.StartsWith("::", StringComparison.Ordinal))
            {
                var pidl = ResolveShellLocation(path);
                if (pidl is null)
                {
                    return false;
                }

                location = pidl;
            }

            if (ActiveTab(window) != handle || LastInput() != input)
            {
                return false;
            }

            dynamic automation = browser;
            automation.Navigate2(location);
            return true;
        });
        return navigated;
    }

    public string? ReadDesktopFolder(DesktopFolderClick.DesktopClick click)
    {
        var clickedName = DesktopFolderClick.ReadItemName(click);
        if (string.IsNullOrEmpty(clickedName))
        {
            ExplorerTrace.Log("desktop.resolve no MSAA desktop item at click");
            return null;
        }

        object? windows = null;
        object? desktop = null;
        object? document = null;
        object? selection = null;
        object? item = null;
        object? link = null;
        try
        {
            windows = OpenShellWindows();
            if (windows is null)
            {
                return null;
            }

            desktop = ((dynamic)windows).Item(8u);
            document = ((dynamic)desktop!).Document;
            selection = ((dynamic)document!).SelectedItems();
            dynamic selected = selection!;
            var selectionCount = (int)selected.Count;
            if (selectionCount != 1)
            {
                ExplorerTrace.Log($"desktop.resolve Shell selection count={selectionCount}");
                return null;
            }

            item = selected.Item(0);
            dynamic selectedItem = item!;
            string itemName = selectedItem.Name;
            if (!clickedName.Equals(itemName, StringComparison.Ordinal))
            {
                ExplorerTrace.Log($"desktop.resolve selection mismatch hit=\"{clickedName}\" selected=\"{itemName}\"");
                return null;
            }

            string path;
            if ((bool)selectedItem.IsLink)
            {
                link = selectedItem.GetLink;
                path = ((dynamic)link!).Path;
            }
            else
            {
                path = selectedItem.Path;
            }

            if (!ExplorerMergePolicy.IsSupportedPath(path)
                || (!path.StartsWith("::{", StringComparison.Ordinal) && !Directory.Exists(path)))
            {
                ExplorerTrace.Log($"desktop.resolve unsupported folder path=\"{path}\"");
                return null;
            }

            return path;
        }
        catch (Exception exception) when (exception is COMException or RuntimeBinderException or InvalidCastException)
        {
            ExplorerTrace.Log($"desktop.resolve exception={exception.GetType().Name} hresult=0x{exception.HResult:X8} message=\"{exception.Message}\"");
            return null;
        }
        finally
        {
            Release(link);
            Release(item);
            Release(selection);
            Release(document);
            Release(desktop);
            Release(windows);
        }
    }

    public void Dispose()
    {
        Release(_shell);
        _shell = null;
    }

    private object? OpenShellWindows()
    {
        if (_shell is null)
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
            {
                return null;
            }

            _shell = Activator.CreateInstance(type);
        }

        if (_shell is null)
        {
            return null;
        }

        dynamic shell = _shell;
        object? windows = shell.Windows();
        return windows;
    }

    private bool VisitBrowsers(nint window, nint[] handles, Func<nint, object, bool> visit)
    {
        object? windows = null;
        try
        {
            windows = OpenShellWindows();
            if (windows is null)
            {
                return false;
            }

            dynamic collection = windows!;
            int count = collection.Count;
            for (int index = 0; index < count; index++)
            {
                object? browser = null;
                try
                {
                    browser = collection.Item(index);
                    if (browser is null)
                    {
                        continue;
                    }

                    dynamic automation = browser;
                    nint owner = (nint)(long)automation.HWND;
                    if (owner != window)
                    {
                        continue;
                    }

                    var tab = BrowserTab(browser);
                    if (tab != 0 && handles.Contains(tab) && visit(tab, browser))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is COMException or RuntimeBinderException or InvalidCastException)
                {
                    // Explorer may be loading the tab or disconnect its automation object.
                }
                finally
                {
                    Release(browser);
                }
            }
        }
        catch (Exception exception) when (exception is COMException or RuntimeBinderException)
        {
            Release(_shell);
            _shell = null;
        }
        finally
        {
            Release(windows);
        }

        return false;
    }

    private static ExplorerTabState ReadTab(nint handle, object browser)
    {
        dynamic automation = browser;
        if (automation.Busy || (int)automation.ReadyState != 4)
        {
            return new ExplorerTabState(handle, "", true);
        }

        object? document = null;
        object? folder = null;
        object? self = null;
        object? selection = null;
        try
        {
            document = automation.Document;
            dynamic view = document!;
            folder = view.Folder;
            self = ((dynamic)folder!).Self;
            string path = ((dynamic)self!).Path;
            if (!ExplorerMergePolicy.IsSupportedPath(path))
            {
                path = "";
            }

            selection = view.SelectedItems();
            int count = ((dynamic)selection!).Count;
            string name = automation.LocationName;
            var state = new ExplorerTabState(handle, path, count != 0, name);
            return state;
        }
        finally
        {
            Release(selection);
            Release(self);
            Release(folder);
            Release(document);
        }
    }

    private static nint BrowserTab(object browser)
    {
        if (browser is not IShellServiceProvider provider)
        {
            return 0;
        }

        int result = provider.QueryService(in ShellBrowserId, in ShellBrowserId, out nint shellBrowser);
        if (result < 0 || shellBrowser == 0)
        {
            return 0;
        }

        try
        {
            var table = Marshal.ReadIntPtr(shellBrowser);
            var slot = Marshal.ReadIntPtr(table, 3 * IntPtr.Size);
            var getWindow = Marshal.GetDelegateForFunctionPointer<GetBrowserWindow>(slot);
            if (getWindow(shellBrowser, out nint window) >= 0)
            {
                return window;
            }

            return 0;
        }
        finally
        {
            Marshal.Release(shellBrowser);
        }
    }

    private static byte[]? ResolveShellLocation(string path)
    {
        int result = SHParseDisplayName(path, 0, out nint list, 0, out _);
        if (result < 0 || list == 0)
        {
            return null;
        }

        try
        {
            var size = checked((int)ILGetSize(list));
            if (size <= 0)
            {
                return null;
            }

            var bytes = new byte[size];
            Marshal.Copy(list, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeCoTaskMem(list);
        }
    }

    private static nint[] TabHandles(nint window)
    {
        var tabs = new List<nint>();
        EnumChildWindows(window, (child, _) =>
        {
            if (HasClass(child, ExplorerTabClass))
            {
                tabs.Add(child);
            }
            return true;
        }, 0);
        return tabs.ToArray();
    }

    public static bool HasClass(nint window, string name)
    {
        var buffer = _classNameBuffer ??= new char[ClassNameCapacity];
        var length = GetClassName(window, buffer, buffer.Length);
        var matches = length > 0 && buffer.AsSpan(0, length).SequenceEqual(name.AsSpan());
        return matches;
    }

    private static bool SendCommand(nint window, uint command, nint argument)
    {
        var sent = window != 0 && SendMessageTimeout(window, CommandMessage, command, argument, 2, 200, out _) != 0;
        return sent;
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        [PreserveSig]
        int QueryService(in Guid service, in Guid interfaceId, out nint value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Tick;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBrowserWindow(nint shellBrowser, out nint window);

    private delegate bool EnumerateWindow(nint window, nint state);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumerateWindow callback, nint state);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint window, EnumerateWindow callback, nint state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint previous, string className, string? name);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, [Out] char[] className, int capacity);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flag);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint command, nint argument, uint flags, uint timeout, out nuint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(nint window, uint message, nuint parameter, nint argument);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint list, uint attributes, out uint resultAttributes);

    [DllImport("shell32.dll")]
    private static extern uint ILGetSize(nint list);
}
