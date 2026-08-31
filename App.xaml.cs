using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using TrayStats.Helpers;
using TrayStats.ViewModels;
using TrayStats.Views;

namespace TrayStats;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TaskbarIcon? _trayIcon;
    private DashboardPopup? _popup;
    private DashboardViewModel? _viewModel;
    private DispatcherTimer? _iconTimer;
    private Settings _settings = new();
    private IconStyle _iconStyle = IconStyle.MiniChart;
    private TrayMetric _trayMetric = TrayMetric.CPU;
    private MenuItem[]? _gpuMenuItems;
    private bool _isExiting;
    private bool _dashboardOpen;
    private bool _keepVisible;
    private bool _sidebarMode;
    private MenuItem? _sidebarMenuItem;
    private DateTime _lastToggle = DateTime.MinValue;
    private IntPtr _currentHIcon = IntPtr.Zero;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogException(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogException(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException(args.Exception);
            args.SetObserved();
        };

        _mutex = new Mutex(true, "TrayStats_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("TrayStats is already running.", "TrayStats",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _viewModel = new DashboardViewModel();

        LoadSettings();

        // Dump all sensors to a log file for diagnostics
        try
        {
            var dump = _viewModel.HardwareContext.DumpAllSensors();
            var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "sensors.log");
            System.IO.File.WriteAllText(logPath, dump);
        }
        catch { }

        _popup = new DashboardPopup
        {
            DataContext = _viewModel,
            KeepVisible = _keepVisible
        };
        _popup.DashboardHidden += () =>
        {
            _dashboardOpen = false;
            _lastToggle = DateTime.UtcNow;
            _viewModel?.SetDashboardActive(false);
        };
        _popup.SidebarModeDisabled += () =>
        {
            // The user closed a docked sidebar, which turned the mode off.
            _sidebarMode = false;
            if (_sidebarMenuItem != null) _sidebarMenuItem.IsChecked = false;
            SaveSettings();
        };

        CreateTrayIcon();

        // Restore a previously docked sidebar.
        if (_settings.SidebarMode)
            SetSidebarMode(true);

        _iconTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _iconTimer.Tick += UpdateTrayIcon;
        _iconTimer.Start();
    }

    private void CreateTrayIcon()
    {
        var icon = IconGenerator.CreateIcon(0, _iconStyle);

        var contextMenu = new ContextMenu();

        var showItem = new MenuItem { Header = "Show Dashboard" };
        showItem.Click += (_, _) => ShowPopup();
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new Separator());

        // Tray metric submenu (GPU items are dynamic)
        var metricMenu = new MenuItem { Header = "Tray Metric" };
        var cpuMetric = new MenuItem { Header = "CPU", IsCheckable = true, IsChecked = _trayMetric == TrayMetric.CPU };
        var ramMetric = new MenuItem { Header = "RAM", IsCheckable = true, IsChecked = _trayMetric == TrayMetric.RAM };

        cpuMetric.Click += (_, _) => SetTrayMetric(TrayMetric.CPU, metricMenu);
        ramMetric.Click += (_, _) => SetTrayMetric(TrayMetric.RAM, metricMenu);

        metricMenu.Items.Add(cpuMetric);
        BuildGpuMenuItems(metricMenu);
        metricMenu.Items.Add(new Separator());
        metricMenu.Items.Add(ramMetric);
        contextMenu.Items.Add(metricMenu);

        // Icon style submenu
        var styleMenu = new MenuItem { Header = "Icon Style" };
        var barItem = new MenuItem { Header = "Bar", IsCheckable = true, IsChecked = _iconStyle == IconStyle.Bar };
        var pctItem = new MenuItem { Header = "Percentage", IsCheckable = true, IsChecked = _iconStyle == IconStyle.Percentage };
        var chartItem = new MenuItem { Header = "Mini Chart", IsCheckable = true, IsChecked = _iconStyle == IconStyle.MiniChart };

        barItem.Click += (_, _) => SetIconStyle(IconStyle.Bar, barItem, pctItem, chartItem);
        pctItem.Click += (_, _) => SetIconStyle(IconStyle.Percentage, barItem, pctItem, chartItem);
        chartItem.Click += (_, _) => SetIconStyle(IconStyle.MiniChart, barItem, pctItem, chartItem);

        styleMenu.Items.Add(barItem);
        styleMenu.Items.Add(pctItem);
        styleMenu.Items.Add(chartItem);
        contextMenu.Items.Add(styleMenu);

        // Sections submenu
        var sectionsMenu = new MenuItem { Header = "Sections" };
        AddSectionToggle(sectionsMenu, "Weather", () => _viewModel!.Sections.ShowWeather, v => _viewModel!.Sections.ShowWeather = v);
        AddSectionToggle(sectionsMenu, "CPU", () => _viewModel!.Sections.ShowCpu, v => _viewModel!.Sections.ShowCpu = v);
        AddSectionToggle(sectionsMenu, "GPU", () => _viewModel!.Sections.ShowGpu, v => _viewModel!.Sections.ShowGpu = v);
        AddSectionToggle(sectionsMenu, "RAM", () => _viewModel!.Sections.ShowRam, v => _viewModel!.Sections.ShowRam = v);
        AddSectionToggle(sectionsMenu, "Disk", () => _viewModel!.Sections.ShowDisk, v => _viewModel!.Sections.ShowDisk = v);
        AddSectionToggle(sectionsMenu, "Battery", () => _viewModel!.Sections.ShowBattery, v => _viewModel!.Sections.ShowBattery = v);
        AddSectionToggle(sectionsMenu, "Network", () => _viewModel!.Sections.ShowNet, v => _viewModel!.Sections.ShowNet = v);
        AddSectionToggle(sectionsMenu, "Processes", () => _viewModel!.Sections.ShowProcesses, v => _viewModel!.Sections.ShowProcesses = v);
        AddSectionToggle(sectionsMenu, "Bluetooth", () => _viewModel!.Sections.ShowBluetooth, v => _viewModel!.Sections.ShowBluetooth = v);
        AddSectionToggle(sectionsMenu, "Uptime", () => _viewModel!.Sections.ShowUptime, v => _viewModel!.Sections.ShowUptime = v);
        contextMenu.Items.Add(sectionsMenu);

        var unitsMenu = new MenuItem { Header = "Units" };
        var fahrenheitItem = new MenuItem
        {
            Header = "Fahrenheit",
            IsCheckable = true,
            IsChecked = _viewModel!.Sections.UseFahrenheit
        };
        fahrenheitItem.Click += (_, _) => SetUseFahrenheit(fahrenheitItem.IsChecked);
        unitsMenu.Items.Add(fahrenheitItem);
        contextMenu.Items.Add(unitsMenu);

        contextMenu.Items.Add(new Separator());

        if (!IsRunningAsAdmin())
        {
            var adminItem = new MenuItem { Header = "Restart as Admin" };
            adminItem.Click += (_, _) => RestartAsAdmin();
            contextMenu.Items.Add(adminItem);
        }

        var startupItem = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = StartupHelper.IsStartupEnabled()
        };
        startupItem.Click += (_, _) => StartupHelper.SetStartup(startupItem.IsChecked);
        contextMenu.Items.Add(startupItem);

        var keepVisibleItem = new MenuItem
        {
            Header = "Keep Visible",
            IsCheckable = true,
            IsChecked = _keepVisible
        };
        keepVisibleItem.Click += (_, _) =>
        {
            _keepVisible = keepVisibleItem.IsChecked;
            if (_popup != null) _popup.KeepVisible = _keepVisible;
            SaveSettings();
        };
        contextMenu.Items.Add(keepVisibleItem);

        var sidebarItem = new MenuItem
        {
            Header = "Sidebar Mode",
            IsCheckable = true,
            IsChecked = _sidebarMode
        };
        sidebarItem.Click += (_, _) => SetSidebarMode(sidebarItem.IsChecked);
        _sidebarMenuItem = sidebarItem;
        contextMenu.Items.Add(sidebarItem);

        contextMenu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApp();
        contextMenu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            Icon = icon,
            ToolTipText = "TrayStats - System Monitor",
            ContextMenu = contextMenu,
            NoLeftClickDelay = true
        };

        _trayIcon.TrayLeftMouseDown += (_, _) => TogglePopup();
        _trayIcon.ForceCreate();
        _currentHIcon = icon.Handle;
    }

    private void SetTrayMetric(TrayMetric metric, MenuItem metricMenu)
    {
        _trayMetric = metric;

        // Uncheck all checkable items in the menu
        foreach (var item in metricMenu.Items.OfType<MenuItem>())
            if (item.IsCheckable && !string.IsNullOrEmpty(item.Header?.ToString()))
                item.IsChecked = false;

        // Check the selected one
        var headers = metricMenu.Items.OfType<MenuItem>().ToDictionary(m => m.Header?.ToString()!, m => m);
        if (metric == TrayMetric.CPU)
        {
            var item = headers.GetValueOrDefault("CPU");
            if (item != null) item.IsChecked = true;
        }
        else if (metric == TrayMetric.RAM)
        {
            var item = headers.GetValueOrDefault("RAM");
            if (item != null) item.IsChecked = true;
        }
        else if (metric == TrayMetric.GPU && _viewModel != null && _viewModel.SelectedTrayGpuIndex < _viewModel.Gpus.Count)
        {
            var gpu = _viewModel.Gpus[_viewModel.SelectedTrayGpuIndex];
            var item = headers.GetValueOrDefault(gpu.DisplayName);
            if (item != null) item.IsChecked = true;
        }

        if (_viewModel != null)
            _viewModel.NeedsGpuInBackground = metric == TrayMetric.GPU;

        SaveSettings();
    }

    private void BuildGpuMenuItems(MenuItem metricMenu)
    {
        // Remove old GPU items
        if (_gpuMenuItems != null)
        {
            foreach (var item in _gpuMenuItems)
                metricMenu.Items.Remove(item);
        }

        var newItems = new List<MenuItem>();

        if (_viewModel?.Gpus != null && _viewModel.Gpus.Count > 0)
        {
            for (int i = 0; i < _viewModel.Gpus.Count; i++)
            {
                var gpu = _viewModel.Gpus[i];
                var index = i; // capture for closure
                var item = new MenuItem
                {
                    Header = gpu.DisplayName,
                    IsCheckable = true,
                };
                bool isSelected = _trayMetric == TrayMetric.GPU && _viewModel.SelectedTrayGpuIndex == index;
                item.IsChecked = isSelected;

                item.Click += (_, _) =>
                {
                    _trayMetric = TrayMetric.GPU;
                    if (_viewModel != null)
                    {
                        _viewModel.SelectedTrayGpuIndex = index;
                        _viewModel.NeedsGpuInBackground = true;
                    }
                    // Update check states
                    foreach (var m in metricMenu.Items.OfType<MenuItem>())
                        if (m.IsCheckable && !string.IsNullOrEmpty(m.Header?.ToString()))
                            m.IsChecked = false;
                    item.IsChecked = true;
                    SaveSettings();
                };

                newItems.Add(item);
            }
        }

        _gpuMenuItems = newItems.ToArray();

        // Insert GPU items after CPU, before separator + RAM
        int insertIndex = 1; // After "CPU" at index 0
        for (int i = 0; i < _gpuMenuItems.Length; i++)
            metricMenu.Items.Insert(insertIndex + i, _gpuMenuItems[i]);
    }

    private void SetIconStyle(IconStyle style, params MenuItem[] items)
    {
        _iconStyle = style;
        foreach (var item in items)
            item.IsChecked = false;

        var selected = style switch
        {
            IconStyle.Bar => items[0],
            IconStyle.Percentage => items[1],
            IconStyle.MiniChart => items[2],
            _ => items[2]
        };
        selected.IsChecked = true;

        SaveSettings();
    }

    private void SetUseFahrenheit(bool useF)
    {
        WeatherTemperatureConverter.UseFahrenheit = useF;
        _viewModel!.Sections.UseFahrenheit = useF;
        _viewModel.RefreshWeatherDisplay();
        SaveSettings();
    }

    private void AddSectionToggle(MenuItem parent, string label, Func<bool> getter, Action<bool> setter)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = true,
            IsChecked = getter()
        };
        item.Click += (_, _) =>
        {
            setter(item.IsChecked);
            SaveSettings();
        };
        parent.Items.Add(item);
    }

    /// <summary>
    /// Restores the user's saved selections (tray metric, icon style, sections, ...)
    /// into the live state.
    /// </summary>
    private void LoadSettings()
    {
        _settings = SettingsStore.Load();
        _iconStyle = _settings.IconStyle;
        _trayMetric = _settings.TrayMetric;
        _keepVisible = _settings.KeepVisible;
        _sidebarMode = _settings.SidebarMode;
        _viewModel!.SelectedTrayGpuIndex = _settings.TrayGpuIndex;
        _viewModel.NeedsGpuInBackground = _trayMetric == TrayMetric.GPU;

        var sections = _viewModel.Sections;
        sections.ShowWeather = _settings.ShowWeather;
        sections.ShowCpu = _settings.ShowCpu;
        sections.ShowGpu = _settings.ShowGpu;
        sections.ShowRam = _settings.ShowRam;
        sections.ShowDisk = _settings.ShowDisk;
        sections.ShowBattery = _settings.ShowBattery;
        sections.ShowNet = _settings.ShowNet;
        sections.ShowProcesses = _settings.ShowProcesses;
        sections.ShowBluetooth = _settings.ShowBluetooth;
        sections.ShowUptime = _settings.ShowUptime;
        sections.UseFahrenheit = _settings.UseFahrenheit;
        WeatherTemperatureConverter.UseFahrenheit = _settings.UseFahrenheit;
    }

    /// <summary>
    /// Captures the current selections and persists them so they survive a restart.
    /// </summary>
    private void SaveSettings()
    {
        _settings.TrayMetric = _trayMetric;
        _settings.TrayGpuIndex = _viewModel?.SelectedTrayGpuIndex ?? 0;
        _settings.IconStyle = _iconStyle;
        _settings.KeepVisible = _keepVisible;
        _settings.SidebarMode = _sidebarMode;

        if (_viewModel != null)
        {
            var s = _viewModel.Sections;
            _settings.ShowWeather = s.ShowWeather;
            _settings.ShowCpu = s.ShowCpu;
            _settings.ShowGpu = s.ShowGpu;
            _settings.ShowRam = s.ShowRam;
            _settings.ShowDisk = s.ShowDisk;
            _settings.ShowBattery = s.ShowBattery;
            _settings.ShowNet = s.ShowNet;
            _settings.ShowProcesses = s.ShowProcesses;
            _settings.ShowBluetooth = s.ShowBluetooth;
            _settings.ShowUptime = s.ShowUptime;
            _settings.UseFahrenheit = s.UseFahrenheit;
        }

        SettingsStore.Save(_settings);
    }

    private void TogglePopup()
    {
        if (_popup == null || _isExiting) return;

        var now = DateTime.UtcNow;
        if ((now - _lastToggle).TotalMilliseconds < 400) return;
        _lastToggle = now;

        if (_popup.SidebarMode)
        {
            // A docked sidebar is persistent; a left-click just brings it to front.
            _dashboardOpen = true;
            _viewModel?.SetDashboardActive(true);
            _popup.Activate();
            return;
        }

        if (_dashboardOpen)
        {
            _dashboardOpen = false;
            _popup.Hide();
            _viewModel?.SetDashboardActive(false);
        }
        else
        {
            _dashboardOpen = true;
            _viewModel?.SetDashboardActive(true);
            _popup.ShowAtTray();
        }
    }

    private void ShowPopup()
    {
        if (_isExiting) return;
        _dashboardOpen = true;
        _lastToggle = DateTime.UtcNow;
        _viewModel?.SetDashboardActive(true);
        _popup?.ShowAtTray();
    }

    private void SetSidebarMode(bool enabled)
    {
        if (_popup == null || _isExiting) return;

        _sidebarMode = enabled;
        if (_sidebarMenuItem != null) _sidebarMenuItem.IsChecked = enabled;

        if (enabled)
        {
            _dashboardOpen = true;
            _viewModel?.SetDashboardActive(true);
            _popup.DockToSide();
        }
        else
        {
            _popup.Undock();
        }

        SaveSettings();
    }

    private float GetCurrentMetricValue()
    {
        if (_viewModel == null) return 0;

        if (_trayMetric == TrayMetric.GPU && _viewModel.SelectedTrayGpuIndex < _viewModel.Gpus.Count)
            return (float)_viewModel.Gpus[_viewModel.SelectedTrayGpuIndex].Data.CoreLoad;

        return _trayMetric switch
        {
            TrayMetric.CPU => _viewModel.Cpu.TotalLoad,
            TrayMetric.RAM => _viewModel.Ram.Load,
            _ => _viewModel.Cpu.TotalLoad
        };
    }

    private string GetTooltip()
    {
        if (_viewModel == null) return "TrayStats";

        if (_trayMetric == TrayMetric.GPU && _viewModel.SelectedTrayGpuIndex < _viewModel.Gpus.Count)
        {
            var gpu = _viewModel.Gpus[_viewModel.SelectedTrayGpuIndex];
            return $"{gpu.DisplayName}: {gpu.Data.CoreLoad:F0}%  |  {gpu.Data.Temperature:F0}°C";
        }

        return _trayMetric switch
        {
            TrayMetric.CPU => $"CPU: {_viewModel.CpuSummary}  |  RAM: {_viewModel.RamSummary}",
            TrayMetric.RAM => $"RAM: {_viewModel.RamSummary}",
            _ => $"CPU: {_viewModel.CpuSummary}"
        };
    }

    private float _lastIconValue = -1;

    private void UpdateTrayIcon(object? sender, EventArgs e)
    {
        if (_trayIcon == null || _viewModel == null) return;

        try
        {
            var currentValue = GetCurrentMetricValue();
            bool valueChanged = Math.Abs(currentValue - _lastIconValue) >= 0.5f;

            if (valueChanged)
            {
                SetTrayIcon(IconGenerator.CreateIcon(currentValue, _iconStyle));
                _lastIconValue = currentValue;
            }

            _trayIcon.ToolTipText = GetTooltip();

            // Rebuild GPU menu items if GPU count changed
            if (_gpuMenuItems == null || _gpuMenuItems.Length != _viewModel.Gpus.Count)
                if (_trayIcon.ContextMenu is ContextMenu cm)
                    BuildGpuMenuItems(cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString() == "Tray Metric")!);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    // IconGenerator returns an Icon created via Icon.FromHandle(HICON), which does NOT own the
    // GDI icon handle (Icon.Dispose() is a no-op for it). If we don't DestroyIcon it, one GDI
    // handle leaks per icon update until the process exhausts its GDI quota. The shell keeps its
    // own copy after Shell_NotifyIcon, so it is safe to destroy the previous handle once a new
    // icon has been assigned.
    private void SetTrayIcon(Icon newIcon)
    {
        if (_trayIcon == null) return;
        IntPtr old = _currentHIcon;
        _trayIcon.Icon = newIcon;
        _currentHIcon = newIcon.Handle;
        if (old != IntPtr.Zero)
            DestroyIcon(old);
    }

    private void ReleaseTrayIconHandle()
    {
        if (_currentHIcon != IntPtr.Zero)
        {
            DestroyIcon(_currentHIcon);
            _currentHIcon = IntPtr.Zero;
        }
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void RestartAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            // Release the mutex before launching so the new instance can acquire it
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });

            ExitApp();
        }
        catch
        {
            // User cancelled UAC prompt -- re-acquire the mutex
            _mutex = new Mutex(true, "TrayStats_SingleInstance", out _);
        }
    }

    private void ExitApp()
    {
        _isExiting = true;
        _iconTimer?.Stop();
        _popup?.ReleaseDock();
        _popup?.Hide();
        SaveSettings();
        _viewModel?.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        ReleaseTrayIconHandle();

        Shutdown();
    }

    private static void LogException(Exception ex)
    {
        try
        {
            var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
            System.IO.File.AppendAllText(logPath, entry);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _popup?.ReleaseDock();
        _viewModel?.Dispose();
        _trayIcon?.Dispose();
        ReleaseTrayIconHandle();
        base.OnExit(e);
    }
}
