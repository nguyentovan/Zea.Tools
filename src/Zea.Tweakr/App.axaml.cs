using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Zea.Explorer;
using Zea.SqlIntegration;

namespace Zea.Tweakr;

public partial class App : Application
{
    private CancellationTokenSource _shutdown = new();
    private readonly SqlSelectionService _sql = new();
    private readonly ExplorerService _explorer = new();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private BackgroundToastWindow? _backgroundToast;
    private WindowsHotkeys? _hotkeys;
    private FeatureSettings _settings = new();
    private readonly StartupRegistration _startup = new();
    private Task _sqlOperation = Task.CompletedTask;
    private Task _explorerOperation = Task.CompletedTask;
    private readonly AppUpdater _updater = AppUpdater.CreateDefault();
    private Task _updateSchedule = Task.CompletedTask;
    private Task _manualUpdate = Task.CompletedTask;
    private string? _notifiedUpdateVersion;
    private bool _exiting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += ReleaseResources;
            ExplorerTrace.Log($"app.start pid={Environment.ProcessId} exe=\"{Environment.ProcessPath}\" assembly=\"{typeof(App).Assembly.Location}\" windows={Environment.OSVersion.Version}");
            SqlTrace.Log($"app.start pid={Environment.ProcessId} exe=\"{Environment.ProcessPath}\"");
            LoadSettings();
            _updater.StatusChanged += DisplayUpdateStatus;
            try
            {
                ApplySettings(_settings);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                ExplorerTrace.Log($"app.start.hotkeyError code={exception.NativeErrorCode} message=\"{exception.Message}\"");
                SqlTrace.Log($"app.start.hotkeyError code={exception.NativeErrorCode}");
                ShowBackgroundToast(exception.Message, NotificationType.Error);
            }

            _updateSchedule = RunUpdateSchedule(_shutdown.Token);
            if (desktop.Args?.Contains("--settings", StringComparer.OrdinalIgnoreCase) == true)
            {
                ShowSettings(this, EventArgs.Empty);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void LoadSettings()
    {
        try
        {
            _settings = FeatureSettings.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowBackgroundToast($"Không thể đọc cấu hình. {exception.Message}", NotificationType.Error);
        }

        try
        {
            _settings = _settings with { StartWithWindows = _startup.IsEnabled() };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowBackgroundToast($"Không thể đọc cài đặt khởi động cùng Windows. {exception.Message}", NotificationType.Error);
        }
    }

    private void ShowSettings(object? sender, EventArgs args)
    {
        if (_exiting)
        {
            return;
        }

        var window = GetWindow();
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private MainWindow GetWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow();
            _window.DisplaySettings(_settings);
            _window.SettingsSaved += SaveSettings;
            _window.CheckForUpdatesRequested += QueueUpdateCheck;
            _window.RestartForUpdateRequested += RestartForUpdate;
            _window.DisplayUpdate(_updater.Status);
        }

        return _window;
    }

    private void SaveSettings(FeatureSettings settings)
    {
        if (_exiting)
        {
            return;
        }

        try
        {
            settings.Save();
            _startup.SetEnabled(settings.StartWithWindows);
            ApplySettings(settings);
            GetWindow().ShowSettingsSaved();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
            or System.Security.SecurityException or InvalidOperationException)
        {
            GetWindow().ShowSettingsSaveError(exception.Message);
        }
    }

    private void ApplySettings(FeatureSettings settings)
    {
        _hotkeys ??= new WindowsHotkeys(QueueSql, QueueExplorer, _explorer.RecordInput);
        _hotkeys.SetEnabled(settings.SqlFormat, settings.SqlLogConvert, settings.ExplorerTab);
        _explorer.SetEnabled(settings.ExplorerTab);
        _settings = settings;
        SqlTrace.Log($"settings.applied format={settings.SqlFormat} convertLog={settings.SqlLogConvert}");
    }

