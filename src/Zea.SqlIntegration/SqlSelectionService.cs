using Avalonia.Threading;

namespace Zea.SqlIntegration;

public sealed class SqlSelectionService
{
    private const ushort CopyKey = 0x43;
    private const ushort PasteKey = 0x56;
    private bool _busy;

    public async Task Execute(nint editor, bool convertLog, bool formatEnabled,
        CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_busy)
        {
            SqlTrace.Log("request.ignored reason=selectionBusy");
            return;
        }

        _busy = true;
        try
        {
            SqlTrace.Log("editor.read");
            var target = SqlEditor.Read(editor, format: !convertLog || formatEnabled);
            SqlTrace.Log($"clipboard.capture dialect={target.Dialect ?? "-"}");
            using var snapshot = await SqlClipboard.Capture(cancellationToken);
            try
            {
                SqlTrace.Log("selection.copy");
                var selection = await CopySelection(target, snapshot, snapshot.OriginalSequence, cancellationToken);
                var sql = selection.Text;
                if (convertLog)
                {
                    sql = await SqlToolProcess.Run("Zea.SqlLogConvert", sql, null, cancellationToken);
                }

                if (target.Dialect is not null)
                {
                    if (convertLog)
                    {
                        sql = await SqlToolProcess.FormatPreservingTokens(sql, target.Dialect, cancellationToken);
                    }
                    else
                    {
                        sql = await SqlToolProcess.FormatSqlSelection(sql, target.Dialect, cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                SqlTrace.Log("selection.verify");
                var current = await CopySelection(target, snapshot, selection.Sequence, cancellationToken);
                if (!string.Equals(current.Text, selection.Text, StringComparison.Ordinal))
                {
                    SqlTrace.Log("editor.selectionChanged stage=verify");
                    throw new InvalidOperationException("Vùng chọn hoặc tab đã thay đổi. SQL chưa được dán.");
                }

                if (!target.IsCurrent())
                {
                    SqlTrace.Log("editor.changed stage=verify");
                    throw new InvalidOperationException("Vùng chọn hoặc tab đã thay đổi. SQL chưa được dán.");
                }

                SqlTrace.Log("clipboard.set");
                await snapshot.SetText(sql, current.Sequence, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                VerifyTarget(target, snapshot.ManagedSequence, "beforePaste",
                    "Tab hoặc clipboard đã thay đổi. SQL chưa được dán.");

                target.SendShortcut(PasteKey);
                SqlTrace.Log("selection.pasteSent");
                // Let the editor consume its queued paste before restoring the user's clipboard.
                await Task.Delay(300, CancellationToken.None);
            }
            finally
            {
                SqlTrace.Log("clipboard.restore");
                await snapshot.Restore();
            }

            SqlTrace.Log("request.completed");
        }
        finally
        {
            _busy = false;
        }
    }

    private static void VerifyTarget(SqlEditor target, uint? expectedSequence, string stage, string errorMessage)
    {
        var editorIsCurrent = target.IsCurrent();
        var actualSequence = SqlClipboard.CurrentSequence();
        if (!editorIsCurrent)
        {
            SqlTrace.Log($"editor.changed stage={stage}");
        }

        if (actualSequence != expectedSequence)
        {
            SqlTrace.Log($"clipboard.changed stage={stage} expectedSequence={expectedSequence} actualSequence={actualSequence}");
        }

        if (!editorIsCurrent || actualSequence != expectedSequence)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static async Task<(string Text, uint Sequence)> CopySelection(SqlEditor target, SqlClipboard snapshot,
        uint previousSequence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        VerifyTarget(target, previousSequence, "beforeCopy",
            "Tab hoặc clipboard đã thay đổi trong lúc xử lý SQL.");

        target.SendShortcut(CopyKey);
        var deadline = Environment.TickCount64 + 1000;
        while (Environment.TickCount64 < deadline)
        {
            // A queued Ctrl+C may still replace the clipboard while the app is shutting down.
            if (snapshot.TryReadCopy(previousSequence, target.ProcessId, out var text, out var sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(text))
                {
                    if (target.IsCurrent())
                    {
                        return (text, sequence);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    SqlTrace.Log("editor.changed stage=afterCopy");
                    throw new InvalidOperationException("Hãy chọn một đoạn SQL hoặc một khối log trong trình soạn thảo.");
                }

                break;
            }

            await Task.Delay(20);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SqlTrace.Log("selection.unavailable");
        throw new InvalidOperationException("Hãy chọn một đoạn SQL hoặc một khối log trong trình soạn thảo.");
    }
}
