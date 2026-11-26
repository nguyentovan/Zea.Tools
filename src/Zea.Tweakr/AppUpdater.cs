using System.Reflection;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Zea.Tweakr;

internal enum AppUpdatePhase
{
    Unmanaged,
    Idle,
    Checking,
    Downloading,
    UpToDate,
    Available,
    Ready,
    Applying,
    Error
}

internal sealed record AppUpdateStatus(
    AppUpdatePhase Phase,
    string CurrentVersion,
    string? AvailableVersion = null,
    int Progress = 0,
    string? Error = null)
{
    public bool CanCheck => Phase is AppUpdatePhase.Unmanaged or AppUpdatePhase.Idle or AppUpdatePhase.UpToDate
        or AppUpdatePhase.Available or AppUpdatePhase.Ready or AppUpdatePhase.Error;
    public bool CanRestart => Phase == AppUpdatePhase.Ready;
}

internal sealed class AppUpdater
{
    internal const string PackageId = "Zea.Tweakr.App";
    internal const string RepositoryUrl = "https://github.com/nguyentovan/Zea.Tools";
    internal const string Channel = "stable";
    private static readonly NullVelopackLogger Log = new();
    private readonly UpdateManager? _manager;
    private readonly IUpdateSource? _source;
    private readonly Action<VelopackAsset>? _applyAndRestart;
    private readonly FeedDownloader? _feedDownloader;
    private AppUpdateStatus _status;
    private VelopackAsset? _pending;
    private int _operation;

    internal event Action<AppUpdateStatus>? StatusChanged;
    internal AppUpdateStatus Status => Volatile.Read(ref _status);
    internal bool CanUpdate { get; }

    internal AppUpdater(UpdateManager manager, Action<VelopackAsset>? applyAndRestart = null, IUpdateSource? source = null)
        : this(manager, applyAndRestart, source, null)
    {
    }

    private AppUpdater(UpdateManager manager, Action<VelopackAsset>? applyAndRestart, IUpdateSource? source, FeedDownloader? feedDownloader)
    {
        _manager = manager;
        _source = source;
        _feedDownloader = feedDownloader;
        _applyAndRestart = applyAndRestart ?? (asset => manager.WaitExitThenApplyUpdates(asset, silent: true, restart: true));
        CanUpdate = manager.IsInstalled && manager.AppId == PackageId;
        var version = (CanUpdate ? manager.CurrentVersion?.ToString() : null) ?? DevelopmentVersion();
        _pending = CanUpdate ? manager.UpdatePendingRestart : null;
        _status = new AppUpdateStatus(
            !CanUpdate ? AppUpdatePhase.Unmanaged : _pending is null ? AppUpdatePhase.Idle : AppUpdatePhase.Ready,
            version, _pending?.Version.ToString());
    }

    private AppUpdater(string error)
    {
        _status = new AppUpdateStatus(AppUpdatePhase.Unmanaged, DevelopmentVersion(), Error: error);
    }

    internal static AppUpdater CreateDefault()
    {
        try
        {
            var downloader = new FeedDownloader();
            var source = new GithubSource(RepositoryUrl, accessToken: null, prerelease: false, downloader);
            return new AppUpdater(new UpdateManager(source, new UpdateOptions { ExplicitChannel = Channel }), null, source, downloader);
        }
        catch (Exception exception)
        {
            return new AppUpdater(exception.Message);
        }
    }

    internal async Task CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _operation, 1, 0) != 0)
        {
            return;
        }

        var previousStatus = Status;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_manager is null)
            {
                throw new InvalidOperationException(previousStatus.Error);
            }

            if (CanUpdate)
            {
                _pending ??= _manager.UpdatePendingRestart;
            }
            if (_pending is not null)
            {
                SetStatus(new AppUpdateStatus(AppUpdatePhase.Ready, previousStatus.CurrentVersion, _pending.Version.ToString()));
                return;
            }

            SetStatus(new AppUpdateStatus(AppUpdatePhase.Checking, previousStatus.CurrentVersion));
            if (_feedDownloader is not null)
            {
                _feedDownloader.CancellationToken = cancellationToken;
            }
            // Other injected sources may not support cancellation. Always await their completion.
            if (!CanUpdate)
            {
                var source = _source ?? throw new InvalidOperationException("The update source is unavailable.");
                var feed = await source.GetReleaseFeed(Log, PackageId, Channel, null, null).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var currentVersion = SemanticVersion.Parse(previousStatus.CurrentVersion);
                var release = feed.Assets
                    .Where(asset => asset.PackageId == PackageId && asset.Type == VelopackAssetType.Full && asset.Version > currentVersion)
                    .MaxBy(asset => asset.Version);
                SetStatus(new AppUpdateStatus(release is null ? AppUpdatePhase.UpToDate : AppUpdatePhase.Available,
                    previousStatus.CurrentVersion, release?.Version.ToString()));
                return;
            }

            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (update is null)
            {
                SetStatus(new AppUpdateStatus(AppUpdatePhase.UpToDate, previousStatus.CurrentVersion));
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            SetStatus(new AppUpdateStatus(AppUpdatePhase.Downloading, previousStatus.CurrentVersion, version));
            await _manager.DownloadUpdatesAsync(update, progress =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    SetStatus(new AppUpdateStatus(AppUpdatePhase.Downloading, previousStatus.CurrentVersion, version, Math.Clamp(progress, 0, 100)));
                }
            }, cancellationToken).ConfigureAwait(false);
            _pending = update.TargetFullRelease;
            SetStatus(new AppUpdateStatus(AppUpdatePhase.Ready, previousStatus.CurrentVersion, version, 100));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(previousStatus);
        }
        catch (Exception exception)
        {
            SetStatus(new AppUpdateStatus(AppUpdatePhase.Error, previousStatus.CurrentVersion, Error: exception.Message));
        }
        finally
        {
            if (_feedDownloader is not null)
            {
                _feedDownloader.CancellationToken = default;
            }
            Interlocked.Exchange(ref _operation, 0);
        }
    }

    internal bool TryApplyAndRestart()
    {
        if (!CanUpdate || _pending is null || Interlocked.CompareExchange(ref _operation, 1, 0) != 0)
        {
            return false;
        }

        var ready = Status;
        try
        {
            SetStatus(ready with { Phase = AppUpdatePhase.Applying, Error = null });
            _applyAndRestart!(_pending);
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(ready with { Phase = AppUpdatePhase.Ready, Error = exception.Message });
            Interlocked.Exchange(ref _operation, 0);
            return false;
        }
    }

    private void SetStatus(AppUpdateStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(status);
    }

    private static string DevelopmentVersion()
    {
        var assembly = typeof(AppUpdater).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private sealed class FeedDownloader : HttpClientFileDownloader
    {
        internal CancellationToken CancellationToken { get; set; }

        public override async Task<string> DownloadString(string url, IDictionary<string, string>? headers, double timeout)
        {
            using var client = CreateHttpClient(headers, Math.Min(timeout, 0.5));
            return await client.GetStringAsync(url, CancellationToken).ConfigureAwait(false);
        }

        public override async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers, double timeout)
        {
            using var client = CreateHttpClient(headers, Math.Min(timeout, 0.5));
            return await client.GetByteArrayAsync(url, CancellationToken).ConfigureAwait(false);
        }
    }
}