    private void QueueSql(nint editor, bool convertLog)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var enabled = convertLog && _settings.SqlLogConvert || !convertLog && _settings.SqlFormat;
            if (!_exiting && enabled && _sqlOperation.IsCompleted)
            {
                _sqlOperation = RunSql(editor, convertLog);
            }
            else
            {
                SqlTrace.Log($"request.ignored convertLog={convertLog} enabled={enabled} exiting={_exiting} busy={!_sqlOperation.IsCompleted}");
            }
        });
    }

    private void QueueExplorer()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_exiting && _settings.ExplorerTab && _explorerOperation.IsCompleted)
            {
                _explorerOperation = RunExplorer();
            }
        });
    }

    private async Task RunSql(nint editor, bool convertLog)
    {
        SqlTrace.Log($"request.start convertLog={convertLog} formatEnabled={_settings.SqlFormat}");
        try
        {
            await _sql.Execute(editor, convertLog, _settings.SqlFormat, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            SqlTrace.Log("request.canceled");
        }
        catch (Exception exception)
        {
            SqlTrace.Log($"request.failed type={exception.GetType().Name} hresult=0x{exception.HResult:X8}");
            ShowBackgroundToast(exception.Message, NotificationType.Error);
        }
    }

    private async Task RunExplorer()
    {
        try
        {
            await _explorer.OpenTab(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowBackgroundToast(exception.Message, NotificationType.Error);
        }
    }

    private void ShowBackgroundToast(string message, NotificationType type)
    {
        if (_exiting)
        {
            return;
        }

        _backgroundToast?.Close();
        var toast = new BackgroundToastWindow(message, type);
        toast.Closed += (_, _) => _backgroundToast = null;
        _backgroundToast = toast;
        toast.Show();
    }

    private void DisplayUpdateStatus(AppUpdateStatus status)
    {
        Dispatcher.UIThread.Post(() => _window?.DisplayUpdate(status));
    }

    private void CheckForUpdates(object? sender, EventArgs args)
    {
        QueueUpdateCheck();
    }

    private void QueueUpdateCheck()
    {
        if (!_exiting && _manualUpdate.IsCompleted && _updater.Status.CanCheck)
        {
            _manualUpdate = RunManualUpdateCheck(_shutdown.Token);
        }
    }

    private async Task RunManualUpdateCheck(CancellationToken cancellationToken)
    {
        await _updater.CheckAndDownloadAsync(cancellationToken);
        if (!_exiting && !cancellationToken.IsCancellationRequested)
        {
            var status = _updater.Status;
            if (status.Phase == AppUpdatePhase.Ready)
            {
                _notifiedUpdateVersion = status.AvailableVersion;
            }
            if (_window is { IsVisible: true, WindowState: not WindowState.Minimized })
            {
                _window.ShowUpdateResult(status);
            }
            else if (MainWindow.CreateUpdateNotification(status) is { } notification)
            {
                ShowBackgroundToast($"{notification.Title}\n{notification.Message}".TrimEnd(), notification.Type);
            }
        }
    }

    private async Task RunUpdateSchedule(CancellationToken cancellationToken)
    {
        try
        {
            await _updater.CheckAndDownloadAsync(cancellationToken);
            NotifyUpdateReady();
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await _updater.CheckAndDownloadAsync(cancellationToken);
                NotifyUpdateReady();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void NotifyUpdateReady()
    {
        var status = _updater.Status;
        if (_exiting || status.Phase != AppUpdatePhase.Ready || _notifiedUpdateVersion == status.AvailableVersion)
        {
            return;
        }

        _notifiedUpdateVersion = status.AvailableVersion;
        if (_window is { IsVisible: true, WindowState: not WindowState.Minimized })
        {
            _window.ShowUpdateResult(status);
        }
        else
        {
            ShowBackgroundToast($"Đã tải phiên bản {status.AvailableVersion}. Mở Cài đặt để cập nhật và khởi động lại.", NotificationType.Success);
        }
    }

    private async void ExitApp(object? sender, EventArgs args)
    {
        if (_exiting)
        {
            return;
        }

        await StopOperations();
        _desktop?.Shutdown();
    }

    private async void RestartForUpdate()
    {
        if (_exiting || !_updater.Status.CanRestart)
        {
            return;
        }

        _window?.SetUpdateActionsEnabled(false);
        await StopOperations();
        if (_updater.TryApplyAndRestart())
        {
            _desktop?.Shutdown();
            return;
        }

        _shutdown.Dispose();
        _shutdown = new CancellationTokenSource();
        _exiting = false;
        try
        {
            ApplySettings(_settings);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            ShowBackgroundToast(exception.Message, NotificationType.Error);
        }
        _window?.DisplayUpdate(_updater.Status);
        _window?.ShowUpdateError("Không thể áp dụng bản cập nhật. Vui lòng thử lại sau.");
        _updateSchedule = RunUpdateSchedule(_shutdown.Token);
    }

    private async Task StopOperations()
    {
        _exiting = true;
        _backgroundToast?.Close();
        _shutdown.Cancel();
        _hotkeys?.Dispose();
        _hotkeys = null;
        _explorer.SetEnabled(false);
        await Task.WhenAll(_sqlOperation, _explorerOperation, _manualUpdate, _updateSchedule);
    }

    private void ReleaseResources(object? sender, ControlledApplicationLifetimeExitEventArgs args)
    {
        _exiting = true;
        _backgroundToast?.Close();
        _shutdown.Cancel();
        _hotkeys?.Dispose();
        _explorer.Dispose();
        _shutdown.Dispose();
        _updater.StatusChanged -= DisplayUpdateStatus;
        SqlTrace.Log("app.exit");
        SqlTrace.Complete();
        ExplorerTrace.Log("app.exit");
        ExplorerTrace.Complete();
    }
}
