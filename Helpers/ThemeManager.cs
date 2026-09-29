using Microsoft.Win32;
using System.Windows;

namespace TrayStats.Helpers;

/// <summary>
/// Applies the dashboard palette selected by the user, or Windows' app color preference.
/// </summary>
public sealed class ThemeManager
{
    private ResourceDictionary? _activePalette;
    private bool? _isLight;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public void SetMode(ThemeMode mode)
    {
        Mode = mode;
        Refresh();
    }

    public void Refresh()
    {
        bool isLight = Mode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => IsWindowsAppThemeLight()
        };

        if (_isLight == isLight)
            return;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/TrayStats;component/Themes/{(isLight ? "Light" : "Dark")}.xaml")
        };

        // The startup dictionary is the dark palette; subsequent changes replace it.
        if (_activePalette == null)
            dictionaries[0] = palette;
        else
            dictionaries[dictionaries.IndexOf(_activePalette)] = palette;

        _activePalette = palette;
        _isLight = isLight;
    }

    private static bool IsWindowsAppThemeLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // Windows treats a missing value as light. SystemUsesLightTheme affects
            // the taskbar; AppsUseLightTheme controls application appearance.
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }
}
