using System.Runtime.InteropServices;

namespace Zea.Explorer;

internal static class ExplorerAutomation
{
    private static readonly Guid AutomationClass = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid AutomationInterface = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    public static bool SelectUniqueTab(nint window, string name, int expectedCount, uint input)
    {
        if (name.Length == 0 || ExplorerShell.LastInput() != input)
        {
            return false;
        }

        nint automation = 0;
        nint root = 0;
        nint condition = 0;
        nint items = 0;
        nint match = 0;
        nint pattern = 0;
        try
        {
            if (CoCreateInstance(in AutomationClass, 0, 1, in AutomationInterface, out automation) < 0 || automation == 0
                || GetMethod<ElementFromHandle>(automation, 6)(automation, window, out root) < 0 || root == 0
                || GetMethod<CreatePropertyCondition>(automation, 23)(automation, 30003, 50019, out condition) < 0 || condition == 0
                || GetMethod<FindAll>(root, 6)(root, 4, condition, out items) < 0 || items == 0
                || GetMethod<GetCount>(items, 3)(items, out int count) < 0 || count != expectedCount)
            {
                return false;
            }

            for (int index = 0; index < count; index++)
            {
                nint item = 0;
                nint currentName = 0;
                try
                {
                    if (GetMethod<GetElement>(items, 4)(items, index, out item) < 0 || item == 0
                        || GetMethod<GetName>(item, 23)(item, out currentName) < 0 || currentName == 0)
                    {
                        return false;
                    }

                    var itemName = Marshal.PtrToStringBSTR(currentName);
                    if (!string.Equals(itemName, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (match != 0)
                    {
                        return false;
                    }

                    match = item;
                    item = 0;
                }
                finally
                {
                    if (currentName != 0)
                    {
                        Marshal.FreeBSTR(currentName);
                    }
                    Release(item);
                }
            }

            if (match == 0 || GetMethod<GetPattern>(match, 16)(match, 10010, out pattern) < 0 || pattern == 0
                || ExplorerShell.LastInput() != input || !ExplorerShell.IsExplorerWindow(window))
            {
                return false;
            }

            var selected = GetMethod<Select>(pattern, 3)(pattern) >= 0;
            return selected;
        }
        finally
        {
            Release(pattern);
            Release(match);
            Release(items);
            Release(condition);
            Release(root);
            Release(automation);
        }
    }

    private static T GetMethod<T>(nint pointer, int slot) where T : Delegate
    {
        var table = Marshal.ReadIntPtr(pointer);
        var entry = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
        var method = Marshal.GetDelegateForFunctionPointer<T>(entry);
        return method;
    }

    private static void Release(nint pointer)
    {
        if (pointer != 0)
        {
            Marshal.Release(pointer);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ElementFromHandle(nint automation, nint window, out nint element);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreatePropertyCondition(nint automation, int property,
        [MarshalAs(UnmanagedType.Struct)] object value, out nint condition);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FindAll(nint element, int scope, nint condition, out nint items);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCount(nint items, out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetElement(nint items, int index, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetName(nint element, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPattern(nint element, int id, out nint pattern);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Select(nint pattern);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);
}
