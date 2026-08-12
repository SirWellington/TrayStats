using System.Collections.ObjectModel;
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
            if (!_dashboardActive)
                _hwContext.SetGpuEnabled(value);
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

        if (active)
        {
            _hwContext.SetGpuEnabled(true);
            _hwContext.SetInterval(ForegroundHwInterval);
            _diskService.SetInterval(ForegroundDiskInterval);

            Task.Run(() =>
            {
                _netService.Start();
                _processService.Start();
                _bluetoothService.Start();
                _uptimeService.Start();
                _hwContext.ForceUpdate();
            });
        }
        else
        {
            _hwContext.SetInterval(BackgroundHwInterval);

            if (!NeedsGpuInBackground)
                _hwContext.SetGpuEnabled(false);

            _netService.Stop();
            _processService.Stop();
            _bluetoothService.Stop();
            _uptimeService.Stop();

            _diskService.SetInterval(BackgroundDiskInterval);
        }
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
            var allGpus = _gpuService.AllGpus;

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
        _dispatcher.BeginInvoke(() =>
        {
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
