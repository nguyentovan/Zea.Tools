using Microsoft.Win32;
using Velopack.Locators;

namespace Zea.Tweakr;

internal sealed class StartupRegistration(string subKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    private const string ValueName = "Zea.Tweakr";

    internal bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var command = CreateCommand(VelopackLocator.Current, Environment.ProcessPath);
            using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    internal static string CreateCommand(IVelopackLocator locator, string? processPath)
    {
        var executablePath = locator.AppId == AppUpdater.PackageId && !string.IsNullOrWhiteSpace(locator.RootAppDir)
            ? Path.Combine(locator.RootAppDir, "Zea.Tweakr.exe")
            : processPath;
        if (string.IsNullOrWhiteSpace(executablePath)
            || !Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(executablePath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Hãy chạy Zea.Tweakr.exe trước khi bật khởi động cùng Windows.");
        }

        return $"\"{executablePath}\"";
    }
}
