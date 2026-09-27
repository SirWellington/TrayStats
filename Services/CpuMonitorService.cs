using System.Management;
using LibreHardwareMonitor.Hardware;
using TrayStats.Models;

namespace TrayStats.Services;

public sealed class CpuMonitorService : IMonitorService
{
    private readonly HardwareContext _context;
    private readonly WindowsCpuUsageSampler _usageSampler = new();
    private readonly WindowsCpuPerfSampler _perfSampler = new();
    private bool _needsFallback;
    private float[] _lpUsage = [];
    private bool[] _lpParked = [];

    public CpuData Data { get; } = new();
    public event Action? DataUpdated;

    public CpuMonitorService(HardwareContext context)
    {
        _context = context;
        InitCores();
        DetectFallbackNeeded();
    }

    public void Start() => _context.HardwareUpdated += OnHardwareUpdated;
    public void Stop() => _context.HardwareUpdated -= OnHardwareUpdated;

    private void OnHardwareUpdated()
    {
        try
        {
            Update();

            if (_needsFallback && _context.TickCount % 5 == 0)
                UpdateFallback();

            DataUpdated?.Invoke();
        }
        catch { }
    }

    private CpuTopology.Info _topology = CpuTopology.Info.Unknown;

    private void InitCores()
    {
        foreach (var hw in _context.GetHardware())
        {
            if (hw.HardwareType != HardwareType.Cpu) continue;

            hw.Update();
            Data.Name = hw.Name;

            int coreCount = 0;
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Load &&
                    TryParseCoreIndex(sensor.Name, out int index))
                    coreCount = Math.Max(coreCount, index + 1);
            }

            Data.CoreCount = coreCount > 0 ? coreCount : Environment.ProcessorCount;
            Data.ThreadCount = Environment.ProcessorCount;

            _topology = CpuTopology.Get();
            for (int i = 0; i < Data.ThreadCount; i++)
                Data.Cores.Add(new CpuCoreData { CoreIndex = i, IsPCore = _topology.IsPPerLp[i] });

            break;
        }
    }

    private void DetectFallbackNeeded()
    {
        foreach (var hw in _context.GetHardware())
        {
            if (hw.HardwareType != HardwareType.Cpu) continue;
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                    return;
            }
        }
        _needsFallback = true;
    }

    private void Update()
    {
        foreach (var hw in _context.GetHardware())
        {
            if (hw.HardwareType != HardwareType.Cpu) continue;

            float maxClock = 0;
            float fallbackTemp = 0;
            float fallbackPower = 0;

            foreach (var sensor in hw.Sensors)
            {
                if (sensor.Value is not { } val) continue;

                switch (sensor.SensorType)
                {
                    case SensorType.Load when sensor.Name == "CPU Total":
                        Data.TotalLoad = val;
                        break;

                    case SensorType.Temperature when sensor.Name.Contains("Package") || sensor.Name.Contains("Average"):
                        Data.Temperature = val;
                        break;
                    case SensorType.Temperature when sensor.Name.StartsWith("CPU Core #"):
                        if (TryParseCoreIndex(sensor.Name, out int tempIdx))
                            SetCoreSensors(tempIdx, temperature: val);
                        if (val > fallbackTemp) fallbackTemp = val;
                        break;
                    case SensorType.Temperature:
                        if (val > fallbackTemp) fallbackTemp = val;
                        break;

                    case SensorType.Clock when sensor.Name.StartsWith("CPU Core #"):
                        if (TryParseCoreIndex(sensor.Name, out int clkIdx))
                            SetCoreSensors(clkIdx, clock: val);
                        if (val > maxClock) maxClock = val;
                        break;
                    case SensorType.Clock when sensor.Name.Contains("Core"):
                        if (val > maxClock) maxClock = val;
                        break;

                    case SensorType.Power when sensor.Name.Contains("Package"):
                        Data.PackagePower = val;
                        break;
                    case SensorType.Power:
                        if (Data.PackagePower == 0 && val > fallbackPower)
                            fallbackPower = val;
                        break;
                }
            }

            // Windows performance counters report per-LP usage and the current parking state.
            if (_lpUsage.Length != Data.Cores.Count)
            {
                _lpUsage = new float[Data.Cores.Count];
                _lpParked = new bool[Data.Cores.Count];
            }

            if (_perfSampler.TrySample(_lpUsage, _lpParked))
            {
                for (int i = 0; i < Data.Cores.Count; i++)
                {
                    Data.Cores[i].Usage = _lpUsage[i];
                    Data.Cores[i].IsParked = _lpParked[i];
                }
            }

            // The Windows aggregate counter covers every logical processor and avoids
            // anomalous CPU Total readings from the hardware sensor on some systems.
            if (_usageSampler.TrySample(out float usage)) Data.TotalLoad = usage;

            if (maxClock > 0) Data.Clock = maxClock;
            if (Data.Temperature == 0 && fallbackTemp > 0) Data.Temperature = fallbackTemp;
            if (Data.PackagePower == 0 && fallbackPower > 0) Data.PackagePower = fallbackPower;

            break;
        }
    }

    private void SetCoreSensors(int coreIdx, float? temperature = null, float? clock = null)
    {
        // LHM "CPU Core #N" sensors are per physical core; spread them onto that core's logical processors.
        if (coreIdx < 0 || coreIdx >= _topology.LpsOfCore.Length) return;

        foreach (int lp in _topology.LpsOfCore[coreIdx])
        {
            if (lp >= Data.Cores.Count) continue;
            if (temperature is { } t) Data.Cores[lp].Temperature = t;
            if (clock is { } c) Data.Cores[lp].Clock = c;
        }
    }

    private void UpdateFallback()
    {
        try
        {
            if (Data.Clock == 0)
            {
                using var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    var current = Convert.ToSingle(obj["CurrentClockSpeed"]);
                    if (current > Data.Clock) Data.Clock = current;
                }
            }

            if (Data.Temperature == 0)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(@"root\WMI",
                        "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                    float maxTemp = 0;
                    foreach (var obj in searcher.Get())
                    {
                        var raw = Convert.ToSingle(obj["CurrentTemperature"]);
                        var celsius = (raw / 10f) - 273.15f;
                        if (celsius > maxTemp && celsius < 150) maxTemp = celsius;
                    }
                    if (maxTemp > 0) Data.Temperature = maxTemp;
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool TryParseCoreIndex(string name, out int index)
    {
        index = 0;
        int hashPos = name.IndexOf('#');
        if (hashPos < 0 || hashPos + 1 >= name.Length) return false;

        var numSpan = name.AsSpan(hashPos + 1);
        int spacePos = numSpan.IndexOf(' ');
        if (spacePos > 0)
            numSpan = numSpan[..spacePos];

        if (int.TryParse(numSpan, out int parsed))
        {
            index = parsed - 1;
            return index >= 0;
        }
        return false;
    }

    public void Dispose()
    {
        Stop();
        _usageSampler.Dispose();
        _perfSampler.Dispose();
    }
}
