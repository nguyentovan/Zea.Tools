using System.Diagnostics;
#if DEBUG
using System.Threading.Channels;
#endif

namespace Zea.Explorer;

public static class ExplorerTrace
{
#if DEBUG
    private static readonly Channel<string> Entries = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    });

    private static readonly Task Writer = Task.Run(WriteLog);
#endif

    [Conditional("DEBUG")]
    public static void Log(string message)
    {
#if DEBUG
        var entry = $"{DateTimeOffset.Now:O} tick={Environment.TickCount64} {message}";
        Entries.Writer.TryWrite(entry);
#endif
    }

    [Conditional("DEBUG")]
    public static void Complete()
    {
#if DEBUG
        Entries.Writer.TryComplete();
        Writer.Wait(TimeSpan.FromMilliseconds(300));
#endif
    }

#if DEBUG
    private static async Task WriteLog()
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Zea.Tweakr", "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"explorer-debug-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
            await using var writer = new StreamWriter(path);
            writer.AutoFlush = true;
            await foreach (var entry in Entries.Reader.ReadAllAsync())
            {
                Debug.WriteLine(entry, "Zea.Tweakr.Explorer");
                await writer.WriteLineAsync(entry);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not write Explorer trace: {exception.Message}", "Zea.Tweakr.Explorer");
        }
    }
#endif
}
