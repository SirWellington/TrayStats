using System.Runtime.InteropServices;

namespace TrayStats.Services;

/// <summary>Samples utilization and parking state for each logical processor using Windows performance counters.</summary>
internal sealed class WindowsCpuPerfSampler : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;

    private IntPtr _query;
    private readonly IntPtr[] _usageCounters;
    private readonly IntPtr[] _parkingCounters;

    public WindowsCpuPerfSampler()
    {
        int count = Environment.ProcessorCount;
        _usageCounters = new IntPtr[count];
        _parkingCounters = new IntPtr[count];

        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
            {
                Dispose();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                // Processor Information uses (group,logical processor), not (logical processor).
                string instance = $"{i / 64},{i % 64}";
                if (PdhAddEnglishCounter(_query, $@"\Processor Information({instance})\% Processor Time",
                        IntPtr.Zero, out _usageCounters[i]) != 0)
                {
                    Dispose();
                    return;
                }

                // Older Windows versions may lack this counter; usage still works without it.
                if (PdhAddEnglishCounter(_query, $@"\Processor Information({instance})\Parking Status",
                        IntPtr.Zero, out _parkingCounters[i]) != 0)
                    _parkingCounters[i] = IntPtr.Zero;
            }

            if (PdhCollectQueryData(_query) != 0)
                Dispose();
        }
        catch
        {
            Dispose();
        }
    }

    public bool TrySample(float[] usage, bool[] parked)
    {
        if (_query == IntPtr.Zero || usage.Length != _usageCounters.Length || parked.Length != usage.Length ||
            PdhCollectQueryData(_query) != 0)
            return false;

        for (int i = 0; i < usage.Length; i++)
        {
            if (PdhGetFormattedCounterValue(_usageCounters[i], PdhFmtDouble, out _, out var value) != 0 ||
                value.Status is not (0 or 1) || !double.IsFinite(value.Value))
                return false;

            usage[i] = (float)Math.Clamp(value.Value, 0, 100);
            parked[i] = _parkingCounters[i] != IntPtr.Zero &&
                PdhGetFormattedCounterValue(_parkingCounters[i], PdhFmtDouble, out _, out var parking) == 0 &&
                parking.Status is 0 or 1 && double.IsFinite(parking.Value) && parking.Value >= 0.5;
        }

        return true;
    }

    public void Dispose()
    {
        if (_query == IntPtr.Zero) return;
        PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFormattedCounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type,
        out PdhFormattedCounterValue value);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);
}
