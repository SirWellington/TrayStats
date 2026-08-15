using System.Diagnostics;
using System.Timers;
using TrayStats.Models;

namespace TrayStats.Services;

public sealed class ProcessMonitorService : IMonitorService
{
    private readonly System.Timers.Timer _timer;
    private readonly int _processorCount;
    private readonly string _ownProcessName = Process.GetCurrentProcess().ProcessName;
    private Dictionary<int, (string Name, TimeSpan CpuTime, long MemBytes)> _previousSnapshot = new();
    private DateTime _lastSampleTime;
    private int _isUpdating;

    public ProcessData Data { get; } = new();
    public event Action? DataUpdated;

    public ProcessMonitorService()
    {
        _processorCount = Environment.ProcessorCount;
        _timer = new System.Timers.Timer(3000);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public void Start()
    {
        TakeSnapshot();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void TakeSnapshot()
    {
        _previousSnapshot = BuildSnapshot();
        _lastSampleTime = DateTime.UtcNow;
    }

    private static Dictionary<int, (string Name, TimeSpan CpuTime, long MemBytes)> BuildSnapshot()
    {
        var snapshot = new Dictionary<int, (string Name, TimeSpan CpuTime, long MemBytes)>();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    snapshot[proc.Id] = (proc.ProcessName, proc.TotalProcessorTime, proc.WorkingSet64);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }
        return snapshot;
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _isUpdating, 1, 0) != 0)
            return;

        try
        {
            Update();
            DataUpdated?.Invoke();
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _isUpdating, 0);
        }
    }

    private void Update()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastSampleTime).TotalMilliseconds;
        if (elapsed < 100) return;

        var currentSnapshot = BuildSnapshot();
        var totalPhysicalMb = GetTotalPhysicalMemoryMb();

        var grouped = new Dictionary<string, (double Cpu, double MemMb, int Count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (pid, (name, cpuTime, memBytes)) in currentSnapshot)
        {
            double cpuPercent = 0;
            if (_previousSnapshot.TryGetValue(pid, out var prev))
            {
                var cpuDelta = (cpuTime - prev.CpuTime).TotalMilliseconds;
                cpuPercent = (cpuDelta / elapsed / _processorCount) * 100.0;
                if (cpuPercent < 0) cpuPercent = 0;
                if (cpuPercent > 100) cpuPercent = 100;
            }

            double memMb = memBytes / (1024.0 * 1024.0);

            if (grouped.TryGetValue(name, out var existing))
                grouped[name] = (existing.Cpu + cpuPercent, existing.MemMb + memMb, existing.Count + 1);
            else
                grouped[name] = (cpuPercent, memMb, 1);
        }

        var top = grouped
            .Where(kv => !string.Equals(kv.Key, "Idle", StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(kv.Key, "System", StringComparison.OrdinalIgnoreCase)
                      && !string.Equals(kv.Key, _ownProcessName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value.Cpu)
            .Take(5)
            .Select(kv => new ProcessInfo
            {
                Name = kv.Key,
                CpuPercent = Math.Round(kv.Value.Cpu, 1),
                MemoryMb = Math.Round(kv.Value.MemMb, 0),
                MemoryPercent = totalPhysicalMb > 0 ? Math.Round((kv.Value.MemMb / totalPhysicalMb) * 100, 1) : 0,
                InstanceCount = kv.Value.Count
            })
            .ToList();

        // Stage the results -- ViewModel will apply to UI on dispatcher thread
        Data.LatestSnapshot = top;

        if (top.Count > 0)
        {
            Data.TopConsumerName = top[0].InstanceCount > 1
                ? $"{top[0].Name} ({top[0].InstanceCount})"
                : top[0].Name;
            Data.TopConsumerCpu = top[0].CpuPercent;
        }

        _previousSnapshot = currentSnapshot;
        _lastSampleTime = now;
    }

    private static double GetTotalPhysicalMemoryMb()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                try
                {
                    var kb = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                    return kb / 1024.0;
                }
                finally
                {
                    obj.Dispose();
                }
            }
        }
        catch { }
        return 0;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
