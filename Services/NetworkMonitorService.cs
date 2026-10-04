using System.Net.NetworkInformation;
using System.Timers;
using TrayStats.Models;

namespace TrayStats.Services;

public sealed class NetworkMonitorService : IMonitorService
{
    private readonly System.Timers.Timer _timer;
    private readonly ProcessNetworkSampler _applicationSampler = new();
    private long _previousBytesReceived;
    private long _previousBytesSent;
    private long _sessionStartReceived;
    private long _sessionStartSent;
    private bool _initialized;
    private int _isUpdating;

    public NetData Data { get; } = new();
    public event Action? DataUpdated;

    public NetworkMonitorService()
    {
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public void Start()
    {
        InitializeBaseline();
        _applicationSampler.Start();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _applicationSampler.Stop();
    }

    private void InitializeBaseline()
    {
        var (recv, sent) = GetTotalBytes();
        _previousBytesReceived = recv;
        _previousBytesSent = sent;
        _sessionStartReceived = recv;
        _sessionStartSent = sent;
        _initialized = true;
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _isUpdating, 1, 0) != 0)
            return;

        try
        {
            if (!_initialized) return;

            var (recv, sent) = GetTotalBytes();

            long deltaRecv = recv - _previousBytesReceived;
            long deltaSent = sent - _previousBytesSent;

            if (deltaRecv < 0) deltaRecv = 0;
            if (deltaSent < 0) deltaSent = 0;

            Data.DownloadBytesPerSec = deltaRecv;
            Data.UploadBytesPerSec = deltaSent;
            Data.TotalDownloaded = recv - _sessionStartReceived;
            Data.TotalUploaded = sent - _sessionStartSent;

            _previousBytesReceived = recv;
            _previousBytesSent = sent;

            var sample = _applicationSampler.Sample();
            Data.LatestApplications = sample.Applications;
            Data.ApplicationStatus = sample.Status;

            DataUpdated?.Invoke();
        }
        catch
        {
            // Swallow network read errors
        }
        finally
        {
            Interlocked.Exchange(ref _isUpdating, 0);
        }
    }

    private static (long received, long sent) GetTotalBytes()
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
            return (0, 0);

        long totalReceived = 0;
        long totalSent = 0;

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel)
                continue;

            var stats = ni.GetIPv4Statistics();
            totalReceived += stats.BytesReceived;
            totalSent += stats.BytesSent;
        }

        return (totalReceived, totalSent);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _applicationSampler.Dispose();
    }
}
