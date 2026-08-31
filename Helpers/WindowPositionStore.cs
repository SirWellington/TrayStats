using System.IO;
using System.Text.Json;

namespace TrayStats.Helpers;

/// <summary>
/// A saved window position (top-left corner, in WPF device-independent pixels).
/// </summary>
public class WindowPosition
{
    public double Left { get; set; }
    public double Top { get; set; }
}

/// <summary>
/// Persists the dashboard window position to %AppData%\TrayStats\window.json so the
/// window reopens where the user last dragged it.
/// </summary>
public static class WindowPositionStore
{
    private static string StorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrayStats");

    private static string FilePath => Path.Combine(StorageDirectory, "window.json");

    public static WindowPosition? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<WindowPosition>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WindowPosition position)
    {
        try
        {
            System.IO.Directory.CreateDirectory(StorageDirectory);
            var json = JsonSerializer.Serialize(position);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Saving is best-effort; never let it break the app.
        }
    }
}
