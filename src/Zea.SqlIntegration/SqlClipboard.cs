using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Zea.SqlIntegration;

internal sealed class SqlClipboard : IDisposable
{
    private const uint UnicodeText = 13;
    private const uint MoveableMemory = 2;
    private readonly List<ClipboardItem> _formats = new();
    private readonly nint _ownerWindow;

    private SqlClipboard(nint ownerWindow)
    {
        _ownerWindow = ownerWindow;
    }

    public uint OriginalSequence { get; private set; }
    public uint? ManagedSequence { get; private set; }

    public static uint CurrentSequence()
    {
        var sequence = GetClipboardSequenceNumber();
        return sequence;
    }

    public static async Task<SqlClipboard> Capture(CancellationToken cancellationToken)
    {
        // EmptyClipboard requires an HWND owned by this process when data will be written.
        var window = CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0, new nint(-3), 0, 0, 0);
        if (window == 0)
        {
            SqlTrace.Log("clipboard.captureFailed reason=ownerWindow");
            throw new InvalidOperationException("Không thể tạo cửa sổ quản lý clipboard.");
        }

        var snapshot = new SqlClipboard(window);
        try
        {
            if (!await snapshot.Open(cancellationToken))
            {
                SqlTrace.Log("clipboard.captureFailed reason=busy");
                throw new InvalidOperationException("Clipboard đang được ứng dụng khác sử dụng. Hãy thử lại.");
            }

            try
            {
                snapshot.SaveFormats();
                snapshot.OriginalSequence = CurrentSequence();
            }
            finally
            {
                CloseClipboard();
            }

            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    public bool TryReadCopy(uint previousSequence, uint editorProcess, out string? text, out uint sequence)
    {
        text = null;
        sequence = previousSequence;
        if (!OpenClipboard(_ownerWindow))
        {
            return false;
        }

        try
        {
            var owner = GetClipboardOwner();
            var beforeRead = CurrentSequence();
            if (owner == 0 || SqlEditor.WindowProcess(owner) != editorProcess)
            {
                if (beforeRead != previousSequence)
                {
                    SqlTrace.Log($"clipboard.changed stage=copy expectedSequence={previousSequence} actualSequence={beforeRead}");
                    throw new InvalidOperationException("Clipboard đã được ứng dụng khác cập nhật.");
                }

                return false;
            }

            // SWT can publish an OLE clipboard object without rendering its text.
            // Read the format before checking the sequence so the owner can render it.
            var handle = GetClipboardData(UnicodeText);
            sequence = CurrentSequence();
            if (sequence == previousSequence)
            {
                return false;
            }

            // Hold the clipboard lock from verifying the owner through tracking the
            // post-render sequence so other applications cannot replace the data.
            ManagedSequence = sequence;
            if (handle == 0 || HasWholeLineFormat())
            {
                return true;
            }

            var address = GlobalLock(handle);
            if (address == 0)
            {
                return true;
            }

            try
            {
                var characters = GlobalSize(handle) / sizeof(char);
                if (characters == 0 || characters > int.MaxValue)
                {
                    return true;
                }

                var value = Marshal.PtrToStringUni(address, (int)characters);
                var terminator = value.IndexOf('\0');
                if (terminator >= 0)
                {
                    text = value[..terminator];
                }

                return true;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool HasWholeLineFormat()
    {
        var wholeLine = RegisterClipboardFormat("VisualStudioEditorOperationsLineCutCopyClipboardTag");
        var legacyWholeLine = RegisterClipboardFormat("MSDEVLineSelect");
        var isWholeLine = wholeLine != 0 && IsClipboardFormatAvailable(wholeLine) ||
            legacyWholeLine != 0 && IsClipboardFormatAvailable(legacyWholeLine);
        return isWholeLine;
    }

    public async Task SetText(string text, uint expectedSequence, CancellationToken cancellationToken)
    {
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var handle = GlobalAlloc(MoveableMemory, (nuint)bytes.Length);
        if (handle == 0)
        {
            throw new InvalidOperationException("Không đủ bộ nhớ để ghi SQL đã xử lý vào clipboard.");
        }

        try
        {
            var address = GlobalLock(handle);
            if (address == 0)
            {
                throw new InvalidOperationException("Không thể ghi SQL đã xử lý vào clipboard.");
            }

            try
            {
                Marshal.Copy(bytes, 0, address, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            if (!await Open(cancellationToken))
            {
                throw new InvalidOperationException("Clipboard đang được ứng dụng khác sử dụng. Hãy thử lại.");
            }

            var emptied = false;
            try
            {
                var sequence = CurrentSequence();
                if (sequence != expectedSequence)
                {
                    SqlTrace.Log($"clipboard.changed stage=write expectedSequence={expectedSequence} actualSequence={sequence}");
                    throw new InvalidOperationException("Clipboard đã thay đổi trong lúc xử lý SQL.");
                }

                if (!EmptyClipboard())
                {
                    throw new InvalidOperationException("Không thể chuẩn bị clipboard để dán SQL.");
                }

                emptied = true;
                if (SetClipboardData(UnicodeText, handle) == 0)
                {
                    SqlTrace.Log("clipboard.writeFailed reason=setData");
                    throw new InvalidOperationException("Không thể đặt SQL đã xử lý vào clipboard.");
                }

                handle = 0;
            }
            finally
            {
                CloseClipboard();
                if (emptied)
                {
                    // Windows can advance the sequence on close. Read the sequence before
                    // checking ownership so a concurrent replacement is never tracked as ours.
                    var sequence = CurrentSequence();
                    if (GetClipboardOwner() == _ownerWindow)
                    {
                        ManagedSequence = sequence;
                        if (handle == 0)
                        {
                            SqlTrace.Log($"clipboard.published sequence={sequence}");
                        }
                    }
                    else
                    {
                        SqlTrace.Log($"clipboard.changed stage=publish actualSequence={sequence} reason=owner");
                    }
                }
            }
        }
        finally
        {
            if (handle != 0)
            {
                GlobalFree(handle);
            }
        }
    }

    public async Task Restore()
    {
        if (ManagedSequence is not uint sequence || CurrentSequence() != sequence)
        {
            return;
        }

        if (!await Open(CancellationToken.None))
        {
            if (CurrentSequence() == sequence)
            {
                throw new InvalidOperationException("Không thể khôi phục clipboard vì ứng dụng khác đang sử dụng.");
            }

            return;
        }

        try
        {
            if (CurrentSequence() != sequence)
            {
                return;
            }

            if (!EmptyClipboard())
            {
                throw new InvalidOperationException("Không thể khôi phục clipboard trước khi xử lý SQL.");
            }

            var restored = true;
            foreach (var item in _formats)
            {
                if (SetClipboardData(item.Format, item.Handle) == 0)
                {
                    restored = false;
                }
                else
                {
                    item.Handle = 0;
                }
            }

            if (!restored)
            {
                throw new InvalidOperationException("Windows không khôi phục được một số định dạng clipboard.");
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Dispose()
    {
        foreach (var item in _formats)
        {
            if (item.Handle == 0)
            {
                continue;
            }

            var medium = new STGMEDIUM();
            medium.tymed = item.Medium;
            medium.unionmember = item.Handle;
            ReleaseStgMedium(ref medium);
        }

        _formats.Clear();
        DestroyWindow(_ownerWindow);
    }

    private void SaveFormats()
    {
        var formatName = new StringBuilder(256);
        var format = EnumClipboardFormats(0);
        while (format != 0)
        {
            var nativeFormat = format;
            var medium = TYMED.TYMED_HGLOBAL;
            // Native and display variants use the same handle type.
            switch (format)
            {
                case 2:
                case 9:
                case 0x82:
                    if (format == 0x82)
                    {
                        nativeFormat = 2;
                    }
                    medium = TYMED.TYMED_GDI;
                    break;
                case 3:
                case 0x83:
                    nativeFormat = 3;
                    medium = TYMED.TYMED_MFPICT;
                    break;
                case 14:
                case 0x8E:
                    nativeFormat = 14;
                    medium = TYMED.TYMED_ENHMF;
                    break;
                case 0x80:
                case >= 0x200 and <= 0x2FF:
                    SqlTrace.Log($"clipboard.captureFailed reason=privateFormat format={format}");
                    throw new InvalidOperationException("Clipboard có định dạng riêng của ứng dụng nguồn. Hãy sao chép văn bản thường trước khi dùng phím tắt SQL.");
            }

            if (format >= 0xC000)
            {
                formatName.Clear();
                GetClipboardFormatName(format, formatName, formatName.Capacity);
                var name = formatName.ToString();
                if (name.Equals("ObjectLink", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("OwnerLink", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Link Source", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Native", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Embed Source", StringComparison.OrdinalIgnoreCase))
                {
                    SqlTrace.Log($"clipboard.captureFailed reason=officeLink format={format}");
                    throw new InvalidOperationException("Clipboard có liên kết tới dữ liệu Office. Hãy sao chép văn bản thường trước khi dùng phím tắt SQL.");
                }
            }

            var source = GetClipboardData(format);
            var duplicate = source == 0 ? 0 : OleDuplicateData(source, (ushort)nativeFormat, MoveableMemory);
            if (duplicate == 0)
            {
                SqlTrace.Log($"clipboard.captureFailed reason=duplicate format={format}");
                throw new InvalidOperationException($"Không thể sao lưu định dạng clipboard {format}. Hãy sao chép văn bản thường rồi thử lại.");
            }

            _formats.Add(new ClipboardItem(format, duplicate, medium));
            format = EnumClipboardFormats(format);
        }

        if (Marshal.GetLastPInvokeError() != 0)
        {
            SqlTrace.Log("clipboard.captureFailed reason=enumeration");
            throw new InvalidOperationException("Không thể xác định các định dạng trong clipboard.");
        }
    }

    private async Task<bool> Open(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OpenClipboard(_ownerWindow))
            {
                return true;
            }

            await Task.Delay(20, cancellationToken);
        }

        return false;
    }

    private sealed class ClipboardItem(uint format, nint handle, TYMED medium)
    {
        public uint Format { get; } = format;
        public nint Handle { get; set; } = handle;
        public TYMED Medium { get; } = medium;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string? title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardOwner();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll")]
    private static extern nint SetClipboardData(uint format, nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string formatName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClipboardFormatName(uint format, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("ole32.dll")]
    private static extern nint OleDuplicateData(nint source, ushort format, uint flags);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(nint memory);
}
