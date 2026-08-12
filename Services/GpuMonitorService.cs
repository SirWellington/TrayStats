using LibreHardwareMonitor.Hardware;
using TrayStats.Models;

namespace TrayStats.Services;

public sealed class GpuMonitorService : IMonitorService
{
    private const float SwitchThreshold = 10f;
    private const float IdleThreshold = 15f;

    private readonly HardwareContext _context;

    // Primary GPU (auto-selected for backward compatibility)
    public GpuData Data { get; } = new();

    // All detected GPUs with stable references
    public List<GpuData> AllGpus { get; } = new();

    public event Action? DataUpdated;

    private readonly Dictionary<string, GpuEntry> _gpuMap = new(StringComparer.Ordinal);

    public GpuMonitorService(HardwareContext context)
    {
        _context = context;
    }

    public void Start() => _context.HardwareUpdated += OnHardwareUpdated;
    public void Stop() => _context.HardwareUpdated -= OnHardwareUpdated;

    private void OnHardwareUpdated()
    {
        try
        {
            Update();
            DataUpdated?.Invoke();
        }
        catch { }
    }

    private static int GpuPriority(HardwareType t) => t switch
    {
        HardwareType.GpuNvidia => 3,
        HardwareType.GpuAmd => 2,
        HardwareType.GpuIntel => 1,
        _ => 0
    };

    private void Update()
    {
        var hardware = _context.GetHardware();

        var gpus = new List<IHardware>();
        try
        {
            foreach (var hw in hardware)
            {
                if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    gpus.Add(hw);
            }
        }
        catch { return; }

        if (gpus.Count == 0)
        {
            AllGpus.Clear();
            _gpuMap.Clear();
            Data.Name = string.Empty;
            return;
        }

        // Build key set for currently present GPUs
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hw in gpus)
        {
            var key = GetGpuKey(hw);
            currentKeys.Add(key);

            if (!_gpuMap.TryGetValue(key, out var entry))
            {
                var gpuData = new GpuData();
                _gpuMap[key] = new GpuEntry(gpuData);
                AllGpus.Add(gpuData);
            }

            _gpuMap[key].Hardware = hw;
        }

        // Remove disconnected GPUs
        foreach (var key in _gpuMap.Keys.ToList())
        {
            if (!currentKeys.Contains(key))
            {
                var removed = _gpuMap[key];
                _gpuMap.Remove(key);
                AllGpus.Remove(removed.GpuData);
            }
        }

        // Sort: discrete GPUs first (by priority), then by index stability
        AllGpus.Sort((a, b) =>
        {
            var aEntry = _gpuMap.Values.FirstOrDefault(e => ReferenceEquals(e.GpuData, a));
            var bEntry = _gpuMap.Values.FirstOrDefault(e => ReferenceEquals(e.GpuData, b));
            if (aEntry == null || bEntry == null || aEntry.Hardware == null || bEntry.Hardware == null) return 0;
            return GpuPriority(bEntry.Hardware.HardwareType) - GpuPriority(aEntry.Hardware.HardwareType);
        });

        // Re-sort map values to match AllGpus order for priority selection
        foreach (var kvp in _gpuMap)
        {
            ReadSensors(kvp.Value.Hardware!, kvp.Value.GpuData);
        }

