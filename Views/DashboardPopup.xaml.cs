using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TrayStats.Helpers;
using TrayStats.Models;
using TrayStats.ViewModels;
using TrayStats.Views.Components;

namespace TrayStats.Views;

public partial class DashboardPopup : Window
{
    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    /// <summary>
    /// True once the user has dragged the window (or a saved position was restored).
    /// While set, the window is not snapped back to the tray corner on resize.
    /// </summary>
    private bool _userMoved;

    public DashboardPopup()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    public event Action? DashboardHidden;

    /// <summary>
    /// Raised when the user closes the dashboard while it is docked, which
    /// turns sidebar mode off. The owner (App) updates its menu state.
    /// </summary>
    public event Action? SidebarModeDisabled;

    /// <summary>
    /// When true, the window does not hide on deactivation.
    /// </summary>
    public bool KeepVisible { get; set; }

    /// <summary>
    /// True while the window is docked to the screen edge as a sidebar.
    /// </summary>
    public bool SidebarMode { get; private set; }

    public bool IsDockSuspended => _dockSuspended;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is DashboardViewModel oldVm)
            oldVm.InvalidateCharts -= OnInvalidateCharts;

        if (e.NewValue is DashboardViewModel newVm)
            newVm.InvalidateCharts += OnInvalidateCharts;
    }

    private void OnInvalidateCharts()
    {
        CpuChart.InvalidateValues();
        RamChart.InvalidateValues();
        BatteryChart.InvalidateValues();
        NetDownChart.InvalidateValues();
        NetUpChart.InvalidateValues();

        // Invalidate all GPU sparkline charts in the ItemsControl
        if (GpuItems is { } gpuContainer)
        {
            for (int i = 0; i < gpuContainer.Items.Count; i++)
            {
                var container = gpuContainer.ItemContainerGenerator.ContainerFromIndex(i) as DependencyObject;
                if (container != null)
                    FindAndInvalidateSparklineCharts(container);
            }
        }
    }

    private static void FindAndInvalidateSparklineCharts(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is SparklineChart chart)
                chart.InvalidateValues();
            else
                FindAndInvalidateSparklineCharts(child);
        }
    }

    public void ShowAtTray()
    {
        if (SidebarMode)
        {
            if (_dockSuspended)
                return;

            // Keep the docked sidebar visible when it is re-shown.
            Show();
            Activate();
            return;
        }

        ApplyMaxHeight();

        // Respect a user-chosen position (this session or a previous one). Only
        // fall back to the tray corner when the window has not been moved.
        if (!_userMoved && !RestoreSavedPosition())
            PositionNearTray();

        Show();
        Activate();
    }

    // ---- Sidebar mode (docked to the screen edge) --------------------------
    //
    // Docking uses the shell app-bar API (SHAppBarMessage), the same mechanism
    // the taskbar uses. Registering an app bar on a screen edge makes the shell
    // reserve that strip of the work area, so maximized windows stop at the
    // dashboard's edge instead of sliding under it. A direct SPI_SETWORKAREA
    // change would be overwritten by Explorer on Windows 10+, so the app-bar
    // route is the one that sticks.

    private IntPtr _dockedMonitor = IntPtr.Zero;
    private RECT _dockMonitorRect;
    private RECT _dockWorkRect;
    private int _dockWidthPx;
    private double _dockWidthDip;
    private DispatcherTimer? _remoteDesktopTimer;
    private IntPtr _fullscreenRemoteDesktop = IntPtr.Zero;
    private bool _dockSuspended;

    /// <summary>
    /// Docks the window to the right edge of the monitor it is currently on,
    /// spanning that monitor's full height, and reserves the strip so maximized
    /// windows avoid it.
    /// </summary>
    public void DockToSide()
    {
        IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
        bool resuming = _dockSuspended;
        if (!SidebarMode)
            _dockWidthDip = Width;

        // Re-dock cleanly if we were already docked (e.g. after a DPI change).
        if (SidebarMode && !resuming)
        {
            RemoveAppBar();
            SidebarMode = false;
        }

        IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
            hMonitor = GetPrimaryMonitor();

        MONITORINFO mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
            return;

        double scale = GetMonitorDpiScale(hMonitor);
        int widthPx = (int)Math.Round(_dockWidthDip * scale);

        var workAreaBefore = SystemParameters.WorkArea;

        var abd = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = 0,
            uEdge = ABE_RIGHT,
            rc = new RECT
            {
                Left = mi.rcWork.Right - widthPx,
                Top = mi.rcWork.Top,
                Right = mi.rcWork.Right,
                Bottom = mi.rcWork.Bottom
            }
        };

        int reqLeft = abd.rc.Left; // capture request rect for logging
        uint newRc = (uint)SHAppBarMessage(ABM_NEW, ref abd).ToUInt32();
        uint queryRc = (uint)SHAppBarMessage(ABM_QUERYPOS, ref abd).ToUInt32();

        // The shell may adjust the rect during QUERYPOS (e.g. to avoid the
        // taskbar), which can drop our width. Re-apply it for the right edge so
        // the committed bar keeps the intended width.
        abd.rc.Left = abd.rc.Right - widthPx;

        uint setRc = (uint)SHAppBarMessage(ABM_SETPOS, ref abd).ToUInt32();
        bool newOk = newRc != 0;
        bool queryOk = queryRc != 0;
        bool setOk = setRc != 0;

        var workAreaAfter = SystemParameters.WorkArea;
        LogDock($"Dock: hwnd={hwnd} monitor={hMonitor} scale={scale:F3} widthPx={widthPx} " +
                $"monitorRect=({mi.rcMonitor.Left},{mi.rcMonitor.Top},{mi.rcMonitor.Right},{mi.rcMonitor.Bottom}) " +
                $"workRect=({mi.rcWork.Left},{mi.rcWork.Top},{mi.rcWork.Right},{mi.rcWork.Bottom}) " +
                $"reqLeft={reqLeft} new={newOk}({newRc}) query={queryOk}({queryRc}) set={setOk}({setRc}) " +
                $"finalRc=({abd.rc.Left},{abd.rc.Top},{abd.rc.Right},{abd.rc.Bottom}) " +
                $"workAreaBefore=({workAreaBefore.Left},{workAreaBefore.Top},{workAreaBefore.Right},{workAreaBefore.Bottom}) " +
                $"workAreaAfter=({workAreaAfter.Left},{workAreaAfter.Top},{workAreaAfter.Right},{workAreaAfter.Bottom})");

        // Match the WPF window (DIPs) to the shell-approved app-bar rect (px).
        // Bleed 1 DIP past the top/right screen edges so WPF's pixel rounding
        // can never leave a hairline of desktop showing at the docked corner.
        SizeToContent = SizeToContent.Manual;
        MaxHeight = double.PositiveInfinity;
        Width = (abd.rc.Right - abd.rc.Left) / scale + 1;
        Height = (abd.rc.Bottom - abd.rc.Top) / scale + 1;
        Left = abd.rc.Left / scale;
        Top = abd.rc.Top / scale - 1;
        _userMoved = true;

        // Collapse the floating margin (normally room for the drop shadow) and
        // square off the corners so the content fills the full reserved rect and
        // no desktop shows through at the edges or the rounded corner.
        RootBorder.Margin = new Thickness(0);
        RootBorder.CornerRadius = new CornerRadius(0);

        _dockedMonitor = hMonitor;
        _dockMonitorRect = mi.rcMonitor;
        _dockWorkRect = mi.rcWork;
        _dockWidthPx = widthPx;
        SidebarMode = true;
        _dockSuspended = false;
        Topmost = true;

        // Fullscreen Remote Desktop needs the entire monitor, including the
        // area normally reserved by our app bar. Check before showing so a
        // restored sidebar does not steal focus from an existing session.
        if (_remoteDesktopTimer == null)
        {
            _remoteDesktopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _remoteDesktopTimer.Tick += CheckRemoteDesktop;
        }
        _remoteDesktopTimer.Start();
        CheckRemoteDesktop(this, EventArgs.Empty);

        if (!IsVisible && !_dockSuspended)
        {
            Show();

            // WPF can move a newly shown window into the work area. At this
            // point that area already excludes our app bar, so it shifts the
            // sidebar left by its own width. Place the shown HWND back on the
            // shell-approved edge without changing its size or taking focus.
            SetWindowPos(hwnd, IntPtr.Zero, abd.rc.Left,
                (int)Math.Round(abd.rc.Top - scale), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            if (!resuming)
                Activate();
        }
    }

    private void CheckRemoteDesktop(object? sender, EventArgs e)
    {
        if (!SidebarMode)
            return;

        // Keep the strip free even if focus moves to the RDP connection bar
        // or another local window while the fullscreen session is still open.
        if (_dockSuspended && IsFullscreenRemoteDesktop(_fullscreenRemoteDesktop))
            return;

        IntPtr foreground = GetForegroundWindow();
        if (IsFullscreenRemoteDesktop(foreground))
        {
            _fullscreenRemoteDesktop = foreground;
            if (!_dockSuspended)
            {
                RemoveAppBar();
                _dockSuspended = true;
                Topmost = false;
                Hide();
            }
        }
        else if (_dockSuspended)
        {
            _fullscreenRemoteDesktop = IntPtr.Zero;
            DockToSide();
        }
    }

    private bool IsFullscreenRemoteDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd) ||
            MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) != _dockedMonitor ||
            !GetWindowRect(hwnd, out RECT rect))
            return false;

        // A maximized, windowed RDP client must still respect the sidebar.
        // Fullscreen may cover the monitor outright or start borderless at the
        // available work-area width while our app bar is registered.
        const int tolerance = 16;
        bool coversMonitor = rect.Left <= _dockMonitorRect.Left + tolerance &&
                             rect.Top <= _dockMonitorRect.Top + tolerance &&
                             rect.Right >= _dockMonitorRect.Right - tolerance &&
                             rect.Bottom >= _dockMonitorRect.Bottom - tolerance;
        bool fillsWorkAreaWithoutChrome = (GetWindowLong(hwnd, GWL_STYLE) & WS_CAPTION) == 0 &&
                                          !IsZoomed(hwnd) &&
                                          rect.Left <= _dockWorkRect.Left + tolerance &&
                                          rect.Top <= _dockWorkRect.Top + tolerance &&
                                          rect.Right >= _dockMonitorRect.Right - _dockWidthPx - tolerance &&
                                          rect.Bottom >= _dockWorkRect.Bottom - tolerance;
        if (!coversMonitor && !fillsWorkAreaWithoutChrome)
            return false;

        GetWindowThreadProcessId(hwnd, out uint processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("mstsc", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("msrdc", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; } // The window just closed.
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private void RemoveAppBar()
    {
        var abd = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = new WindowInteropHelper(this).EnsureHandle()
        };
        SHAppBarMessage(ABM_REMOVE, ref abd);
    }

    private void StopRemoteDesktopWatch()
    {
        _remoteDesktopTimer?.Stop();
        _fullscreenRemoteDesktop = IntPtr.Zero;
        _dockSuspended = false;
        Topmost = true;
    }

    /// <summary>
    /// Removes the app bar (restoring the work area) and returns the window to
    /// its floating position near the tray.
    /// </summary>
    public void Undock()
    {
        if (!SidebarMode)
            return;

        if (!_dockSuspended)
            RemoveAppBar();
        StopRemoteDesktopWatch();
        _dockedMonitor = IntPtr.Zero;
        SidebarMode = false;

        SizeToContent = SizeToContent.Height;
        Width = _dockWidthDip;
        ApplyMaxHeight();
        _userMoved = false;

        // Restore the floating margin (drop-shadow room) and rounded corners now
        // that we are no longer flush against the screen edge.
        RootBorder.Margin = new Thickness(4);
        RootBorder.CornerRadius = new CornerRadius(8);

        if (IsVisible)
            PositionNearTray();
    }

    /// <summary>
    /// Releases the app bar registration. Called on app exit so the reserved
    /// work area is not left behind.
    /// </summary>
    public void ReleaseDock()
    {
        if (!SidebarMode)
            return;

        if (!_dockSuspended)
            RemoveAppBar();
        StopRemoteDesktopWatch();
        _dockedMonitor = IntPtr.Zero;
        SidebarMode = false;
    }

    private void ApplyMaxHeight()
    {
        var workArea = SystemParameters.WorkArea;
        MaxHeight = workArea.Height - 20;
    }

    private void PositionNearTray()
    {
        var workArea = SystemParameters.WorkArea;

        Left = workArea.Right - Width - 8;

        double height = ActualHeight > 0 ? ActualHeight : 400;
        Top = workArea.Bottom - height - 8;

        // Clamp so the top never goes above the work area
        if (Top < workArea.Top)
            Top = workArea.Top + 4;
    }

    /// <summary>
    /// Restores the last saved window position if it is still on a visible
    /// monitor. Returns true if a position was applied.
    /// </summary>
    private bool RestoreSavedPosition()
    {
        var saved = WindowPositionStore.Load();
        if (saved == null)
            return false;

        if (!IsPositionOnScreen(saved.Left, saved.Top))
            return false;

        Left = saved.Left;
        Top = saved.Top;
        _userMoved = true;
        return true;
    }

    private void SavePosition()
    {
        WindowPositionStore.Save(new WindowPosition { Left = Left, Top = Top });
    }

    /// <summary>
    /// True if the given top-left corner (in DIPs) is at least partially on a
    /// visible monitor. The virtual-screen bounds come back in physical pixels,
    /// so they are converted to DIPs before comparing.
    /// </summary>
    private static bool IsPositionOnScreen(double left, double top)
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int cx = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int cy = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (cx <= 0 || cy <= 0)
            return true; // Unknown virtual screen; trust the saved value.

        double scale = GetDpiScale();
        double dipX = x / scale;
        double dipY = y / scale;
        double dipCx = cx / scale;
        double dipCy = cy / scale;

        return left + 40 > dipX && left < dipX + dipCx
            && top + 40 > dipY && top < dipY + dipCy;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarMode)
            return;

        // Only keep the window pinned to the tray corner while the user has not
        // taken control of its position.
        if (IsVisible && !_userMoved)
            PositionNearTray();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!SidebarMode && !_userMoved)
            PositionNearTray();
    }

    // ---- Dragging ----------------------------------------------------------

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start a drag when the user is clicking the close button.
        var source = e.OriginalSource as DependencyObject;
        while (source != null && !ReferenceEquals(source, sender))
        {
            if (source is Button)
                return;
            source = VisualTreeHelper.GetParent(source);
        }

        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        var before = new Point(Left, Top);
        DragMove();

        if (Left != before.X || Top != before.Y)
        {
            _userMoved = true;
            SavePosition();
        }
    }

    // ---- DPI change (e.g. returning from an RDP session) ------------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.DpiChanged += OnDpiChanged;
    }

    private void OnDpiChanged(object? sender, EventArgs e)
    {
        if (SidebarMode)
        {
            if (_dockSuspended)
                return;

            // Re-dock so the app-bar rect and window size track the new DPI.
            DockToSide();
            return;
        }

        // The screen DPI changed (e.g. the RDP session ended and the local
        // session took over at a different scaling). WPF re-renders the content
        // at the new DPI but does not re-run the SizeToContent sizing pass, so
        // the window keeps its old pixel height and the content looks squeezed.
        // Force the sizing pass to run again.
        ApplyMaxHeight();

        SizeToContent = SizeToContent.Manual;
        UpdateLayout();
        SizeToContent = SizeToContent.Height;
        UpdateLayout();

        if (!_userMoved)
            PositionNearTray();
    }

    // ---- Win32 -------------------------------------------------------------

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const int LOGPIXELSX = 88;

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    /// <summary>
    /// The current system DPI scale factor (1.0 = 96 DPI, 1.5 = 144 DPI, ...).
    /// </summary>
    private static double GetDpiScale()
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return 1.0;
        try
        {
            int dpi = GetDeviceCaps(hdc, LOGPIXELSX);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    // ---- Shell app bar / monitor / per-monitor DPI -------------------------

    private const uint ABM_NEW = 0;
    private const uint ABM_REMOVE = 1;
    private const uint ABM_QUERYPOS = 3;
    private const uint ABM_SETPOS = 4;

    private const uint ABE_RIGHT = 2;

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlag);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, uint dpiType, out uint dpiX, out uint dpiY);

    private static IntPtr GetPrimaryMonitor()
    {
        IntPtr hwnd = IntPtr.Zero;
        return MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
    }

    /// <summary>
    /// DPI scale (1.0 = 96 DPI) of a specific monitor, falling back to the
    /// system scale if the per-monitor call is unavailable.
    /// </summary>
    private static double GetMonitorDpiScale(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero &&
            GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _) == 0 &&
            dpiX > 0)
            return dpiX / 96.0;

        return GetDpiScale();
    }

    /// <summary>
    /// Appends a line to the dock diagnostics log so we can see exactly what the
    /// shell app-bar calls return and how the work area changes.
    /// </summary>
    private static void LogDock(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TrayStats", "dock.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // A docked sidebar is a persistent panel; it must not vanish when the
        // user clicks another window (which would leave a reserved gap).
        if (KeepVisible || SidebarMode) return;
        Hide();
        DashboardHidden?.Invoke();
    }

    private void WeatherRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleWeatherDetailCommand.Execute(null);
    }

    private void CpuRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleCpuDetailCommand.Execute(null);
    }

    private void GpuItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GpuDisplayModel gpu)
            gpu.IsExpanded = !gpu.IsExpanded;
    }

    private void RamRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleRamDetailCommand.Execute(null);
    }

    private void DiskRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleDiskDetailCommand.Execute(null);
    }

    private void BatteryRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleBatteryDetailCommand.Execute(null);
    }

    private void NetRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleNetDetailCommand.Execute(null);
    }

    private void ProcessesRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleProcessesDetailCommand.Execute(null);
    }

    private void BluetoothRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleBluetoothDetailCommand.Execute(null);
    }

    private void UptimeRow_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.ToggleUptimeDetailCommand.Execute(null);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarMode)
        {
            // Closing a docked sidebar turns the mode off and frees the work area.
            Undock();
            SidebarModeDisabled?.Invoke();
        }

        Hide();
        DashboardHidden?.Invoke();
    }
}
