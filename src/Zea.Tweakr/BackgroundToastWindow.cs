using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;

namespace Zea.Tweakr;

internal sealed class BackgroundToastWindow : Window
{
    private const double ScreenMargin = 12;
    private readonly WindowNotificationManager _notifications;
    private readonly Notification _notification;

    public BackgroundToastWindow(string message, NotificationType type)
    {
        Title = "Zea.Tweakr";
        Width = 400;
        Height = 200;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        _notifications = new WindowNotificationManager
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 1,
            ClipToBounds = true
        };
        _notification = new Notification(Title, message, type, TimeSpan.FromSeconds(5),
            onClose: () =>
            {
                if (IsVisible)
                {
                    Close();
                }
            });
        Content = _notifications;

        if (Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea;
            var right = (int)Math.Ceiling((Width + ScreenMargin) * screen.Scaling);
            var bottom = (int)Math.Ceiling((Height + ScreenMargin) * screen.Scaling);
            Position = new PixelPoint(Math.Max(area.X, area.Right - right), Math.Max(area.Y, area.Bottom - bottom));
        }
    }

    protected override void OnOpened(EventArgs args)
    {
        base.OnOpened(args);
        _notifications.ApplyTemplate();
        _notifications.Show(_notification);
    }
}
