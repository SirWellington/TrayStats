using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using TrayStats.Helpers;

namespace TrayStats;

public static class Program
{
    private const int MaxRestarts = 3;
    private const int RestartWindowSeconds = 60;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--configure-mqtt-from-env", StringComparer.Ordinal))
        {
            Environment.ExitCode = ConfigureMqttFromEnvironment();
            return;
        }

        PreloadLocalD3d9();
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            RestartSelf(args);
        }
    }

    private static int ConfigureMqttFromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("TRAYSTATS_MQTT_HOST") ?? "127.0.0.1";
        var username = Environment.GetEnvironmentVariable("TRAYSTATS_MQTT_USERNAME") ?? "traystats";
        var password = Environment.GetEnvironmentVariable("TRAYSTATS_MQTT_PASSWORD");
        var deviceName = Environment.GetEnvironmentVariable("TRAYSTATS_MQTT_DEVICE_NAME");
        var portText = Environment.GetEnvironmentVariable("TRAYSTATS_MQTT_PORT") ?? "1883";

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrEmpty(password)
            || !int.TryParse(portText, out var port)
            || port is < 1 or > 65535)
        {
            return 2;
        }

        var settings = SettingsStore.Load();
        settings.MqttEnabled = true;
        settings.MqttDeviceName = string.IsNullOrWhiteSpace(deviceName) ? Environment.MachineName : deviceName.Trim();
        settings.MqttHost = host.Trim();
        settings.MqttPort = port;
        settings.MqttUsername = username.Trim();
        settings.MqttPassword = password;
        SettingsStore.Save(settings);

        var saved = SettingsStore.Load();
        return saved.MqttEnabled
            && saved.MqttHost == settings.MqttHost
            && saved.MqttPort == settings.MqttPort
            && saved.MqttUsername == settings.MqttUsername
            && saved.MqttPassword == password
            ? 0
            : 3;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogCrash(ex);

        if (e.IsTerminating)
            RestartSelf([]);
    }

    private static void PreloadLocalD3d9()
    {
        try
        {
            var localDll = Path.Combine(AppContext.BaseDirectory, "d3d9.dll");
            if (File.Exists(localDll))
                LoadLibraryW(localDll);
        }
        catch { }
    }

    private static void RestartSelf(string[] args)
    {
        try
        {
            var countFile = Path.Combine(AppContext.BaseDirectory, ".restart_count");
            int count = 0;

            if (File.Exists(countFile))
            {
                var parts = File.ReadAllText(countFile).Split('|');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int prev)
                    && long.TryParse(parts[1], out long ticks)
                    && (DateTime.UtcNow - new DateTime(ticks)).TotalSeconds < RestartWindowSeconds)
                {
                    count = prev;
                }
            }

            count++;
            File.WriteAllText(countFile, $"{count}|{DateTime.UtcNow.Ticks}");

            if (count > MaxRestarts)
                return;

            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false
                });
            }
        }
        catch { }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(logPath, entry);
        }
        catch { }
    }
}
