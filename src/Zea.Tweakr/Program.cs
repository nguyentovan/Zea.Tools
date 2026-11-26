using Avalonia;
using Velopack;

namespace Zea.Tweakr;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        using var instance = new Mutex(true, @"Local\Zea.Tweakr", out var firstInstance);
        if (firstInstance)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        return builder;
    }
}
