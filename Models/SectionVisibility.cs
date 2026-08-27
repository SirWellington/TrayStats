using CommunityToolkit.Mvvm.ComponentModel;

namespace TrayStats.Models;

public partial class SectionVisibility : ObservableObject
{
    [ObservableProperty] private bool _showWeather = true;
    [ObservableProperty] private bool _showCpu = true;
    [ObservableProperty] private bool _showGpu = true;
    [ObservableProperty] private bool _showRam = true;
    [ObservableProperty] private bool _showDisk = true;
    [ObservableProperty] private bool _showBattery = true;
    [ObservableProperty] private bool _showNet = true;
    [ObservableProperty] private bool _showProcesses = true;
    [ObservableProperty] private bool _showBluetooth = true;
    [ObservableProperty] private bool _showUptime = true;
    [ObservableProperty] private bool _useFahrenheit;
}
