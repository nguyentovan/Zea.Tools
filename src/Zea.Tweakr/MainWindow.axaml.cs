using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Zea.Tweakr;

public partial class MainWindow : Window
{
    internal event Action<FeatureSettings>? SettingsSaved;
    internal event Action? CheckForUpdatesRequested;
    internal event Action? RestartForUpdateRequested;

    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += (_, change) =>
        {
            if ((change.Property == IsVisibleProperty && !IsVisible)
                || (change.Property == WindowStateProperty && WindowState == WindowState.Minimized))
            {
                DismissSettingsToast();
            }
        };
    }

    internal void DisplaySettings(FeatureSettings settings)
    {
        ExplorerTabOption.IsChecked = settings.ExplorerTab;
        SqlFormatOption.IsChecked = settings.SqlFormat;
        SqlLogConvertOption.IsChecked = settings.SqlLogConvert;
        StartWithWindowsOption.IsChecked = settings.StartWithWindows;
    }

    internal void ShowSettingsSaved()
    {
        var notification = new Notification("Đã lưu cài đặt.", string.Empty, NotificationType.Success, TimeSpan.FromSeconds(3));
        ShowSettingsToast(notification);
    }

    internal void ShowSettingsSaveError(string message)
    {
        var notification = new Notification("Không thể áp dụng cài đặt.", message, NotificationType.Error, TimeSpan.FromSeconds(5));
        ShowSettingsToast(notification);
    }

    internal void ShowUpdateError(string message)
    {
        var notification = new Notification("Chưa thể cập nhật.", message, NotificationType.Error, TimeSpan.FromSeconds(5));
        ShowSettingsToast(notification);
    }

    internal void ShowUpdateResult(AppUpdateStatus status)
    {
        var notification = CreateUpdateNotification(status);
        if (notification is not null)
        {
            ShowSettingsToast(notification);
        }
    }

    internal static Notification? CreateUpdateNotification(AppUpdateStatus status)
    {
        return status.Phase switch
        {
            AppUpdatePhase.UpToDate => new Notification("Bạn đang dùng phiên bản mới nhất.", string.Empty,
                NotificationType.Information, TimeSpan.FromSeconds(3)),
            AppUpdatePhase.Available => new Notification($"Có phiên bản {status.AvailableVersion} mới hơn.",
                "Tải bản ZIP portable từ GitHub Releases để cập nhật.", NotificationType.Information, TimeSpan.FromSeconds(5)),
            AppUpdatePhase.Ready => new Notification($"Đã tải phiên bản {status.AvailableVersion}.",
                "Khởi động lại để áp dụng, hoặc dùng bản mới ở lần mở tiếp theo.", NotificationType.Success, TimeSpan.FromSeconds(5)),
            AppUpdatePhase.Error => new Notification("Chưa thể cập nhật.",
                "Không thể kiểm tra hoặc tải phiên bản mới. Hãy thử lại sau hoặc tải bản ZIP portable từ GitHub Releases.",
                NotificationType.Error, TimeSpan.FromSeconds(5)),
            _ => null
        };
    }

    internal void DisplayUpdate(AppUpdateStatus status)
    {
        Title = $"Zea.Tweakr · {status.CurrentVersion}";
        CheckUpdateButton.Content = status.Phase switch
        {
            AppUpdatePhase.Checking => "Đang kiểm tra…",
            AppUpdatePhase.Downloading => "Đang tải…",
            _ => "Kiểm tra cập nhật"
        };
        CheckUpdateButton.IsEnabled = status.CanCheck;
        RestartUpdateButton.IsVisible = status.CanRestart;
        RestartUpdateButton.IsEnabled = status.CanRestart;
    }

    internal void SetUpdateActionsEnabled(bool enabled)
    {
        CheckUpdateButton.IsEnabled = enabled;
        RestartUpdateButton.IsEnabled = enabled;
    }

    private void ShowSettingsToast(Notification notification)
    {
        DismissSettingsToast();
        SettingsToast.Show(notification);
    }

    private void DismissSettingsToast()
    {
        // Complete the close immediately when hiding the window or replacing a message.
        var cards = SettingsToast.GetVisualDescendants().OfType<NotificationCard>().ToArray();
        foreach (var card in cards)
        {
            card.SetCurrentValue(NotificationCard.IsClosedProperty, true);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs args)
    {
        if (args.CloseReason == WindowCloseReason.WindowClosing)
        {
            args.Cancel = true;
            Hide();
        }

        base.OnClosing(args);
    }

    private void SaveSettings(object? sender, RoutedEventArgs args)
    {
        var settings = new FeatureSettings
        {
            ExplorerTab = ExplorerTabOption.IsChecked == true,
            SqlFormat = SqlFormatOption.IsChecked == true,
            SqlLogConvert = SqlLogConvertOption.IsChecked == true,
            StartWithWindows = StartWithWindowsOption.IsChecked == true
        };
        SettingsSaved?.Invoke(settings);
    }

    private void HideSettings(object? sender, RoutedEventArgs args)
    {
        Hide();
    }

    private void CheckForUpdates(object? sender, RoutedEventArgs args)
    {
        CheckForUpdatesRequested?.Invoke();
    }

    private void RestartForUpdate(object? sender, RoutedEventArgs args)
    {
        RestartForUpdateRequested?.Invoke();
    }
}
