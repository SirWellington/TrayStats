using System.Timers;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using TrayStats.Models;

namespace TrayStats.Services;

public sealed class BluetoothMonitorService : IMonitorService
{
    private readonly System.Timers.Timer _timer;
    private int _isUpdating;

    public BluetoothData Data { get; } = new();
    public event Action? DataUpdated;

    public BluetoothMonitorService()
    {
        _timer = new System.Timers.Timer(15000);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public async void Start()
    {
        try
        {
            await UpdateAsync();
            DataUpdated?.Invoke();
        }
        catch { }
        finally
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _isUpdating, 1, 0) != 0)
            return;

        try
        {
            await UpdateAsync();
            DataUpdated?.Invoke();
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _isUpdating, 0);
        }
    }

    private async Task UpdateAsync()
    {
        var devices = new List<BluetoothDeviceInfo>();

        try
        {
            // Query connected classic Bluetooth devices
            var connectedSelector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(
                BluetoothConnectionStatus.Connected);
            var connectedDevices = await DeviceInformation.FindAllAsync(connectedSelector);

            var connectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dev in connectedDevices)
            {
                if (connectedNames.Contains(dev.Name)) continue;
                connectedNames.Add(dev.Name);

                var deviceType = ClassifyDevice(dev.Name);
                devices.Add(new BluetoothDeviceInfo
                {
                    Name = dev.Name,
                    IsConnected = true,
                    DeviceType = deviceType,
                    Icon = GetDeviceIcon(deviceType),
                    BatteryPercent = null
                });
            }

            // Query connected BLE devices
            var connectedBleSelector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(
                BluetoothConnectionStatus.Connected);
            var connectedBleDevices = await DeviceInformation.FindAllAsync(connectedBleSelector);

            foreach (var dev in connectedBleDevices)
            {
                if (connectedNames.Contains(dev.Name)) continue;
                connectedNames.Add(dev.Name);

                var deviceType = ClassifyDevice(dev.Name);
                devices.Add(new BluetoothDeviceInfo
                {
                    Name = dev.Name,
                    IsConnected = true,
                    DeviceType = deviceType,
                    Icon = GetDeviceIcon(deviceType),
                    BatteryPercent = null
                });
            }

            // Also list paired-but-disconnected devices for reference
            var pairedSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var pairedDevices = await DeviceInformation.FindAllAsync(pairedSelector);

            foreach (var dev in pairedDevices)
            {
                if (connectedNames.Contains(dev.Name)) continue;

                var deviceType = ClassifyDevice(dev.Name);
                devices.Add(new BluetoothDeviceInfo
                {
                    Name = dev.Name,
                    IsConnected = false,
                    DeviceType = deviceType,
                    Icon = GetDeviceIcon(deviceType),
                    BatteryPercent = null
                });
            }
        }
        catch { }

        Data.LatestDevices = devices;
        Data.ConnectedCount = devices.Count(d => d.IsConnected);
    }

    private static string ClassifyDevice(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("audio")
            || lower.Contains("speaker") || lower.Contains("earbuds") || lower.Contains("airpods")
            || lower.Contains("buds") || lower.Contains("jabra") || lower.Contains("sony wh")
            || lower.Contains("beats"))
            return "Audio";
        if (lower.Contains("mouse") || lower.Contains("trackpad") || lower.Contains("trackball")
            || lower.Contains("tk-ms"))
            return "Mouse";
        if (lower.Contains("keyboard") || lower.Contains("keychron") || lower.Contains("hhkb"))
            return "Keyboard";
        if (lower.Contains("controller") || lower.Contains("gamepad") || lower.Contains("xbox")
            || lower.Contains("dualsense") || lower.Contains("dualshock"))
            return "Controller";
        if (lower.Contains("phone") || lower.Contains("iphone") || lower.Contains("galaxy")
            || lower.Contains("pixel"))
            return "Phone";
        return "Other";
    }

    private static string GetDeviceIcon(string deviceType) => deviceType switch
    {
        "Audio" => "\uE7F5",
        "Mouse" => "\uE962",
        "Keyboard" => "\uE765",
        "Controller" => "\uE7FC",
        "Phone" => "\uE8EA",
        _ => "\uE702"
    };

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
