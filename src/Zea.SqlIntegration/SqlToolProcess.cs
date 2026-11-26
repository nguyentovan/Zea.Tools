using System.ComponentModel;
using System.Diagnostics;
using System.Text;
#if DEBUG
using System.Text.RegularExpressions;
#endif

namespace Zea.SqlIntegration;

internal static class SqlToolProcess
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static Task<string> Run(string tool, string input, string? dialect, CancellationToken cancellationToken)
    {
        var task = RunCore(tool, input, dialect, preserveTokens: false, validate: false, cancellationToken);
        return task;
    }

    public static Task<string> FormatPreservingTokens(string input, string dialect, CancellationToken cancellationToken)
    {
        var task = RunCore("Zea.SqlFormat", input, dialect, preserveTokens: true, validate: false, cancellationToken);
        return task;
    }

    public static Task<string> FormatSqlSelection(string input, string dialect, CancellationToken cancellationToken)
    {
        var task = RunCore("Zea.SqlFormat", input, dialect, preserveTokens: true, validate: true, cancellationToken);
        return task;
    }

    private static async Task<string> RunCore(string tool, string input, string? dialect, bool preserveTokens, bool validate,
        CancellationToken cancellationToken)
    {
        SqlTrace.Log($"tool.start tool={tool} dialect={dialect ?? "-"}");
        var executable = Path.Combine(AppContext.BaseDirectory, $"{tool}.exe");
        if (!File.Exists(executable))
        {
            SqlTrace.Log($"tool.missing tool={tool} path=\"{executable}\"");
            throw new InvalidOperationException($"Không tìm thấy công cụ {tool}: {executable}");
        }

        var startInfo = new ProcessStartInfo(executable);
        startInfo.WorkingDirectory = Path.GetDirectoryName(executable)!;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardInputEncoding = Utf8;
        startInfo.StandardOutputEncoding = Utf8;
        startInfo.StandardErrorEncoding = Utf8;
        if (dialect is not null)
        {
            startInfo.ArgumentList.Add("--dialect");
            startInfo.ArgumentList.Add(dialect);
            if (preserveTokens)
            {
                startInfo.ArgumentList.Add("--preserve-tokens");
                if (validate)
                {
                    startInfo.ArgumentList.Add("--validate");
                }
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        timeout.Token.ThrowIfCancellationRequested();

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Không thể chạy {tool}.");
        using var stopProcess = timeout.Token.Register(() =>
        {
            StopProcess(process);
        });
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var inputTask = WriteInput(process.StandardInput, input, timeout.Token);
            var exitTask = process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(outputTask, errorTask, inputTask, exitTask);

            var error = (await errorTask).Trim();
            SqlTrace.Log($"tool.exit tool={tool} dialect={dialect ?? "-"} code={process.ExitCode}");
            if (process.ExitCode != 0)
            {
                TraceError(tool, dialect, error);
                if (error.Length != 0)
                {
                    throw new InvalidOperationException(error);
                }

                throw new InvalidOperationException($"{tool} kết thúc với mã lỗi {process.ExitCode}.");
            }

            var output = await outputTask;
            if (!preserveTokens && tool != "Zea.SqlLogConvert")
            {
                output = output.TrimEnd('\r', '\n');
            }

            if (output.Length == 0)
            {
                SqlTrace.Log($"tool.emptyOutput tool={tool}");
                throw new InvalidOperationException($"{tool} không trả về kết quả.");
            }

            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SqlTrace.Log($"tool.timeout tool={tool}");
            throw new TimeoutException($"{tool} mất quá nhiều thời gian để xử lý.");
        }
        finally
        {
            StopProcess(process);
        }
    }

    [Conditional("DEBUG")]
    private static void TraceError(string tool, string? dialect, string error)
    {
#if DEBUG
        var parserPrefix = $"Could not parse {dialect}. ";
        if (tool == "Zea.SqlFormat" && error.StartsWith(parserPrefix, StringComparison.Ordinal))
        {
            // ANTLR diagnostics can include tokens from the selection. Keep only the location.
            var location = Regex.Match(error[parserPrefix.Length..], @"\ALine ([0-9]+), column ([0-9]+):");
            var position = location.Success ? $" line={location.Groups[1].Value} column={location.Groups[2].Value}" : "";
            SqlTrace.Log($"tool.failed tool={tool} kind=parser{position}");
        }
        else if (tool == "Zea.SqlFormat" && error == "Right Way formatting would change the SQL syntax.")
        {
            SqlTrace.Log($"tool.failed tool={tool} kind=syntaxGuard");
        }
#endif
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and Kill.
        }
        catch (Win32Exception)
        {
            // Cancellation and the process exit can race.
        }
    }

    private static async Task WriteInput(StreamWriter writer, string input, CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(input.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            // A tool can exit early. Its stderr and exit code explain the failure.
        }
        finally
        {
            try
            {
                writer.Close();
            }
            catch (IOException)
            {
                // A process that exited early has already closed its end of the pipe.
            }
        }
    }
}
