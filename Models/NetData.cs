using CommunityToolkit.Mvvm.ComponentModel;

namespace TrayStats.Models;

public sealed class NetApplicationData
{
    public string Name { get; init; } = string.Empty;
    public double DownloadBytesPerSec { get; init; }
    public double UploadBytesPerSec { get; init; }
    public string DownloadFormatted => NetData.FormatSpeed(DownloadBytesPerSec);
    public string UploadFormatted => NetData.FormatSpeed(UploadBytesPerSec);
}

public partial class NetData : ObservableObject
{
    [ObservableProperty] private double _downloadBytesPerSec;
    [ObservableProperty] private double _uploadBytesPerSec;
    [ObservableProperty] private long _totalDownloaded;
    [ObservableProperty] private long _totalUploaded;

    public List<NetApplicationData> LatestApplications { get; set; } = new();
    [ObservableProperty] private string _applicationStatus = "Collecting application network activity…";

    public string DownloadFormatted => FormatSpeed(DownloadBytesPerSec);
    public string UploadFormatted => FormatSpeed(UploadBytesPerSec);
    public string TotalDownloadedFormatted => FormatBytes(TotalDownloaded);
    public string TotalUploadedFormatted => FormatBytes(TotalUploaded);

    partial void OnDownloadBytesPerSecChanged(double value)
    {
        OnPropertyChanged(nameof(DownloadFormatted));
    }

    partial void OnUploadBytesPerSecChanged(double value)
    {
        OnPropertyChanged(nameof(UploadFormatted));
    }

    partial void OnTotalDownloadedChanged(long value)
    {
        OnPropertyChanged(nameof(TotalDownloadedFormatted));
    }

    partial void OnTotalUploadedChanged(long value)
    {
        OnPropertyChanged(nameof(TotalUploadedFormatted));
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024)
            return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024)
            return $"{bytesPerSec / 1024:F1} KB/s";
        if (bytesPerSec < 1024 * 1024 * 1024)
            return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSec / (1024 * 1024 * 1024):F2} GB/s";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
