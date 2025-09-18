using System.Text.Json.Serialization;

namespace Voxta.Modules.Aios.OpenWeather.Clients;

[Serializable]
public class OpenWeatherResponse
{
    [JsonPropertyName("main")]
    public required MainInfo Main { get; init; }

    [JsonPropertyName("weather")]
    public required List<WeatherDetail> Weather { get; init; }

    [JsonPropertyName("sys")]
    public required SysInfo Sys { get; init; }

    [JsonPropertyName("rain")]
    public Precipitation? Rain { get; init; }

    [JsonPropertyName("snow")]
    public Precipitation? Snow { get; init; }
}

[Serializable]
public class MainInfo
{
    [JsonPropertyName("temp")]
    public required double Temp { get; init; }

    [JsonPropertyName("feels_like")]
    public required double FeelsLike { get; init; }

    [JsonPropertyName("temp_min")]
    public required double TempMin { get; init; }

    [JsonPropertyName("temp_max")]
    public required double TempMax { get; init; }
}

[Serializable]
public class WeatherDetail
{
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("icon")]
    public required string Icon { get; init; }
}

[Serializable]
public class SysInfo
{
    [JsonPropertyName("country")]
    public required string Country { get; init; }
}

[Serializable]
public class Precipitation
{
    [JsonPropertyName("1h")]
    public required double OneHour { get; init; }
}
