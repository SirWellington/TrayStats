using System.IO;
using System.Text.Json;

namespace TrayStats.Helpers;

/// <summary>
/// The user's persistent settings: which metric the tray icon shows, the icon
/// style, which dashboard sections are visible, and related toggles.
/// </summary>
public class Settings
{
    public TrayMetric TrayMetric { get; set; } = TrayMetric.CPU;
    public int TrayGpuIndex { get; set; }
    public IconStyle IconStyle { get; set; } = IconStyle.MiniChart;
    public bool KeepVisible { get; set; }
    public bool SidebarMode { get; set; }

    public bool ShowWeather { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowDisk { get; set; } = true;
    public bool ShowBattery { get; set; } = true;
    public bool ShowNet { get; set; } = true;
    public bool ShowProcesses { get; set; } = true;
    public bool ShowBluetooth { get; set; } = true;
    public bool ShowUptime { get; set; } = true;
    public bool UseFahrenheit { get; set; }
}

/// <summary>
/// Persists user settings to %AppData%\TrayStats\settings.json so selections
/// survive app and system restarts.
/// </summary>
public static class SettingsStore
{
    private static string StorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrayStats");

    private static string FilePath => Path.Combine(StorageDirectory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Settings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public static void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            var json = JsonSerializer.Serialize(settings);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Saving is best-effort; never let it break the app.
        }
    }
}
