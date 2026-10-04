using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using TrayStats.Models;

namespace TrayStats.Services;

// Kernel network events report actual TCP/UDP bytes and the owning process,
// unlike process I/O counters, which also include disk and pipe activity.
internal sealed class ProcessNetworkSampler : IDisposable
{
    private readonly object _lifecycleLock = new();
    private readonly object _trafficLock = new();
    private Dictionary<int, (long Received, long Sent)> _traffic = new();
    private TraceEventSession? _session;
    private Task? _reader;
    private long _lastSample;
    private volatile string _status = "Collecting application network activity…";

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_session != null) return;

            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                _status = "Use Restart as Admin to see application network usage.";
                return;
            }

            lock (_trafficLock)
            {
                _traffic.Clear();
                _lastSample = Stopwatch.GetTimestamp();
            }

            _status = "Collecting application network activity…";
            try
            {
                var session = new TraceEventSession($"TrayStats-Network-{Environment.ProcessId}");
                _session = session;
                // Accessing Source starts the session. Kernel providers must be
                // enabled first; they cannot be added to an already-started session.
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
                var source = session.Source;
                source.Kernel.TcpIpRecv += e => Record(e.ProcessID, e.size, false);
                source.Kernel.TcpIpSend += e => Record(e.ProcessID, e.size, true);
                source.Kernel.TcpIpRecvIPV6 += e => Record(e.ProcessID, e.size, false);
                source.Kernel.TcpIpSendIPV6 += e => Record(e.ProcessID, e.size, true);
                source.Kernel.UdpIpRecv += e => Record(e.ProcessID, e.size, false);
                source.Kernel.UdpIpSend += e => Record(e.ProcessID, e.size, true);
                source.Kernel.UdpIpRecvIPV6 += e => Record(e.ProcessID, e.size, false);
                source.Kernel.UdpIpSendIPV6 += e => Record(e.ProcessID, e.size, true);
                _reader = Task.Run(() =>
                {
                    try { source.Process(); }
                    catch (Exception ex)
                    {
                        _status = $"Application network tracking stopped: {ex.Message}";
                        Debug.WriteLine($"Network event reader: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _session?.Dispose();
                _session = null;
                _status = $"Application network tracking could not start: {ex.Message}";
                Debug.WriteLine($"Network event session: {ex.Message}");
            }
        }
    }

    private void Record(int processId, int bytes, bool upload)
    {
        if (processId < 0 || bytes <= 0) return;
        lock (_trafficLock)
        {
            _traffic.TryGetValue(processId, out var current);
            _traffic[processId] = upload
                ? (current.Received, current.Sent + bytes)
                : (current.Received + bytes, current.Sent);
        }
    }

    public (List<NetApplicationData> Applications, string Status) Sample()
    {
        Dictionary<int, (long Received, long Sent)> traffic;
        double elapsed;
        lock (_trafficLock)
        {
            var now = Stopwatch.GetTimestamp();
            elapsed = Stopwatch.GetElapsedTime(_lastSample, now).TotalSeconds;
            _lastSample = now;
            traffic = _traffic;
            _traffic = new();
        }

        var grouped = new Dictionary<string, (long Received, long Sent)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pid, bytes) in traffic)
        {
            string name;
            try
            {
                using var process = Process.GetProcessById(pid);
                name = process.ProcessName;
            }
            catch
            {
                // Keep traffic from processes that exited during the sample.
                name = $"Process {pid}";
            }

            grouped.TryGetValue(name, out var previous);
            grouped[name] = (previous.Received + bytes.Received, previous.Sent + bytes.Sent);
        }

        var applications = grouped
            .OrderByDescending(p => p.Value.Received + p.Value.Sent)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(p => new NetApplicationData
            {
                Name = p.Key,
                DownloadBytesPerSec = p.Value.Received / Math.Max(elapsed, 0.001),
                UploadBytesPerSec = p.Value.Sent / Math.Max(elapsed, 0.001)
            })
            .ToList();

        var status = _status;
        if (_session != null && _reader is { IsCompleted: false })
            status = applications.Count == 0 ? "No application network activity." : string.Empty;
        return (applications, status);
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            _session?.Dispose();
            _session = null;
            _reader?.Wait(TimeSpan.FromSeconds(2));
            _reader = null;
            lock (_trafficLock) _traffic.Clear();
        }
    }

    public void Dispose() => Stop();
}
