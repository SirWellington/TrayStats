using CommunityToolkit.Mvvm.ComponentModel;

namespace TrayStats.Models;

public partial class CpuCoreData : ObservableObject
{
    [ObservableProperty] private int _coreIndex;
    [ObservableProperty] private float _usage;
    [ObservableProperty] private float _temperature;
    [ObservableProperty] private float _clock;
    [ObservableProperty] private bool _isParked;
    [ObservableProperty] private bool _isPCore = true;
}

public partial class CpuData : ObservableObject
{
    [ObservableProperty] private float _totalLoad;
    [ObservableProperty] private float _temperature;
    [ObservableProperty] private float _packagePower;
    [ObservableProperty] private float _clock;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _coreCount;
    [ObservableProperty] private int _threadCount;

    public List<CpuCoreData> Cores { get; set; } = new();
}
