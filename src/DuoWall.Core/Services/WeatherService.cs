using System.Net.Http;
using System.Text.Json;

namespace DuoWall.Core.Services;

public sealed class WeatherSnapshot
{
    public string CityDisplay { get; init; } = "Unknown";
    public double Temperature { get; init; }
    public double ApparentTemperature { get; init; }
    public int HumidityPercent { get; init; }
    public double WindSpeedKmh { get; init; }
    public string ConditionText { get; init; } = "Clear";
    public string ConditionIcon { get; init; } = "☀";
    public string UnitSymbol { get; init; } = "°C";
    public string ProviderName { get; init; } = "Open-Meteo";
    public DateTime FetchedUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Provides live weather via keyless Open-Meteo by default, or optional user-supplied OpenWeatherMap API key.
/// </summary>
public sealed class WeatherService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public WeatherService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DuoWall/2.0 (+https://github.com/xarmad19/DuoWall)");
            _ownsHttp = true;
        }
    }

    public async Task<WeatherSnapshot> GetCurrentWeatherAsync(
        string city,
        bool useFahrenheit,
        string? customOpenWeatherApiKey = null,
        CancellationToken cancellationToken = default)
    {
        string provider = string.IsNullOrWhiteSpace(customOpenWeatherApiKey) ? "OpenMeteo" : "OpenWeatherMap";
        return await GetCurrentWeatherAsync(city, useFahrenheit, provider, customOpenWeatherApiKey, cancellationToken);
    }

    public async Task<WeatherSnapshot> GetCurrentWeatherAsync(
        string city,
        bool useFahrenheit,
        string? providerName,
        string? customApiKey,
        CancellationToken cancellationToken = default)
    {
        string cleanCity = string.IsNullOrWhiteSpace(city) ? "Lahore" : city.Trim();
        string cleanProvider = providerName?.Trim() ?? "OpenMeteo";
        string? cleanKey = string.IsNullOrWhiteSpace(customApiKey) ? null : customApiKey.Trim();

        if (cleanProvider.Contains("WeatherAPI", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(cleanKey))
                throw new InvalidOperationException("WeatherAPI.com requires an API key.");
            return await FetchWeatherApiComAsync(cleanCity, useFahrenheit, cleanKey, cancellationToken);
        }

        if (cleanProvider.Contains("OpenWeather", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(cleanKey))
                throw new InvalidOperationException("OpenWeatherMap requires an API key.");
            return await FetchOpenWeatherMapAsync(cleanCity, useFahrenheit, cleanKey, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(cleanKey) && string.IsNullOrWhiteSpace(providerName))
        {
            return await FetchOpenWeatherMapAsync(cleanCity, useFahrenheit, cleanKey, cancellationToken);
        }

        return await FetchOpenMeteoAsync(cleanCity, useFahrenheit, cancellationToken);
    }

    private async Task<WeatherSnapshot> FetchOpenMeteoAsync(string city, bool useFahrenheit, CancellationToken ct)
    {
        string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";
        string geoJson = await _http.GetStringAsync(geoUrl, ct);
        using var geoDoc = JsonDocument.Parse(geoJson);

        if (!geoDoc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new InvalidOperationException($"Location '{city}' not found.");

        var loc = results[0];
        double lat = loc.GetProperty("latitude").GetDouble();
        double lon = loc.GetProperty("longitude").GetDouble();
        string name = loc.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? city : city;
        string country = loc.TryGetProperty("country_code", out var cEl) ? cEl.GetString() ?? string.Empty : string.Empty;
        string display = string.IsNullOrWhiteSpace(country) ? name : $"{name}, {country}";

        string tempUnit = useFahrenheit ? "fahrenheit" : "celsius";
        string wxUrl = FormattableString.Invariant(
            $"https://api.open-meteo.com/v1/forecast?latitude={lat:F4}&longitude={lon:F4}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&temperature_unit={tempUnit}");

        string wxJson = await _http.GetStringAsync(wxUrl, ct);
        using var wxDoc = JsonDocument.Parse(wxJson);
        var cur = wxDoc.RootElement.GetProperty("current");

        int code = cur.TryGetProperty("weather_code", out var wc) ? wc.GetInt32() : 0;
        var (desc, icon) = MapWmoWeatherCode(code);

        return new WeatherSnapshot
        {
            CityDisplay = display,
            Temperature = cur.GetProperty("temperature_2m").GetDouble(),
            ApparentTemperature = cur.GetProperty("apparent_temperature").GetDouble(),
            HumidityPercent = cur.GetProperty("relative_humidity_2m").GetInt32(),
            WindSpeedKmh = cur.GetProperty("wind_speed_10m").GetDouble(),
            ConditionText = desc,
            ConditionIcon = icon,
            UnitSymbol = useFahrenheit ? "°F" : "°C",
            ProviderName = "Open-Meteo (Keyless)",
            FetchedUtc = DateTime.UtcNow
        };
    }

    private async Task<WeatherSnapshot> FetchOpenWeatherMapAsync(string city, bool useFahrenheit, string apiKey, CancellationToken ct)
    {
        string units = useFahrenheit ? "imperial" : "metric";
        string url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(city)}&units={units}&appid={Uri.EscapeDataString(apiKey)}";
        string json = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var main = root.GetProperty("main");
        var wx = root.GetProperty("weather")[0];
        double wind = root.TryGetProperty("wind", out var wEl) && wEl.TryGetProperty("speed", out var sp) ? sp.GetDouble() : 0;
        if (!useFahrenheit) wind *= 3.6;

        string cond = wx.TryGetProperty("main", out var mEl) ? mEl.GetString() ?? "Clear" : "Clear";
        return new WeatherSnapshot
        {
            CityDisplay = root.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? city : city,
            Temperature = main.GetProperty("temp").GetDouble(),
            ApparentTemperature = main.GetProperty("feels_like").GetDouble(),
            HumidityPercent = main.GetProperty("humidity").GetInt32(),
            WindSpeedKmh = wind,
            ConditionText = cond,
            ConditionIcon = cond.Contains("Rain", StringComparison.OrdinalIgnoreCase) ? "🌧"
                : (cond.Contains("Cloud", StringComparison.OrdinalIgnoreCase) ? "☁"
                : (cond.Contains("Snow", StringComparison.OrdinalIgnoreCase) ? "❄" : "☀")),
            UnitSymbol = useFahrenheit ? "°F" : "°C",
            ProviderName = "OpenWeatherMap",
            FetchedUtc = DateTime.UtcNow
        };
    }

    private async Task<WeatherSnapshot> FetchWeatherApiComAsync(string city, bool useFahrenheit, string apiKey, CancellationToken ct)
    {
        string url = $"https://api.weatherapi.com/v1/current.json?key={Uri.EscapeDataString(apiKey)}&q={Uri.EscapeDataString(city)}&aqi=no";
        string json = await _http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var loc = root.GetProperty("location");
        var cur = root.GetProperty("current");
        var condEl = cur.GetProperty("condition");

        string name = loc.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? city : city;
        string country = loc.TryGetProperty("country", out var cEl) ? cEl.GetString() ?? string.Empty : string.Empty;
        string display = string.IsNullOrWhiteSpace(country) ? name : $"{name}, {country}";
        string condText = condEl.TryGetProperty("text", out var ctEl) ? ctEl.GetString() ?? "Clear" : "Clear";

        double temp = useFahrenheit ? cur.GetProperty("temp_f").GetDouble() : cur.GetProperty("temp_c").GetDouble();
        double feels = useFahrenheit ? cur.GetProperty("feelslike_f").GetDouble() : cur.GetProperty("feelslike_c").GetDouble();

        return new WeatherSnapshot
        {
            CityDisplay = display,
            Temperature = temp,
            ApparentTemperature = feels,
            HumidityPercent = cur.GetProperty("humidity").GetInt32(),
            WindSpeedKmh = cur.GetProperty("wind_kph").GetDouble(),
            ConditionText = condText,
            ConditionIcon = condText.Contains("Rain", StringComparison.OrdinalIgnoreCase) ? "🌧"
                : (condText.Contains("Cloud", StringComparison.OrdinalIgnoreCase) || condText.Contains("Overcast", StringComparison.OrdinalIgnoreCase) ? "☁"
                : (condText.Contains("Snow", StringComparison.OrdinalIgnoreCase) ? "❄" : "☀")),
            UnitSymbol = useFahrenheit ? "°F" : "°C",
            ProviderName = "WeatherAPI.com",
            FetchedUtc = DateTime.UtcNow
        };
    }

    public static (string Description, string Icon) MapWmoWeatherCode(int code) => code switch
    {
        0 => ("Clear Sky", "☀"),
        1 or 2 => ("Partly Cloudy", "⛅"),
        3 => ("Overcast", "☁"),
        45 or 48 => ("Fog", "🌫"),
        51 or 53 or 55 => ("Drizzle", "🌦"),
        61 or 63 or 65 => ("Rain", "🌧"),
        71 or 73 or 75 or 77 => ("Snow", "❄"),
        80 or 81 or 82 => ("Rain Showers", "🌧"),
        95 or 96 or 99 => ("Thunderstorm", "⛈"),
        _ => ("Fair", "🌤")
    };

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
