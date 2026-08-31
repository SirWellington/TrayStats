using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    /// When true, the window does not hide on deactivation.
    /// </summary>
    public bool KeepVisible { get; set; }

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
        ApplyMaxHeight();

        // Respect a user-chosen position (this session or a previous one). Only
        // fall back to the tray corner when the window has not been moved.
        if (!_userMoved && !RestoreSavedPosition())
            PositionNearTray();

        Show();
        Activate();
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
        // Only keep the window pinned to the tray corner while the user has not
        // taken control of its position.
        if (IsVisible && !_userMoved)
            PositionNearTray();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (!_userMoved)
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

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (KeepVisible) return;
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
        Hide();
        DashboardHidden?.Invoke();
    }
}
