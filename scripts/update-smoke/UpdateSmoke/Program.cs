using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack;
using Zea.Tweakr;

namespace Zea.Tweakr.UpdateSmoke;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static SmokeConfiguration? configuration;

    [STAThread]
    private static int Main(string[] args)
    {
        VelopackApp.Build().Run();

        string? configurationPath = Environment.GetEnvironmentVariable("ZEA_TWEAKR_UPDATE_SMOKE_CONFIG");
        if (string.IsNullOrWhiteSpace(configurationPath))
            return 2;

        try
        {
            configuration = JsonSerializer.Deserialize<SmokeConfiguration>(File.ReadAllText(configurationPath), JsonOptions)
                ?? throw new InvalidDataException("The smoke configuration is empty.");
            string mutexSuffix = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(configuration.PortableRoot)))[..16];
            using var mutex = new Mutex(false, @"Local\Zea.Tweakr.UpdateSmoke." + mutexSuffix);
            Require(mutex.WaitOne(0), "The restarted process must acquire the singleton mutex.");
            try
            {
                RunAsync(configuration).GetAwaiter().GetResult();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
            return 0;
        }
        catch (Exception exception)
        {
            if (configuration is not null)
            {
                WriteEvent("failed", new { exception = exception.ToString() });
                WriteResult(new { passed = false, processId = Environment.ProcessId, error = exception.ToString() });
            }
            return 1;
        }
    }

    private static async Task RunAsync(SmokeConfiguration config)
    {
        var manager = CreateManager(config.FeedNext);
        string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        WriteEvent("started", new
        {
            processId = Environment.ProcessId,
            assemblyVersion,
            packageVersion = manager.CurrentVersion?.ToString(),
            manager.IsInstalled,
            manager.IsPortable,
            baseDirectory = AppContext.BaseDirectory
        });
        Require(manager.IsInstalled, "An extracted release must be recognized as a managed application.");
        Require(manager.IsPortable, "The ZIP must be recognized as a portable application.");
        Require(manager.AppId == AppUpdater.PackageId, "The portable package must use the production update identity.");
        Require(!IsBelow(config.SentinelPath, config.PortableRoot), "The sentinel settings must be outside the package directory.");
        VerifySentinel(config);

        if (File.Exists(config.RestartMarkerPath))
        {
            int firstProcessId = int.Parse(File.ReadAllText(config.RestartMarkerPath));
            Require(firstProcessId != Environment.ProcessId, "Velopack must restart the application in a new process.");
            Require(manager.CurrentVersion?.ToString() == "0.0.2", "The restarted package version must be 0.0.2.");
            Require(assemblyVersion == "0.0.2", "The restarted executable must contain the 0.0.2 assembly.");
            Require(manager.UpdatePendingRestart is null, "The restarted app must not retain an unapplied update.");
            var finalUpdater = Observe(new AppUpdater(manager), "after-restart");
            await finalUpdater.CheckAndDownloadAsync(CancellationToken.None);
            Require(finalUpdater.Status.Phase == AppUpdatePhase.UpToDate, "The newest release must report no further update.");
            VerifySentinel(config);
            WriteEvent("passed", new { firstProcessId, processId = Environment.ProcessId, assemblyVersion });
            WriteResult(new
            {
                passed = true,
                firstProcessId,
                restartedProcessId = Environment.ProcessId,
                currentVersion = manager.CurrentVersion?.ToString(),
                assemblyVersion,
                manager.IsInstalled,
                manager.IsPortable,
                settingsSha256 = SentinelHash(config),
                checks = new[] { "portable-layout", "no-update", "unavailable-network", "corrupt-download", "download-newer-version", "graceful-apply", "restart-new-assembly", "settings-preserved" }
            });
            return;
        }

        Require(manager.CurrentVersion?.ToString() == "0.0.1", "The initial package version must be 0.0.1.");
        Require(assemblyVersion == "0.0.1", "The initial executable must contain the 0.0.1 assembly.");

        var currentUpdater = Observe(new AppUpdater(CreateManager(config.FeedCurrent)), "no-update");
        await currentUpdater.CheckAndDownloadAsync(CancellationToken.None);
        Require(currentUpdater.Status.Phase == AppUpdatePhase.UpToDate, "The current-version feed must report no update.");
        Require(!currentUpdater.TryApplyAndRestart(), "No update must not schedule an application restart.");

        var offlineUpdater = Observe(new AppUpdater(CreateManager(config.UnavailableUrl)), "unavailable-network");
        await offlineUpdater.CheckAndDownloadAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Require(offlineUpdater.Status.Phase == AppUpdatePhase.Error, "An unavailable feed must report an update failure.");
        Require(!string.IsNullOrWhiteSpace(offlineUpdater.Status.Error), "An unavailable feed must report an error message.");
        Require(!offlineUpdater.TryApplyAndRestart(), "An unavailable feed must not schedule an application restart.");
        VerifySentinel(config);

        var corruptManager = CreateManager(config.FeedCorrupt);
        var corruptUpdater = Observe(new AppUpdater(corruptManager), "corrupt-download");
        await corruptUpdater.CheckAndDownloadAsync(CancellationToken.None);
        Require(corruptUpdater.Status.Phase == AppUpdatePhase.Error, "A corrupted package must be rejected.");
        Require(!string.IsNullOrWhiteSpace(corruptUpdater.Status.Error), "A rejected package must report an error message.");
        Require(corruptManager.UpdatePendingRestart is null, "A corrupted package must not become a pending update.");
        Require(!corruptUpdater.TryApplyAndRestart(), "A corrupted package must not schedule an application restart.");
        VerifySentinel(config);

        var update = await manager.CheckForUpdatesAsync();
        Require(update?.TargetFullRelease.Version.ToString() == "0.0.2", "The local feed must offer the newer full package.");
        var updater = Observe(new AppUpdater(manager), "download-newer-version");
        await updater.CheckAndDownloadAsync(CancellationToken.None);
        Require(updater.Status.Phase == AppUpdatePhase.Ready, "A verified newer package must be ready for restart.");
        Require(manager.UpdatePendingRestart?.Version.ToString() == "0.0.2", "The verified package must be a pending 0.0.2 update.");
        Require(manager.CurrentVersion?.ToString() == "0.0.1", "Downloading must leave the running version unchanged.");
        VerifySentinel(config);
        File.WriteAllText(config.RestartMarkerPath, Environment.ProcessId.ToString());
        Require(updater.TryApplyAndRestart(), "The production updater must schedule the update and restart.");
        WriteEvent("graceful-exit-requested", new { processId = Environment.ProcessId, currentVersion = manager.CurrentVersion?.ToString() });
    }

    private static UpdateManager CreateManager(string source) => new(source, new UpdateOptions { ExplicitChannel = "stable" });

    private static AppUpdater Observe(AppUpdater updater, string operation)
    {
        updater.StatusChanged += status => WriteEvent("status", new
        {
            operation,
            phase = status.Phase.ToString(),
            status.CurrentVersion,
            status.AvailableVersion,
            status.Progress,
            status.Error
        });
        return updater;
    }

    private static bool IsBelow(string path, string parent) => Path.GetFullPath(path)
        .StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string SentinelHash(SmokeConfiguration config) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(config.SentinelPath)));

    private static void VerifySentinel(SmokeConfiguration config) => Require(
        SentinelHash(config).Equals(config.SentinelSha256, StringComparison.OrdinalIgnoreCase),
        "Updating must preserve the original settings bytes outside the package directory.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void WriteEvent(string name, object detail)
    {
        string line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, name, detail });
        File.AppendAllText(configuration!.EventPath, line + Environment.NewLine);
    }

    private static void WriteResult(object result)
    {
        string temporaryPath = configuration!.ResultPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, configuration.ResultPath, true);
    }

    private sealed record SmokeConfiguration(
        string PortableRoot,
        string FeedCurrent,
        string FeedNext,
        string FeedCorrupt,
        string UnavailableUrl,
        string SentinelPath,
        string SentinelSha256,
        string RestartMarkerPath,
        string EventPath,
        string ResultPath);
}
