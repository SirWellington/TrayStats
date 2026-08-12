using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrayStats.Models;

public partial class GpuDisplayModel : ObservableObject
{
    private readonly List<double> _sparklineValues = new();

    public GpuData Data { get; }
    public int Index { get; }
    public string DisplayName { get; }
    public SolidColorBrush StrokeBrush { get; }
    public List<double> SparklineValues => _sparklineValues;

    [ObservableProperty]
    private bool _isExpanded;

    public GpuDisplayModel(GpuData data, int index, string colorHex)
    {
        Data = data;
        Index = index;
        DisplayName = $"GPU {index + 1}";
        StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 
            Convert.ToByte(colorHex.Substring(1, 2), 16),
            Convert.ToByte(colorHex.Substring(3, 2), 16),
            Convert.ToByte(colorHex.Substring(5, 2), 16)));

        for (int i = 0; i < 60; i++)
            _sparklineValues.Add(0);
    }

    public void PushValue(double value)
    {
        if (_sparklineValues.Count >= 60)
            _sparklineValues.RemoveAt(0);
        _sparklineValues.Add(value);
    }
}
