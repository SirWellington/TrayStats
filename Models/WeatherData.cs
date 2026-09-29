using CommunityToolkit.Mvvm.ComponentModel;

namespace TrayStats.Models;

public partial class WeatherData : ObservableObject
{
    [ObservableProperty] private string _city = "Locating...";
    [ObservableProperty] private float _temperature;
    [ObservableProperty] private float _feelsLike;
    [ObservableProperty] private int _humidity;
    [ObservableProperty] private float _windSpeed;
    [ObservableProperty] private string _weatherCondition = "--";
    [ObservableProperty] private string _weatherIcon = "\u2601"; // default cloud icon
    [ObservableProperty] private string _weatherIconColor = "#FF708398";
    [ObservableProperty] private int _precipitationProbability;

    public List<DailyForecast> LatestForecast { get; set; } = new();
}

public partial class DailyForecast : ObservableObject
{
    [ObservableProperty] private string _dayName = "";
    [ObservableProperty] private float _high;
    [ObservableProperty] private float _low;
    [ObservableProperty] private string _weatherCondition = "";
    [ObservableProperty] private string _weatherIcon = "";
    [ObservableProperty] private string _weatherIconColor = "#FF708398";
    [ObservableProperty] private int _precipitationProbability;
}

public static class WmoWeatherCodes
{
    private const string Yellow  = "#FFFFC107";
    private const string White   = "#FF708398";
    private const string Blue    = "#FF64B5F6";
    private const string IceBlue = "#FFB3E5FC";
    private const string Purple  = "#FFCE93D8";

    public static (string Condition, string Icon, string Color) Decode(int code)
    {
        return code switch
        {
            0 => ("Clear", "\u2600", Yellow),                                   // ☀
            1 => ("Mostly Clear", "\u26C5", Yellow),                            // ⛅
            2 => ("Partly Cloudy", "\u26C5", Yellow),                           // ⛅
            3 => ("Overcast", "\u2601", White),                                 // ☁
            45 or 48 => ("Foggy", "\u2601", White),                             // ☁
            51 or 53 or 55 => ("Drizzle", "\uD83C\uDF27", Blue),               // 🌧
            56 or 57 => ("Freezing Drizzle", "\uD83C\uDF27", IceBlue),         // 🌧
            61 or 63 or 65 => ("Rain", "\uD83C\uDF27", Blue),                  // 🌧
            66 or 67 => ("Freezing Rain", "\uD83C\uDF27", IceBlue),            // 🌧
            71 or 73 or 75 => ("Snow", "\u2744", IceBlue),                     // ❄
            77 => ("Snow Grains", "\u2744", IceBlue),                           // ❄
            80 or 81 or 82 => ("Showers", "\uD83C\uDF27", Blue),              // 🌧
            85 or 86 => ("Snow Showers", "\u2744", IceBlue),                   // ❄
            95 => ("Thunderstorm", "\u26C8", Purple),                           // ⛈
            96 or 99 => ("Thunderstorm w/ Hail", "\u26C8", Purple),            // ⛈
            _ => ("Unknown", "\u2601", White)                                   // ☁
        };
    }
}
