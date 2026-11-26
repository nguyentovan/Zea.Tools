using System.Text.Json;
using Microsoft.Win32;
using Velopack;
using Velopack.Locators;

namespace Zea.Tweakr;

internal static class StartupChecks
{
    private static void Main()
    {
        var root = @"C:\Portable tools\Zea.Tweakr";
        var locator = new TestVelopackLocator(AppUpdater.PackageId, "0.1.0", Path.Combine(root, "packages"),
            Path.Combine(root, "current"), root, Path.Combine(root, "Update.exe"));
        VelopackApp.Build().SetLocator(locator).Run();
        var command = $"\"{Path.Combine(root, "Zea.Tweakr.exe")}\"";
        Require(StartupRegistration.CreateCommand(locator, @"C:\Portable tools\Zea.Tweakr\current\Zea.Tweakr.exe") == command,
            "Portable startup must quote the stable root launcher and have no settings argument.");
        var unmanaged = new TestVelopackLocator("Other.App", "0.1.0", Path.Combine(root, "packages"));
        Require(StartupRegistration.CreateCommand(unmanaged, @"C:\Raw app\Zea.Tweakr.exe") == "\"C:\\Raw app\\Zea.Tweakr.exe\"",
            "An unmanaged build must quote its own apphost path.");
        foreach (var path in new[] { @"C:\Program Files\dotnet\dotnet.exe", @"C:\Raw app\Zea.Tweakr.dll", null })
        {
            try
            {
                StartupRegistration.CreateCommand(unmanaged, path);
                throw new Exception("Startup must reject a missing apphost or the dotnet host.");
            }
            catch (InvalidOperationException)
            {
            }
        }
        Console.WriteLine("PASS portable and raw startup commands");

        var json = JsonSerializer.Serialize(new FeatureSettings { StartWithWindows = true, ExplorerTab = false });
        Require(!json.Contains(nameof(FeatureSettings.StartWithWindows), StringComparison.Ordinal),
            "Windows startup state must not be stored in settings JSON.");
        var settings = JsonSerializer.Deserialize<FeatureSettings>("{\"StartWithWindows\":true,\"ExplorerTab\":false,\"SqlFormat\":false,\"SqlLogConvert\":true}")!;
        Require(!settings.StartWithWindows && !settings.ExplorerTab && !settings.SqlFormat && settings.SqlLogConvert,
            "Old JSON must not enable startup or change the existing feature flags.");
        Console.WriteLine("PASS startup state stays out of settings JSON");

        var subKeyPath = @"Software\Zea.Tweakr\StartupChecks\" + Guid.NewGuid().ToString("N");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
            key.SetValue("Other.App", "keep me", RegistryValueKind.String);
            var startup = new StartupRegistration(subKeyPath);
            Require(!startup.IsEnabled(), "Startup must be off when the app has no Run value.");
            startup.SetEnabled(false);
            startup.SetEnabled(true);
            startup.SetEnabled(true);
            Require(startup.IsEnabled() && Equals(key.GetValue("Zea.Tweakr"), command),
                "Repeated enable must register the same quoted launcher.");
            Require(key.GetValueKind("Zea.Tweakr") == RegistryValueKind.String,
                "Startup must write a Windows Run command string.");
            startup.SetEnabled(false);
            startup.SetEnabled(false);
            Require(!startup.IsEnabled() && key.GetValue("Zea.Tweakr") is null && Equals(key.GetValue("Other.App"), "keep me"),
                "Repeated disable must remove only the app's Run value.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
        Console.WriteLine("PASS isolated registry enable, disable, repeat, and other value preservation");
        Console.WriteLine("All startup behavior checks passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
