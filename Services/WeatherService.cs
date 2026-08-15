using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Timers;
using TrayStats.Models;

namespace TrayStats.Services;

public sealed class WeatherService : IMonitorService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly System.Timers.Timer _timer;
    private int _isUpdating;
    private double _latitude;
    private double _longitude;
    private bool _locationResolved;

    public WeatherData Data { get; } = new();
    public event Action? DataUpdated;

    public WeatherService()
    {
        _timer = new System.Timers.Timer(15 * 60 * 1000); // 15 minutes
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public async void Start()
    {
        try
        {
            await ResolveLocationAsync();
            await FetchWeatherAsync();
        }
        catch { }
        finally
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _isUpdating, 1, 0) != 0)
            return;

        try
        {
            await FetchWeatherAsync();
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _isUpdating, 0);
        }
    }

    private async Task ResolveLocationAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("http://ip-api.com/json/");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("lat", out var lat) &&
                root.TryGetProperty("lon", out var lon))
            {
                _latitude = lat.GetDouble();
                _longitude = lon.GetDouble();
                _locationResolved = true;

                if (root.TryGetProperty("city", out var city))
                    Data.City = city.GetString() ?? "Unknown";
            }
        }
        catch
        {
            Data.City = "Location unavailable";
        }
    }

    private async Task FetchWeatherAsync()
    {
        if (!_locationResolved) return;

        try
        {
            var lat = _latitude.ToString(CultureInfo.InvariantCulture);
            var lon = _longitude.ToString(CultureInfo.InvariantCulture);

            var url = $"https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={lat}&longitude={lon}" +
                      $"&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code" +
                      $"&daily=temperature_2m_max,temperature_2m_min,weather_code,precipitation_probability_max" +
                      $"&temperature_unit=celsius&wind_speed_unit=kmh&forecast_days=4&timezone=auto";

            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Current conditions
            if (root.TryGetProperty("current", out var current))
            {
                Data.Temperature = GetFloat(current, "temperature_2m");
                Data.FeelsLike = GetFloat(current, "apparent_temperature");
                Data.Humidity = (int)GetFloat(current, "relative_humidity_2m");
                Data.WindSpeed = GetFloat(current, "wind_speed_10m");

                int code = (int)GetFloat(current, "weather_code");
                var (condition, icon, color) = WmoWeatherCodes.Decode(code);
                Data.WeatherCondition = condition;
                Data.WeatherIcon = icon;
                Data.WeatherIconColor = color;
            }

            // Daily forecast (skip today = index 0, take next 3 days)
            if (root.TryGetProperty("daily", out var daily))
            {
                var times = daily.GetProperty("time");
                var maxTemps = daily.GetProperty("temperature_2m_max");
                var minTemps = daily.GetProperty("temperature_2m_min");
                var codes = daily.GetProperty("weather_code");
                var precip = daily.TryGetProperty("precipitation_probability_max", out var p) ? p : (JsonElement?)null;

                var forecast = new List<DailyForecast>();

                int count = Math.Min(times.GetArrayLength(), 4);
                for (int i = 1; i < count; i++) // skip today
                {
                    var dateStr = times[i].GetString();
                    var dayName = DateTime.TryParse(dateStr, out var dt) ? dt.ToString("ddd") : dateStr ?? "";

                    int wmoCode = codes[i].GetInt32();
                    var (cond, ico, clr) = WmoWeatherCodes.Decode(wmoCode);

                    forecast.Add(new DailyForecast
                    {
                        DayName = dayName,
                        High = (float)maxTemps[i].GetDouble(),
                        Low = (float)minTemps[i].GetDouble(),
                        WeatherCondition = cond,
                        WeatherIcon = ico,
                        WeatherIconColor = clr,
                        PrecipitationProbability = precip.HasValue ? precip.Value[i].GetInt32() : 0
                    });
                }

                Data.LatestForecast = forecast;
            }

            DataUpdated?.Invoke();
        }
        catch
        {
            // Swallow weather fetch errors silently
        }
    }

    private static float GetFloat(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val))
        {
            return val.ValueKind switch
            {
                JsonValueKind.Number => (float)val.GetDouble(),
                _ => 0
            };
        }
        return 0;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
