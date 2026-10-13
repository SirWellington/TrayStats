using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrayStats.Models;
using TrayStats.Services;

namespace TrayStats.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private const int MaxDataPoints = 60;

    private readonly HardwareContext _hwContext;
    private readonly CpuMonitorService _cpuService;
    private readonly GpuMonitorService _gpuService;
    private readonly RamMonitorService _ramService;
    private readonly BatteryMonitorService _batteryService;
    private readonly NetworkMonitorService _netService;
    private readonly DiskMonitorService _diskService;
    private readonly WeatherService _weatherService;
    private readonly ProcessMonitorService _processService;
    private readonly BluetoothMonitorService _bluetoothService;
    private readonly UptimeMonitorService _uptimeService;
    private readonly Dispatcher _dispatcher;

    public HardwareContext HardwareContext => _hwContext;

    // Sparkline data
    public List<double> CpuValues { get; } = new();
    public List<double> RamValues { get; } = new();
    public List<double> NetDownValues { get; } = new();
    public List<double> NetUpValues { get; } = new();
    public List<double> BatteryValues { get; } = new();

    // Multi-GPU support
    public ObservableCollection<GpuDisplayModel> Gpus { get; } = new();
    private int _selectedTrayGpuIndex = 0;
    public int SelectedTrayGpuIndex
    {
        get => _selectedTrayGpuIndex;
        set
        {
            if (_selectedTrayGpuIndex == value) return;
            _selectedTrayGpuIndex = value;
            OnPropertyChanged();
        }
    }

    // Expose model objects
    public CpuData Cpu => _cpuService.Data;
    public GpuData Gpu => _gpuService.Data;
    public RamData Ram => _ramService.Data;
    public BatteryData Battery => _batteryService.Data;
    public NetData Net => _netService.Data;
    public DiskData Disk => _diskService.Data;
    public WeatherData Weather => _weatherService.Data;
    public ProcessData Processes => _processService.Data;
    public BluetoothData Bluetooth => _bluetoothService.Data;
    public UptimeData Uptime => _uptimeService.Data;

    // UI-thread collections for cross-thread-safe binding
    public ObservableCollection<DailyForecast> ForecastItems { get; } = new();
    public ObservableCollection<ProcessInfo> TopProcesses { get; } = new();
    public ObservableCollection<NetApplicationData> TopNetworkApplications { get; } = new();
    public ObservableCollection<BluetoothDeviceInfo> BluetoothDevices { get; } = new();

    // Section visibility
    public SectionVisibility Sections { get; } = new();

    // Detail panel visibility
    [ObservableProperty] private bool _isWeatherDetailVisible;
    [ObservableProperty] private bool _isCpuDetailVisible;
    [ObservableProperty] private bool _isRamDetailVisible;
    [ObservableProperty] private bool _isDiskDetailVisible;
    [ObservableProperty] private bool _isBatteryDetailVisible;
    [ObservableProperty] private bool _isNetDetailVisible;
    [ObservableProperty] private bool _isProcessesDetailVisible;
    [ObservableProperty] private bool _isBluetoothDetailVisible;
    [ObservableProperty] private bool _isUptimeDetailVisible;

    // Formatted summary strings
    [ObservableProperty] private string _cpuSummary = "0%";
    [ObservableProperty] private string _gpuSummary = "0%";
    [ObservableProperty] private string _ramSummary = "0 / 0 GB";
    [ObservableProperty] private string _diskSummary = "0%";
    [ObservableProperty] private string _batterySummary = "--";
    [ObservableProperty] private string _netSummary = "-- / --";

    private const int BackgroundHwInterval = 5000;
    private const int ForegroundHwInterval = 2000;
    private const int BackgroundDiskInterval = 30000;
    private const int ForegroundDiskInterval = 5000;

    private bool _dashboardActive;
    private bool _telemetryActive;
    private bool _extendedMonitoringActive;

    private bool _needsGpuInBackground;

    /// <summary>
    /// When true, GPU hardware stays enabled even in background mode (e.g. tray icon shows GPU).
    /// When false, GPU handles are released in background to allow NVIDIA Optimus switching.
    /// </summary>
    public bool NeedsGpuInBackground
    {
        get => _needsGpuInBackground;
        set
        {
            _needsGpuInBackground = value;
            ApplyPollingMode();
        }
    }

    public DashboardViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;

        _hwContext = new HardwareContext();
        _cpuService = new CpuMonitorService(_hwContext);
        _gpuService = new GpuMonitorService(_hwContext);
        _ramService = new RamMonitorService(_hwContext);
        _batteryService = new BatteryMonitorService(_hwContext);
        _netService = new NetworkMonitorService();
        _diskService = new DiskMonitorService(_hwContext);
        _weatherService = new WeatherService();
        _processService = new ProcessMonitorService();
        _bluetoothService = new BluetoothMonitorService();
        _uptimeService = new UptimeMonitorService();

        for (int i = 0; i < MaxDataPoints; i++)
        {
            CpuValues.Add(0);
            RamValues.Add(0);
            NetDownValues.Add(0);
            NetUpValues.Add(0);
            BatteryValues.Add(0);
        }

        _cpuService.DataUpdated += OnCpuUpdated;
        _gpuService.DataUpdated += OnGpuUpdated;
        _ramService.DataUpdated += OnRamUpdated;
        _batteryService.DataUpdated += OnBatteryUpdated;
        _netService.DataUpdated += OnNetworkUpdated;
        _diskService.DataUpdated += OnDiskUpdated;
        _weatherService.DataUpdated += OnWeatherUpdated;
        _processService.DataUpdated += OnProcessesUpdated;
        _bluetoothService.DataUpdated += OnBluetoothUpdated;
        _uptimeService.DataUpdated += OnUptimeUpdated;

        // Start in background mode: only HardwareContext (slow), CPU/GPU/RAM/Battery
        // (via HardwareUpdated), Weather, and Disk (slow) run.
        _hwContext.SetInterval(BackgroundHwInterval);
        _hwContext.Start();
        _cpuService.Start();
        _gpuService.Start();
        _ramService.Start();
        _batteryService.Start();
        _weatherService.Start();
        _diskService.SetInterval(BackgroundDiskInterval);
        _diskService.Start();
    }

    public void SetDashboardActive(bool active)
    {
        if (_dashboardActive == active) return;
        _dashboardActive = active;

        ApplyPollingMode();

        if (active)
            Task.Run(_hwContext.ForceUpdate);
    }

    public void SetTelemetryActive(bool active)
    {
        if (_telemetryActive == active) return;
        _telemetryActive = active;
        ApplyPollingMode();
    }

    private void ApplyPollingMode()
    {
        _hwContext.SetInterval(_dashboardActive ? ForegroundHwInterval : BackgroundHwInterval);
        _diskService.SetInterval(_dashboardActive ? ForegroundDiskInterval : BackgroundDiskInterval);
        _hwContext.SetGpuEnabled(_dashboardActive || _telemetryActive || NeedsGpuInBackground);

        bool shouldRunExtendedMonitoring = _dashboardActive || _telemetryActive;
        if (shouldRunExtendedMonitoring && !_extendedMonitoringActive)
        {
            _extendedMonitoringActive = true;
            Task.Run(() =>
            {
                _netService.Start();
                _processService.Start();
                _bluetoothService.Start();
                _uptimeService.Start();
            });
        }
        else if (!shouldRunExtendedMonitoring && _extendedMonitoringActive)
        {
            _extendedMonitoringActive = false;
            _netService.Stop();
            _processService.Stop();
            _bluetoothService.Stop();
            _uptimeService.Stop();
        }
    }

    public MqttTelemetrySnapshot CreateMqttSnapshot()
    {
        var entities = new List<MqttEntityState>();
        var cpu = Cpu;
        var ram = Ram;
        var battery = Battery;
        var network = Net;
        var uptime = Uptime;

        AddNumber(entities, "cpu_load", "CPU Load", cpu.TotalLoad, "%", stateClass: "measurement");
        AddOptionalNumber(entities, "cpu_temperature", "CPU Temperature", cpu.Temperature, "°C", stateClass: "measurement");
        AddOptionalNumber(entities, "cpu_package_power", "CPU Package Power", cpu.PackagePower, "W", "power", "measurement");
        AddOptionalNumber(entities, "cpu_clock", "CPU Clock", cpu.Clock, "MHz", stateClass: "measurement");

        foreach (var core in cpu.Cores)
        {
            var coreId = core.CoreIndex + 1;
            AddNumber(entities, $"cpu_thread_{coreId}_load", $"CPU Thread {coreId} Load", core.Usage, "%", stateClass: "measurement");
            entities.Add(new MqttEntityState(
                $"cpu_thread_{coreId}_parked",
                $"CPU Thread {coreId} Parked",
                core.IsParked ? "ON" : "OFF",
                Component: "binary_sensor",
                Icon: "mdi:cpu-64-bit"));
        }

        var gpuSnapshots = _gpuService.GetTelemetrySnapshot();
        for (int index = 0; index < gpuSnapshots.Count; index++)
        {
            var gpu = gpuSnapshots[index];
            var gpuId = $"gpu_{MqttDeviceIdentity.Slugify(gpu.Name)}_{index + 1}";
            var gpuName = $"GPU {index + 1} {gpu.Name}";
            AddNumber(entities, $"{gpuId}_load", $"{gpuName} Load", gpu.CoreLoad, "%", stateClass: "measurement");
            AddOptionalNumber(entities, $"{gpuId}_temperature", $"{gpuName} Temperature", gpu.Temperature, "°C", stateClass: "measurement");
            AddOptionalNumber(entities, $"{gpuId}_power", $"{gpuName} Power", gpu.Power, "W", "power", "measurement");
            AddOptionalNumber(entities, $"{gpuId}_core_clock", $"{gpuName} Core Clock", gpu.CoreClock, "MHz", stateClass: "measurement");
            AddOptionalNumber(entities, $"{gpuId}_memory_clock", $"{gpuName} Memory Clock", gpu.MemoryClock, "MHz", stateClass: "measurement");
            AddOptionalNumber(entities, $"{gpuId}_vram_used", $"{gpuName} VRAM Used", gpu.MemoryUsed, "MB", "data_size", "measurement");
            AddOptionalNumber(entities, $"{gpuId}_vram_total", $"{gpuName} VRAM Total", gpu.MemoryTotal, "MB", "data_size", "measurement");
            AddOptionalNumber(entities, $"{gpuId}_vram_load", $"{gpuName} VRAM Load", gpu.MemoryLoad, "%", stateClass: "measurement");
            AddOptionalNumber(entities, $"{gpuId}_fan_speed", $"{gpuName} Fan Speed", gpu.FanSpeed, "rpm", stateClass: "measurement");
            AddOptionalNumber(entities, $"{gpuId}_fan_percent", $"{gpuName} Fan Speed Percent", gpu.FanPercent, "%", stateClass: "measurement");
        }

        AddNumber(entities, "memory_load", "Memory Load", ram.Load, "%", stateClass: "measurement");
        AddNumber(entities, "memory_used", "Memory Used", ram.UsedGb, "GB", "data_size", "measurement");
        AddNumber(entities, "memory_available", "Memory Available", ram.AvailableGb, "GB", "data_size", "measurement");
        AddNumber(entities, "memory_total", "Memory Total", ram.TotalGb, "GB", "data_size", "measurement");

        var drives = _diskService.GetDrivesSnapshot();
        AddNumber(entities, "disk_total_usage", "Disk Total Usage", Disk.TotalUsagePercent, "%", stateClass: "measurement");
        for (int index = 0; index < drives.Count; index++)
        {
            var drive = drives[index];
            var driveId = $"drive_{MqttDeviceIdentity.Slugify(drive.Name)}_{index + 1}";
            var driveName = string.IsNullOrWhiteSpace(drive.Label) ? drive.Name : drive.Label;
            AddNumber(entities, $"{driveId}_total", $"{driveName} Total", drive.TotalGb, "GB", "data_size", "measurement");
            AddNumber(entities, $"{driveId}_used", $"{driveName} Used", drive.UsedGb, "GB", "data_size", "measurement");
            AddNumber(entities, $"{driveId}_free", $"{driveName} Free", drive.FreeGb, "GB", "data_size", "measurement");
            AddNumber(entities, $"{driveId}_usage", $"{driveName} Usage", drive.UsagePercent, "%", stateClass: "measurement");
                AddOptionalNumber(entities, $"{driveId}_temperature", $"{driveName} Temperature", drive.Temperature, "°C", stateClass: "measurement");
            AddOptionalNumber(entities, $"{driveId}_read_rate", $"{driveName} Read Rate", drive.ReadRate, "MB/s", "data_rate", "measurement");
            AddOptionalNumber(entities, $"{driveId}_write_rate", $"{driveName} Write Rate", drive.WriteRate, "MB/s", "data_rate", "measurement");
        }

        AddNumber(entities, "network_download_rate", "Network Download Rate", network.DownloadBytesPerSec, "B/s", "data_rate", "measurement");
        AddNumber(entities, "network_upload_rate", "Network Upload Rate", network.UploadBytesPerSec, "B/s", "data_rate", "measurement");
        AddNumber(entities, "network_downloaded_session", "Network Downloaded This Session", network.TotalDownloaded, "B", "data_size");
        AddNumber(entities, "network_uploaded_session", "Network Uploaded This Session", network.TotalUploaded, "B", "data_size");

        var processes = TopProcesses.Select(process => new Dictionary<string, object?>
        {
            ["name"] = process.Name,
            ["cpu_percent"] = process.CpuPercent,
            ["memory_mb"] = process.MemoryMb,
            ["memory_percent"] = process.MemoryPercent,
            ["instances"] = process.InstanceCount
        }).ToArray();
        entities.Add(new MqttEntityState(
            "top_process",
            "Top CPU Process",
            processes.FirstOrDefault() is { } topProcess ? (string)topProcess["name"]! : "None",
            Icon: "mdi:application-cog",
            Attributes: new Dictionary<string, object?> { ["processes"] = processes }));

        var networkApplications = TopNetworkApplications.Select(application => new Dictionary<string, object?>
        {
            ["name"] = application.Name,
            ["download_bytes_per_second"] = application.DownloadBytesPerSec,
            ["upload_bytes_per_second"] = application.UploadBytesPerSec
        }).ToArray();
        entities.Add(new MqttEntityState(
            "top_network_applications",
            "Top Network Applications",
            networkApplications.Length.ToString(CultureInfo.InvariantCulture),
            Unit: "apps",
            Icon: "mdi:network",
            Attributes: new Dictionary<string, object?> { ["applications"] = networkApplications }));

        AddNumber(entities, "uptime", "Uptime Seconds", Math.Max(0, (DateTime.Now - uptime.BootTime).TotalSeconds), "s", "duration", "measurement");
        entities.Add(new MqttEntityState("uptime_display", "Uptime", uptime.Uptime, Icon: "mdi:timer-outline"));
        entities.Add(new MqttEntityState(
            "boot_time",
            "Last Boot",
            uptime.BootTime == default ? null : uptime.BootTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DeviceClass: "timestamp",
            Icon: "mdi:clock-start"));
        entities.Add(new MqttEntityState("operating_system", "Operating System", uptime.OsVersion, Icon: "mdi:microsoft-windows"));
        entities.Add(new MqttEntityState("logged_in_user", "Logged-in User", uptime.UserName, Icon: "mdi:account"));

        if (battery.HasBattery)
        {
            AddNumber(entities, "battery_level", "Battery Level", battery.ChargeLevel, "%", "battery", "measurement");
            AddOptionalNumber(entities, "battery_voltage", "Battery Voltage", battery.Voltage, "V", "voltage", "measurement");
            AddOptionalNumber(entities, "battery_charge_rate", "Battery Charge or Drain Rate", battery.ChargeDischargeRate, "W", "power", "measurement");
            AddOptionalNumber(entities, "battery_health", "Battery Health", battery.BatteryHealth, "%", stateClass: "measurement");
            AddOptionalNumber(entities, "battery_designed_capacity", "Battery Design Capacity", battery.DesignedCapacity, "mWh", stateClass: "measurement");
            AddOptionalNumber(entities, "battery_full_charge_capacity", "Battery Full Charge Capacity", battery.FullChargeCapacity, "mWh", stateClass: "measurement");
            AddOptionalNumber(entities, "battery_cycle_count", "Battery Cycle Count", battery.CycleCount, "cycles", stateClass: "measurement");
            AddOptionalNumber(entities, "battery_estimated_adapter_power", "Estimated Adapter Power", battery.AdapterPower, "W", "power", "measurement");
            entities.Add(new MqttEntityState("battery_status", "Battery Status", battery.StatusText, Icon: "mdi:battery"));
            entities.Add(new MqttEntityState("battery_time_remaining", "Battery Time Remaining", battery.TimeRemaining, Icon: "mdi:battery-clock"));
        }

        var bluetoothDevices = BluetoothDevices.Select(device => new Dictionary<string, object?>
        {
            ["name"] = device.Name,
            ["connected"] = device.IsConnected,
            ["type"] = device.DeviceType,
            ["battery_percent"] = device.BatteryPercent
        }).ToArray();
        AddNumber(entities, "bluetooth_connected_count", "Connected Bluetooth Devices", Bluetooth.ConnectedCount, "devices", stateClass: "measurement");
        entities.Add(new MqttEntityState(
            "bluetooth_paired_devices",
            "Bluetooth Paired Devices",
            bluetoothDevices.Length.ToString(CultureInfo.InvariantCulture),
            Unit: "devices",
            Icon: "mdi:bluetooth",
            Attributes: new Dictionary<string, object?> { ["devices"] = bluetoothDevices }));

        return new MqttTelemetrySnapshot(Environment.MachineName, entities);
    }

    private static void AddNumber(
        ICollection<MqttEntityState> entities,
        string id,
        string name,
        double value,
        string? unit = null,
        string? deviceClass = null,
        string? stateClass = null)
    {
        entities.Add(new MqttEntityState(
            id,
            name,
            value.ToString("0.##", CultureInfo.InvariantCulture),
            Unit: unit,
            DeviceClass: deviceClass,
            StateClass: stateClass));
    }

    private static void AddOptionalNumber(
        ICollection<MqttEntityState> entities,
        string id,
        string name,
        double value,
        string? unit = null,
        string? deviceClass = null,
        string? stateClass = null)
    {
        entities.Add(new MqttEntityState(
            id,
            name,
            value > 0 ? value.ToString("0.##", CultureInfo.InvariantCulture) : null,
            Unit: unit,
            DeviceClass: deviceClass,
            StateClass: stateClass));
    }

    private void OnCpuUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            PushValue(CpuValues, Cpu.TotalLoad);
            CpuSummary = $"{Cpu.TotalLoad:F0}%";
            OnPropertyChanged(nameof(Cpu));
            if (_dashboardActive) InvalidateCharts?.Invoke();
        });
    }

    private void OnGpuUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            var allGpus = _gpuService.GetAllGpuReferences();

            // Sync display models with actual hardware GPUs
            var existingKeys = new HashSet<string>(allGpus.Select(g => g.Name));
            var toRemove = Gpus.Where(m => !existingKeys.Contains(m.Data.Name)).ToList();
            foreach (var m in toRemove)
                Gpus.Remove(m);

            // Add or update GPU display models
            string[] colors = { "#FF00BFFF", "#FF4CAF50", "#FFFF8C00", "#FFE91E63", "#FF9C27B0" };
            for (int i = 0; i < allGpus.Count; i++)
            {
                var gpuData = allGpus[i];
                var displayModel = Gpus.FirstOrDefault(m => ReferenceEquals(m.Data, gpuData));

                if (displayModel == null)
                {
                    displayModel = new GpuDisplayModel(gpuData, i, colors[i % colors.Length]);
                    Gpus.Add(displayModel);
                }

                displayModel.PushValue(gpuData.CoreLoad);
            }

            // Clamp selected tray GPU index
            if (SelectedTrayGpuIndex >= Gpus.Count && Gpus.Count > 0)
                SelectedTrayGpuIndex = Gpus.Count - 1;

            // Build summary string for all GPUs
            var parts = new List<string>();
            foreach (var m in Gpus)
                parts.Add($"{m.DisplayName}: {m.Data.CoreLoad:F0}%");
            GpuSummary = string.Join(" | ", parts);

            OnPropertyChanged(nameof(Gpu));
            if (_dashboardActive) InvalidateCharts?.Invoke();
        });
    }

    private void OnRamUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            PushValue(RamValues, Ram.Load);
            RamSummary = $"{Ram.UsedGb:F1} / {Ram.TotalGb:F1} GB";
            OnPropertyChanged(nameof(Ram));
            if (_dashboardActive) InvalidateCharts?.Invoke();
        });
    }

    private void OnBatteryUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            PushValue(BatteryValues, Battery.ChargeLevel);
            BatterySummary = $"{Battery.ChargeLevel:F0}%";

            if (Battery.IsPluggedIn && Battery.ChargeDischargeRate > 0)
                Battery.AdapterPower = Battery.ChargeDischargeRate + Cpu.PackagePower + Gpu.Power;
            else
                Battery.AdapterPower = 0;

            OnPropertyChanged(nameof(Battery));
            if (_dashboardActive) InvalidateCharts?.Invoke();
        });
    }

    private void OnNetworkUpdated()
    {
        var applications = Net.LatestApplications;
        _dispatcher.BeginInvoke(() =>
        {
            TopNetworkApplications.Clear();
            foreach (var application in applications)
                TopNetworkApplications.Add(application);
            PushValue(NetDownValues, Net.DownloadBytesPerSec / 1024.0);
            PushValue(NetUpValues, Net.UploadBytesPerSec / 1024.0);
            NetSummary = $"{Net.DownloadFormatted} / {Net.UploadFormatted}";
            OnPropertyChanged(nameof(Net));
            if (_dashboardActive) InvalidateCharts?.Invoke();
        });
    }

    private void OnDiskUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            DiskSummary = $"{Disk.TotalUsagePercent:F0}% used";
            OnPropertyChanged(nameof(Disk));
        });
    }

    private void OnWeatherUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            ForecastItems.Clear();
            foreach (var f in Weather.LatestForecast)
                ForecastItems.Add(f);
            OnPropertyChanged(nameof(Weather));
        });
    }

    private void OnProcessesUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            TopProcesses.Clear();
            foreach (var p in Processes.LatestSnapshot)
                TopProcesses.Add(p);
            OnPropertyChanged(nameof(Processes));
        });
    }

    private void OnBluetoothUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            BluetoothDevices.Clear();
            foreach (var d in Bluetooth.LatestDevices)
                BluetoothDevices.Add(d);
            OnPropertyChanged(nameof(Bluetooth));
        });
    }

    private void OnUptimeUpdated()
    {
        _dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(Uptime));
        });
    }

    public void RefreshWeatherDisplay()
    {
        _dispatcher.BeginInvoke(() =>
        {
            ForecastItems.Clear();
            foreach (var f in Weather.LatestForecast)
                ForecastItems.Add(f);
            OnPropertyChanged(nameof(Weather));
        });
    }

    public event Action? InvalidateCharts;

    private static void PushValue(List<double> values, double newValue)
    {
        if (values.Count >= MaxDataPoints)
            values.RemoveAt(0);
        values.Add(newValue);
    }

    [RelayCommand]
    private void ToggleWeatherDetail() => IsWeatherDetailVisible = !IsWeatherDetailVisible;

    [RelayCommand]
    private void ToggleCpuDetail() => IsCpuDetailVisible = !IsCpuDetailVisible;

    [RelayCommand]
    private void ToggleRamDetail() => IsRamDetailVisible = !IsRamDetailVisible;

    [RelayCommand]
    private void ToggleDiskDetail() => IsDiskDetailVisible = !IsDiskDetailVisible;

    [RelayCommand]
    private void ToggleBatteryDetail() => IsBatteryDetailVisible = !IsBatteryDetailVisible;

    [RelayCommand]
    private void ToggleNetDetail() => IsNetDetailVisible = !IsNetDetailVisible;

    [RelayCommand]
    private void ToggleProcessesDetail() => IsProcessesDetailVisible = !IsProcessesDetailVisible;

    [RelayCommand]
    private void ToggleBluetoothDetail() => IsBluetoothDetailVisible = !IsBluetoothDetailVisible;

    [RelayCommand]
    private void ToggleUptimeDetail() => IsUptimeDetailVisible = !IsUptimeDetailVisible;

    public void Dispose()
    {
        _cpuService.Dispose();
        _gpuService.Dispose();
        _ramService.Dispose();
        _batteryService.Dispose();
        _netService.Dispose();
        _diskService.Dispose();
        _weatherService.Dispose();
        _processService.Dispose();
        _bluetoothService.Dispose();
        _uptimeService.Dispose();
        _hwContext.Dispose();
    }
}
