using System.IO;
using System.Timers;
using TrayStats.Models;

namespace TrayStats.Services;

public sealed class DiskMonitorService : IMonitorService
{
    private readonly System.Timers.Timer _timer;
    private readonly HardwareContext _hwContext;
    private readonly object _dataLock = new();

    public DiskData Data { get; } = new();
    public event Action? DataUpdated;

    public DiskMonitorService(HardwareContext hwContext)
    {
        _hwContext = hwContext;

        _timer = new System.Timers.Timer(5000);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public void Start()
    {
        Update();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void SetInterval(int ms)
    {
        _timer.Interval = ms;
    }

    public IReadOnlyList<DriveData> GetDrivesSnapshot()
    {
        lock (_dataLock)
        {
            return Data.Drives.Select(drive => new DriveData
            {
                Name = drive.Name,
                Label = drive.Label,
                TotalGb = drive.TotalGb,
                UsedGb = drive.UsedGb,
                FreeGb = drive.FreeGb,
                UsagePercent = drive.UsagePercent,
                Temperature = drive.Temperature,
                ReadRate = drive.ReadRate,
                WriteRate = drive.WriteRate
            }).ToArray();
        }
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            Update();
            DataUpdated?.Invoke();
        }
        catch
        {
            // Swallow errors
        }
    }

    private void Update()
    {
        lock (_dataLock)
            UpdateCore();
    }

    private void UpdateCore()
    {
        var storageHwData = _hwContext.GetStorageData();
        var drives = DriveInfo.GetDrives();

        Data.Drives.Clear();

        double totalSize = 0;
        double totalUsed = 0;

        foreach (var drive in drives)
        {
            if (!drive.IsReady) continue;
            if (drive.DriveType != DriveType.Fixed) continue;

            double totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
            double freeGb = drive.TotalFreeSpace / (1024.0 * 1024 * 1024);
            double usedGb = totalGb - freeGb;
            double usagePercent = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0;

            totalSize += totalGb;
            totalUsed += usedGb;

            var driveData = new DriveData
            {
                Name = drive.Name.TrimEnd('\\'),
                Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : drive.VolumeLabel,
                TotalGb = Math.Round(totalGb, 1),
                UsedGb = Math.Round(usedGb, 1),
                FreeGb = Math.Round(freeGb, 1),
                UsagePercent = Math.Round(usagePercent, 1)
            };

            var matchingHw = storageHwData.FirstOrDefault(s =>
                s.Name.Contains(drive.Name.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

            if (matchingHw != null)
            {
                driveData.Temperature = matchingHw.Temperature;
                driveData.ReadRate = matchingHw.ReadRate;
                driveData.WriteRate = matchingHw.WriteRate;
            }
            else if (storageHwData.Count > 0)
            {
                // Try to match by index if only one storage device
                int driveIndex = Data.Drives.Count;
                if (driveIndex < storageHwData.Count)
                {
                    driveData.Temperature = storageHwData[driveIndex].Temperature;
                    driveData.ReadRate = storageHwData[driveIndex].ReadRate;
                    driveData.WriteRate = storageHwData[driveIndex].WriteRate;
                }
            }

            Data.Drives.Add(driveData);
        }

        Data.TotalUsagePercent = totalSize > 0 ? Math.Round((totalUsed / totalSize) * 100.0, 1) : 0;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
