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
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SqlSelectionService _sql = new();
    private readonly ExplorerService _explorer = new();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private BackgroundToastWindow? _backgroundToast;
    private WindowsHotkeys? _hotkeys;
    private FeatureSettings _settings = new();
    private Task _sqlOperation = Task.CompletedTask;
    private Task _explorerOperation = Task.CompletedTask;
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
            ApplySettings(settings);
            GetWindow().ShowSettingsSaved();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
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

    private async void ExitApp(object? sender, EventArgs args)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _backgroundToast?.Close();
        _shutdown.Cancel();
        _hotkeys?.Dispose();
        _hotkeys = null;
        _explorer.SetEnabled(false);
        await Task.WhenAll(_sqlOperation, _explorerOperation);
        _desktop?.Shutdown();
    }

    private void ReleaseResources(object? sender, ControlledApplicationLifetimeExitEventArgs args)
    {
        _exiting = true;
        _backgroundToast?.Close();
        _shutdown.Cancel();
        _hotkeys?.Dispose();
        _explorer.Dispose();
        _shutdown.Dispose();
        SqlTrace.Log("app.exit");
        SqlTrace.Complete();
        ExplorerTrace.Log("app.exit");
        ExplorerTrace.Complete();
    }
}
