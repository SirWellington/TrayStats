using System.Diagnostics;
using System.Drawing;
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
    private IconStyle _iconStyle = IconStyle.MiniChart;
    private TrayMetric _trayMetric = TrayMetric.CPU;
    private bool _isExiting;
    private bool _dashboardOpen;
    private DateTime _lastToggle = DateTime.MinValue;

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
            DataContext = _viewModel
        };
        _popup.DashboardHidden += () =>
        {
            _dashboardOpen = false;
            _lastToggle = DateTime.UtcNow;
            _viewModel?.SetDashboardActive(false);
        };

        CreateTrayIcon();

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

        // Tray metric submenu
        var metricMenu = new MenuItem { Header = "Tray Metric" };
        var cpuMetric = new MenuItem { Header = "CPU", IsCheckable = true, IsChecked = true };
        var gpuMetric = new MenuItem { Header = "GPU", IsCheckable = true };
        var ramMetric = new MenuItem { Header = "RAM", IsCheckable = true };

        cpuMetric.Click += (_, _) => SetTrayMetric(TrayMetric.CPU, cpuMetric, gpuMetric, ramMetric);
        gpuMetric.Click += (_, _) => SetTrayMetric(TrayMetric.GPU, cpuMetric, gpuMetric, ramMetric);
        ramMetric.Click += (_, _) => SetTrayMetric(TrayMetric.RAM, cpuMetric, gpuMetric, ramMetric);

        metricMenu.Items.Add(cpuMetric);
        metricMenu.Items.Add(gpuMetric);
        metricMenu.Items.Add(ramMetric);
        contextMenu.Items.Add(metricMenu);

        // Icon style submenu
        var styleMenu = new MenuItem { Header = "Icon Style" };
        var barItem = new MenuItem { Header = "Bar", IsCheckable = true };
        var pctItem = new MenuItem { Header = "Percentage", IsCheckable = true };
        var chartItem = new MenuItem { Header = "Mini Chart", IsCheckable = true, IsChecked = true };

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
    }

    private void SetTrayMetric(TrayMetric metric, params MenuItem[] items)
    {
        _trayMetric = metric;
        foreach (var item in items)
            item.IsChecked = false;

        var selected = metric switch
        {
            TrayMetric.CPU => items[0],
            TrayMetric.GPU => items[1],
            TrayMetric.RAM => items[2],
            _ => items[0]
        };
        selected.IsChecked = true;

        if (_viewModel != null)
            _viewModel.NeedsGpuInBackground = metric == TrayMetric.GPU;
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
    }

    private static void AddSectionToggle(MenuItem parent, string label, Func<bool> getter, Action<bool> setter)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = true,
            IsChecked = getter()
        };
        item.Click += (_, _) => setter(item.IsChecked);
        parent.Items.Add(item);
    }

    private void TogglePopup()
    {
        if (_popup == null || _isExiting) return;

        var now = DateTime.UtcNow;
        if ((now - _lastToggle).TotalMilliseconds < 400) return;
        _lastToggle = now;

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

    private float GetCurrentMetricValue()
    {
        if (_viewModel == null) return 0;
        return _trayMetric switch
        {
            TrayMetric.CPU => _viewModel.Cpu.TotalLoad,
            TrayMetric.GPU => _viewModel.Gpu.CoreLoad,
            TrayMetric.RAM => _viewModel.Ram.Load,
            _ => _viewModel.Cpu.TotalLoad
        };
    }

    private string GetTooltip()
    {
        if (_viewModel == null) return "TrayStats";
        return _trayMetric switch
        {
            TrayMetric.CPU => $"CPU: {_viewModel.CpuSummary}  |  RAM: {_viewModel.RamSummary}",
            TrayMetric.GPU => $"GPU: {_viewModel.GpuSummary}  |  {_viewModel.Gpu.Temperature:F0}°C",
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
                // Dispose old icon BEFORE assigning the new one so WPF composition engine releases the visual resource immediately
                _trayIcon.Icon?.Dispose();
                _trayIcon.Icon = IconGenerator.CreateIcon(currentValue, _iconStyle);
                _lastIconValue = currentValue;
            }

            _trayIcon.ToolTipText = GetTooltip();
        }
        catch (Exception ex)
        {
            LogException(ex);
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
        _popup?.Hide();
        _viewModel?.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }

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
        _viewModel?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
