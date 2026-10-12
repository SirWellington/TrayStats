using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrayStats.Helpers;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

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
    public ThemeMode Theme { get; set; } = ThemeMode.System;

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

    public bool MqttEnabled { get; set; }
    public string MqttDeviceName { get; set; } = Environment.MachineName;
    public string MqttHost { get; set; } = "127.0.0.1";
    public int MqttPort { get; set; } = 1883;
    public string MqttUsername { get; set; } = string.Empty;
    public string MqttPasswordProtected { get; set; } = string.Empty;
    public string MqttDeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public int MqttDiscoverySchemaVersion { get; set; }
    public string[] MqttKnownEntities { get; set; } = [];

    [JsonIgnore]
    public string MqttPassword { get; set; } = string.Empty;
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
            var settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            settings.MqttPassword = Unprotect(settings.MqttPasswordProtected);
            if (string.IsNullOrWhiteSpace(settings.MqttDeviceName))
                settings.MqttDeviceName = Environment.MachineName;
            if (string.IsNullOrWhiteSpace(settings.MqttDeviceId))
                settings.MqttDeviceId = Guid.NewGuid().ToString("N");
            return settings;
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
            settings.MqttPasswordProtected = Protect(settings.MqttPassword);
            var json = JsonSerializer.Serialize(settings);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Saving is best-effort; never let it break the app.
        }
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        try
        {
            var clearBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(value),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
