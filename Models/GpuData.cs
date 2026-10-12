using CommunityToolkit.Mvvm.ComponentModel;

namespace TrayStats.Models;

public partial class GpuData : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private float _coreLoad;
    [ObservableProperty] private float _temperature;
    [ObservableProperty] private float _coreClock;
    [ObservableProperty] private float _memoryClock;
    [ObservableProperty] private float _fanSpeed;
    [ObservableProperty] private float _fanPercent;
    [ObservableProperty] private float _memoryUsed;
    [ObservableProperty] private float _memoryTotal;
    [ObservableProperty] private float _memoryLoad;
    [ObservableProperty] private float _power;
}

public sealed record GpuTelemetrySnapshot(
    string Name,
    float CoreLoad,
    float Temperature,
    float CoreClock,
    float MemoryClock,
    float FanSpeed,
    float FanPercent,
    float MemoryUsed,
    float MemoryTotal,
    float MemoryLoad,
    float Power);
