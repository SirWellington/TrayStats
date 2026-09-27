using System.Runtime.InteropServices;

namespace TrayStats.Services;

/// <summary>Samples the Windows aggregate CPU counter across all logical processors.</summary>
internal sealed class WindowsCpuUsageSampler : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private IntPtr _query;
    private IntPtr _counter;

    public WindowsCpuUsageSampler()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0 ||
                PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Time", IntPtr.Zero, out _counter) != 0 ||
                PdhCollectQueryData(_query) != 0)
            {
                Dispose();
            }
        }
        catch (Exception)
        {
            Dispose();
        }
    }

    public bool TrySample(out float usage)
    {
        usage = 0;
        if (_query == IntPtr.Zero || _counter == IntPtr.Zero || PdhCollectQueryData(_query) != 0 ||
            PdhGetFormattedCounterValue(_counter, PdhFmtDouble, out _, out var value) != 0 ||
            value.Status is not (0 or 1) || !double.IsFinite(value.Value))
            return false;

        usage = (float)Math.Clamp(value.Value, 0, 100);
        return true;
    }

    public void Dispose()
    {
        if (_query == IntPtr.Zero) return;
        PdhCloseQuery(_query);
        _query = IntPtr.Zero;
        _counter = IntPtr.Zero;
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
