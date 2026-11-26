using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Zea.Tweakr;

public partial class MainWindow : Window
{
    internal event Action<FeatureSettings>? SettingsSaved;

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
            SqlLogConvert = SqlLogConvertOption.IsChecked == true
        };
        SettingsSaved?.Invoke(settings);
    }

    private void HideSettings(object? sender, RoutedEventArgs args)
    {
        Hide();
    }
}
