using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TrayStats.Models;
using TrayStats.ViewModels;
using TrayStats.Views.Components;

namespace TrayStats.Views;

public partial class DashboardPopup : Window
{
    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsVisible)
            PositionNearTray();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PositionNearTray();
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