        // Auto-select primary GPU for backward-compatible Data property
        var bestEntry = SelectPrimaryGpu();
        if (bestEntry != null)
        {
            // Copy data from selected GPU to primary Data
            Data.Name = bestEntry.GpuData.Name;
            Data.CoreLoad = bestEntry.GpuData.CoreLoad;
            Data.Temperature = bestEntry.GpuData.Temperature;
            Data.CoreClock = bestEntry.GpuData.CoreClock;
            Data.MemoryClock = bestEntry.GpuData.MemoryClock;
            Data.FanSpeed = bestEntry.GpuData.FanSpeed;
            Data.FanPercent = bestEntry.GpuData.FanPercent;
            Data.MemoryUsed = bestEntry.GpuData.MemoryUsed;
            Data.MemoryTotal = bestEntry.GpuData.MemoryTotal;
            Data.MemoryLoad = bestEntry.GpuData.MemoryLoad;
            Data.Power = bestEntry.GpuData.Power;
        }
    }

    private static string GetGpuKey(IHardware hw) => $"{hw.HardwareType}:{hw.Name}";

    private GpuEntry? SelectPrimaryGpu()
    {
        if (_gpuMap.Count == 0) return null;

        var entries = _gpuMap.Values.Where(e => e.Hardware != null).ToList();
        if (entries.Count == 1) return entries[0];

        // Find current primary match
        GpuEntry? current = null;
        foreach (var entry in entries)
        {
            if (entry.GpuData.Name == Data.Name && entry.GpuData.CoreLoad > 0 || entry.Hardware!.Name == Data.Name)
            {
                current = entry;
                break;
            }
        }

        // Default to highest priority GPU
        GpuEntry? best = entries.MaxBy(e => GpuPriority(e.Hardware!.HardwareType));

        if (current != null && best != null)
        {
            float currentLoad = GetGpuLoad(current.Hardware!);
            float bestLoad = GetGpuLoad(best.Hardware!);

            bool shouldSwitch = false;
            if (currentLoad < IdleThreshold && bestLoad < IdleThreshold)
                shouldSwitch = GpuPriority(best.Hardware!.HardwareType) > GpuPriority(current.Hardware!.HardwareType);
            else
                shouldSwitch = bestLoad - currentLoad > SwitchThreshold;

            return shouldSwitch ? best : current;
        }

        return best;
    }

    private static float GetGpuLoad(IHardware hw)
    {
        float coreLoad = 0f, d3dLoad = 0f;
        try
        {
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.Value is not { } val) continue;
                if (sensor.SensorType != SensorType.Load) continue;
                if (sensor.Name == "GPU Core") coreLoad = val;
                else if (sensor.Name.StartsWith("D3D") && val > d3dLoad) d3dLoad = val;
            }
        }
        catch { }
        return coreLoad > 0 ? coreLoad : d3dLoad;
    }

    private static void ReadSensors(IHardware hw, GpuData data)
    {
        try
        {
            data.Name = hw.Name;
            data.CoreLoad = 0;
            data.Temperature = 0;
            data.CoreClock = 0;
            data.MemoryClock = 0;
            data.FanSpeed = 0;
            data.FanPercent = 0;
            data.MemoryUsed = 0;
            data.MemoryTotal = 0;
            data.MemoryLoad = 0;
            data.Power = 0;

            float bestD3DLoad = 0;

            foreach (var sensor in hw.Sensors)
            {
                if (sensor.Value is not { } val) continue;

                switch (sensor.SensorType)
                {
                    case SensorType.Load when sensor.Name == "GPU Core":
                        data.CoreLoad = val;
                        break;
                    case SensorType.Load when sensor.Name.StartsWith("D3D"):
                        if (val > bestD3DLoad) bestD3DLoad = val;
                        break;
                    case SensorType.Temperature when sensor.Name.Contains("GPU"):
                        data.Temperature = val;
                        break;
                    case SensorType.Clock when sensor.Name == "GPU Core":
                        data.CoreClock = val;
                        break;
                    case SensorType.Clock when sensor.Name == "GPU Memory":
                        data.MemoryClock = val;
                        break;
                    case SensorType.Fan:
                        if (data.FanSpeed == 0 || val > 0)
                            data.FanSpeed = val;
                        break;
                    case SensorType.Control:
                        if (data.FanPercent == 0 || val > 0)
                            data.FanPercent = val;
                        break;
                    case SensorType.SmallData when sensor.Name.Contains("Memory Used"):
                        if (val > data.MemoryUsed) data.MemoryUsed = val;
                        break;
                    case SensorType.SmallData when sensor.Name.Contains("Memory Total"):
                        if (val > data.MemoryTotal) data.MemoryTotal = val;
                        break;
                    case SensorType.Load when sensor.Name == "GPU Memory":
                        data.MemoryLoad = val;
                        break;
                    case SensorType.Power:
                        data.Power = val;
                        break;
                }
            }

            if (data.CoreLoad == 0 && bestD3DLoad > 0)
                data.CoreLoad = bestD3DLoad;
        }
        catch { }
    }

    private sealed class GpuEntry
    {
        public IHardware? Hardware;
        public readonly GpuData GpuData;

        public GpuEntry(GpuData gpuData) => GpuData = gpuData;
    }

    public void Dispose() => Stop();
}
