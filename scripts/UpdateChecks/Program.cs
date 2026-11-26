using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Zea.Tweakr;

internal static class UpdateChecks
{
    private static readonly VelopackAsset NextRelease = new()
    {
        PackageId = AppUpdater.PackageId,
        Version = SemanticVersion.Parse("0.0.2"),
        Type = VelopackAssetType.Full,
        FileName = "Zea.Tweakr.App-0.0.2-full.nupkg"
    };

    private static async Task Main()
    {
        VelopackApp.Build().Run();
        await UnmanagedCopiesCanCheck();
        await UnmanagedChecksFindNewerReleases();
        await UnmanagedChecksCanBeRetried();
        await UnmanagedCancellationOwnsTheFeedCheck();
        await OtherPackagesCannotDownload();
        await NoUpdateIsReported();
        await OnlyOneOperationRuns();
        await FailedChecksCanBeRetried();
        await CancellationOwnsTheFeedCheck();
        await DownloadsCanBeCancelled();
        await FailedDownloadsCannotBeApplied();
        await ApplyingCanBeRetried();
        await PendingUpdatesAreReused();
        Console.WriteLine("All updater behavior checks passed.");
    }

    private static async Task UnmanagedCopiesCanCheck()
    {
        var source = new FakeSource();
        var manager = new FakeManager(source) { Managed = false };
        var updater = new AppUpdater(manager, source: source);
        Require(updater.Status.Phase == AppUpdatePhase.Unmanaged && updater.Status.CanCheck && !updater.CanUpdate,
            "Raw binaries must allow checks without permission to install updates.");
        source.OnFeed = () => Task.FromResult(new VelopackAssetFeed
        {
            Assets = [Release(updater.Status.CurrentVersion), Release("0.0.0"), Release("9999.0.0", VelopackAssetType.Delta), Release("9999.0.0", packageId: "Other.App")]
        });
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.UpToDate && updater.Status.CanCheck,
            "Raw checks must ignore current, older, delta, and foreign packages.");
        Require(source.Checks == 1 && source.AppId == AppUpdater.PackageId && source.Channel == AppUpdater.Channel,
            "Raw checks must read the configured package and channel from the update source.");
        Require(manager.Checks == 0 && manager.Downloads == 0 && source.Downloads == 0 && !updater.TryApplyAndRestart(),
            "Raw checks must not invoke managed checks, download packages, or apply updates.");
        Console.WriteLine("PASS raw version checks without downloads");
    }

    private static async Task UnmanagedChecksFindNewerReleases()
    {
        var source = new FakeSource();
        var manager = new FakeManager(source) { Managed = false };
        var applied = 0;
        var updater = new AppUpdater(manager, _ => applied++, source);
        var nextMajor = int.Parse(updater.Status.CurrentVersion.Split('.')[0]) + 1;
        var latestVersion = $"{nextMajor}.0.10";
        source.OnFeed = () => Task.FromResult(new VelopackAssetFeed
        {
            Assets = [Release($"{nextMajor}.0.2"), Release(latestVersion)]
        });
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.Available && updater.Status.AvailableVersion == latestVersion && updater.Status.CanCheck,
            "Raw checks must find the newest semantic version and remain checkable.");
        await updater.CheckAndDownloadAsync();
        Require(source.Checks == 2 && manager.Downloads == 0 && source.Downloads == 0 && !updater.Status.CanRestart
            && !updater.TryApplyAndRestart() && applied == 0, "An available raw update must remain metadata only across repeated checks.");
        Console.WriteLine("PASS raw newer version and repeat check");
    }

    private static async Task UnmanagedChecksCanBeRetried()
    {
        var source = new FakeSource { OnFeed = () => Task.FromException<VelopackAssetFeed>(new IOException("offline")) };
        var manager = new FakeManager(source) { Managed = false };
        var updater = new AppUpdater(manager, source: source);
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.Error && updater.Status.Error == "offline" && updater.Status.CanCheck,
            "A raw feed failure must expose a retryable error.");
        source.OnFeed = () => Task.FromResult(new VelopackAssetFeed { Assets = [] });
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.UpToDate && source.Checks == 2 && manager.Downloads == 0 && source.Downloads == 0,
            "Raw checks must recover after a source failure without downloading.");
        Console.WriteLine("PASS raw failed check and retry");
    }

    private static async Task UnmanagedCancellationOwnsTheFeedCheck()
    {
        var response = new TaskCompletionSource<VelopackAssetFeed>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeSource { OnFeed = () => response.Task };
        var manager = new FakeManager(source) { Managed = false };
        var updater = new AppUpdater(manager, source: source);
        using var cancellation = new CancellationTokenSource();
        var operation = updater.CheckAndDownloadAsync(cancellation.Token);
        await updater.CheckAndDownloadAsync();
        Require(source.Checks == 1 && updater.Status.Phase == AppUpdatePhase.Checking && !updater.Status.CanCheck,
            "Concurrent raw checks must share one active source request.");
        cancellation.Cancel();
        Require(!operation.IsCompleted, "Cancellation must retain ownership of a raw source request that cannot be cancelled.");
        response.SetResult(new VelopackAssetFeed { Assets = [Release("9999.0.0")] });
        await operation;
        Require(updater.Status.Phase == AppUpdatePhase.Unmanaged && updater.Status.CanCheck && manager.Downloads == 0 && source.Downloads == 0,
            "A cancelled raw check must restore its previous state without downloading.");
        await updater.CheckAndDownloadAsync(cancellation.Token);
        Require(source.Checks == 1, "A pre-cancelled raw check must not read its source.");
        source.OnFeed = () => Task.FromResult(new VelopackAssetFeed { Assets = [] });
        await updater.CheckAndDownloadAsync();
        Require(source.Checks == 2 && updater.Status.Phase == AppUpdatePhase.UpToDate && !updater.TryApplyAndRestart(),
            "A raw check must be retryable after cancellation and cannot apply an update.");
        Console.WriteLine("PASS raw cancellation, task ownership, and retry");
    }

    private static async Task OtherPackagesCannotDownload()
    {
        var source = new FakeSource { OnFeed = () => Task.FromResult(new VelopackAssetFeed { Assets = [Release("9999.0.0")] }) };
        var manager = new FakeManager(source) { InstalledPackageId = "Other.App", Pending = NextRelease };
        var updater = new AppUpdater(manager, source: source);
        await updater.CheckAndDownloadAsync();
        Require(!updater.CanUpdate && source.Checks == 1 && manager.Checks == 0 && manager.Downloads == 0 && source.Downloads == 0
            && !updater.TryApplyAndRestart(), "A different installed package must only read the configured release feed.");
        Console.WriteLine("PASS package identity guards download and apply");
    }

    private static async Task NoUpdateIsReported()
    {
        var manager = new FakeManager();
        var updater = new AppUpdater(manager);
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.UpToDate && updater.Status.CurrentVersion == "0.0.1", "The current version must be reported when no update exists.");
        Require(manager.Downloads == 0 && updater.Status.CanCheck, "No package should download when already up to date.");
        Console.WriteLine("PASS current version and no update");
    }

    private static async Task OnlyOneOperationRuns()
    {
        var response = new TaskCompletionSource<UpdateInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new FakeManager { OnCheck = () => response.Task };
        var applied = 0;
        var updater = new AppUpdater(manager, _ => applied++);
        var progress = new List<int>();
        updater.StatusChanged += status =>
        {
            if (status.Phase == AppUpdatePhase.Downloading) { progress.Add(status.Progress); }
        };
        var first = updater.CheckAndDownloadAsync();
        await updater.CheckAndDownloadAsync();
        Require(manager.Checks == 1 && updater.Status.Phase == AppUpdatePhase.Checking, "Concurrent checks must use one feed request.");
        response.SetResult(new UpdateInfo(NextRelease, false));
        await first;
        Require(manager.Downloads == 1 && progress.Contains(42), "One update must download and report progress.");
        Require(updater.Status.Phase == AppUpdatePhase.Ready && updater.Status.AvailableVersion == "0.0.2" && applied == 0, "A download must wait for a requested restart.");
        Require(updater.TryApplyAndRestart() && applied == 1 && updater.Status.Phase == AppUpdatePhase.Applying, "An explicit restart must apply the downloaded release.");
        Require(!updater.TryApplyAndRestart(), "A second restart must not launch a second updater.");
        await updater.CheckAndDownloadAsync();
        Require(manager.Checks == 1, "No new operation may run after applying starts.");
        Console.WriteLine("PASS one operation and explicit restart");
    }

    private static async Task FailedChecksCanBeRetried()
    {
        var manager = new FakeManager { OnCheck = () => Task.FromException<UpdateInfo?>(new IOException("offline")) };
        var updater = new AppUpdater(manager);
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.Error && updater.Status.Error == "offline" && updater.Status.CanCheck, "A failed request must expose a retryable error.");
        manager.OnCheck = () => Task.FromResult<UpdateInfo?>(null);
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.UpToDate && manager.Checks == 2, "The next check must recover from a network failure.");
        Console.WriteLine("PASS failed check and retry");
    }

    private static async Task CancellationOwnsTheFeedCheck()
    {
        var response = new TaskCompletionSource<UpdateInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new FakeManager { OnCheck = () => response.Task };
        var updater = new AppUpdater(manager);
        using var cancellation = new CancellationTokenSource();
        var operation = updater.CheckAndDownloadAsync(cancellation.Token);
        cancellation.Cancel();
        Require(!operation.IsCompleted, "Cancellation must keep ownership of an SDK feed check that cannot be cancelled.");
        response.SetResult(new UpdateInfo(NextRelease, false));
        await operation;
        Require(manager.Downloads == 0 && updater.Status.Phase == AppUpdatePhase.Idle, "A cancelled feed check must not start a download.");
        await updater.CheckAndDownloadAsync(cancellation.Token);
        Require(manager.Checks == 1, "A pre-cancelled request must stay offline.");
        Console.WriteLine("PASS feed cancellation and task ownership");
    }

    private static async Task DownloadsCanBeCancelled()
    {
        var manager = AvailableManager();
        manager.OnDownload = token => Task.Delay(Timeout.Infinite, token);
        var updater = new AppUpdater(manager);
        using var cancellation = new CancellationTokenSource();
        var operation = updater.CheckAndDownloadAsync(cancellation.Token);
        Require(updater.Status.Phase == AppUpdatePhase.Downloading, "An available update must enter download state.");
        cancellation.Cancel();
        await operation;
        Require(updater.Status.Phase == AppUpdatePhase.Idle && !updater.TryApplyAndRestart(), "A cancelled download must not become ready to apply.");
        Console.WriteLine("PASS download cancellation");
    }

    private static async Task FailedDownloadsCannotBeApplied()
    {
        var manager = AvailableManager();
        manager.OnDownload = _ => Task.FromException(new IOException("checksum mismatch"));
        var updater = new AppUpdater(manager);
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.Error && !updater.TryApplyAndRestart(), "An incomplete or invalid package must never be applied.");
        manager.OnDownload = _ => Task.CompletedTask;
        await updater.CheckAndDownloadAsync();
        Require(updater.Status.Phase == AppUpdatePhase.Ready, "A failed download must be retryable.");
        Console.WriteLine("PASS failed download and retry");
    }

    private static async Task ApplyingCanBeRetried()
    {
        var attempts = 0;
        var updater = new AppUpdater(AvailableManager(), _ =>
        {
            if (++attempts == 1) { throw new IOException("updater unavailable"); }
        });
        await updater.CheckAndDownloadAsync();
        Require(!updater.TryApplyAndRestart() && updater.Status.CanRestart && updater.Status.Error == "updater unavailable", "A failed restart must retain the verified package.");
        Require(updater.TryApplyAndRestart() && attempts == 2, "Applying the same verified package must be retryable.");
        Console.WriteLine("PASS failed apply and retry");
    }

    private static async Task PendingUpdatesAreReused()
    {
        var manager = new FakeManager { Pending = NextRelease };
        var updater = new AppUpdater(manager, _ => { });
        Require(updater.Status.CanRestart, "An existing downloaded package must be visible immediately.");
        await updater.CheckAndDownloadAsync();
        Require(manager.Checks == 0 && manager.Downloads == 0 && updater.TryApplyAndRestart(), "A prepared update must not be downloaded again.");
        Console.WriteLine("PASS pending package reuse");
    }

    private static FakeManager AvailableManager() => new() { OnCheck = () => Task.FromResult<UpdateInfo?>(new UpdateInfo(NextRelease, false)) };

    private static VelopackAsset Release(string version, VelopackAssetType type = VelopackAssetType.Full, string packageId = AppUpdater.PackageId) => new()
    {
        PackageId = packageId,
        Version = SemanticVersion.Parse(version),
        Type = type,
        FileName = $"{packageId}-{version}-{type}.nupkg"
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private sealed class FakeManager(IUpdateSource? source = null) : UpdateManager(source ?? new SimpleFileSource(new DirectoryInfo(AppContext.BaseDirectory)))
    {
        internal bool Managed { get; init; } = true;
        internal string InstalledPackageId { get; init; } = AppUpdater.PackageId;
        internal int Checks { get; private set; }
        internal int Downloads { get; private set; }
        internal VelopackAsset? Pending { get; init; }
        internal Func<Task<UpdateInfo?>> OnCheck { get; set; } = () => Task.FromResult<UpdateInfo?>(null);
        internal Func<CancellationToken, Task> OnDownload { get; set; } = _ => Task.CompletedTask;
        public override bool IsInstalled => Managed;
        public override string AppId => InstalledPackageId;
        public override SemanticVersion CurrentVersion => SemanticVersion.Parse("0.0.1");
        public override VelopackAsset? UpdatePendingRestart => Pending;

        public override Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            Checks++;
            return OnCheck();
        }

        public override Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            Downloads++;
            progress?.Invoke(42);
            return OnDownload(cancelToken);
        }
    }

    private sealed class FakeSource : IUpdateSource
    {
        internal int Checks { get; private set; }
        internal int Downloads { get; private set; }
        internal string? AppId { get; private set; }
        internal string? Channel { get; private set; }
        internal Func<Task<VelopackAssetFeed>> OnFeed { get; set; } = () => Task.FromResult(new VelopackAssetFeed { Assets = [] });

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId, VelopackAsset? latestLocalRelease)
        {
            Checks++;
            AppId = appId;
            Channel = channel;
            return OnFeed();
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
        {
            Downloads++;
            throw new InvalidOperationException("Metadata checks must not download release entries.");
        }
    }
}
