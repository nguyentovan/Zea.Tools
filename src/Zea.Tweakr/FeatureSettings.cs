using System.Text;
using System.Text.Json;

namespace Zea.Tweakr;

internal sealed record FeatureSettings
{
    public bool ExplorerTab { get; init; } = true;
    public bool SqlFormat { get; init; } = true;
    public bool SqlLogConvert { get; init; } = true;

    private static string SettingsPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(localAppData, "Zea.Tweakr", "settings.json");
            return path;
        }
    }

    public static FeatureSettings Load()
    {
        var path = SettingsPath;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<FeatureSettings>(json) ?? new FeatureSettings();
            return settings;
        }

        var iniPath = Path.Combine(AppContext.BaseDirectory, "Main.ini");
        if (!File.Exists(iniPath))
        {
            return new FeatureSettings();
        }

        var result = new FeatureSettings();
        var inFeatures = false;
        foreach (var rawLine in File.ReadLines(iniPath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inFeatures = line.Equals("[Features]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var separator = line.IndexOf('=');
            if (!inFeatures || separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var enabled = line[(separator + 1)..].Trim() != "0";
            if (key.Equals("ExplorerTab", StringComparison.OrdinalIgnoreCase))
            {
                result = result with { ExplorerTab = enabled };
            }
            else if (key.Equals("SqlFormat", StringComparison.OrdinalIgnoreCase))
            {
                result = result with { SqlFormat = enabled };
            }
            else if (key.Equals("SqlLogConvert", StringComparison.OrdinalIgnoreCase))
            {
                result = result with { SqlLogConvert = enabled };
            }
        }

        return result;
    }

    public void Save()
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
