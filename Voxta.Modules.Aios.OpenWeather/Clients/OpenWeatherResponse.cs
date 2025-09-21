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

public class GeoResult
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    // Holds names in different languages, keyed by language code
    [JsonPropertyName("local_names")]
    public Dictionary<string, string>? LocalNames { get; init; }

    [JsonPropertyName("lat")]
    public required double Lat { get; init; }

    [JsonPropertyName("lon")]
    public required double Lon { get; init; }

    [JsonPropertyName("country")]
    public required string Country { get; init; }

    // Optional, only present for some countries
    [JsonPropertyName("state")]
    public string? State { get; init; }
}

public class OpenWeatherForecastResponse
{
    [JsonPropertyName("cod")]
    public string? Cod { get; init; }

    [JsonPropertyName("message")]
    public int? Message { get; init; }

    [JsonPropertyName("cnt")]
    public int? Count { get; init; }

    [JsonPropertyName("list")]
    public required List<ForecastItem> List { get; init; }

    [JsonPropertyName("city")]
    public required ForecastCity City { get; init; }
}

public class ForecastItem
{
    [JsonPropertyName("dt")]
    public long Dt { get; init; }

    [JsonPropertyName("main")]
    public required MainInfo Main { get; init; }

    [JsonPropertyName("weather")]
    public required List<WeatherDetail> Weather { get; init; }

    [JsonPropertyName("clouds")]
    public required Clouds Clouds { get; init; }

    [JsonPropertyName("wind")]
    public required Wind Wind { get; init; }

    [JsonPropertyName("visibility")]
    public int? Visibility { get; init; }

    [JsonPropertyName("pop")]
    public double? Pop { get; init; }

    [JsonPropertyName("rain")]
    public Precipitation? Rain { get; init; }

    [JsonPropertyName("snow")]
    public Precipitation? Snow { get; init; }

    [JsonPropertyName("sys")]
    public required ForecastSys Sys { get; init; }

    [JsonPropertyName("dt_txt")]
    public string? DtTxt { get; init; }
}


public class ForecastCity
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("coord")]
    public required Coord Coord { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("population")]
    public int? Population { get; init; }

    [JsonPropertyName("timezone")]
    public int? Timezone { get; init; }

    [JsonPropertyName("sunrise")]
    public long? Sunrise { get; init; }

    [JsonPropertyName("sunset")]
    public long? Sunset { get; init; }
}

public class Clouds
{
    [JsonPropertyName("all")]
    public int All { get; init; }
}

public class Coord
{
    [JsonPropertyName("all")]
    public int All { get; init; }
}

public class Wind
{
    [JsonPropertyName("speed")]
    public double Speed { get; init; }

    [JsonPropertyName("deg")]
    public int Deg { get; init; }

    [JsonPropertyName("gust")]
    public double? Gust { get; init; }
}

public class ForecastSys
{
    [JsonPropertyName("pod")]
    public string? Pod { get; init; }
}

public class OpenWeatherAirPollutionResponse
{
    public List<AirPollutionData> List { get; set; } = new();
}

public class AirPollutionData
{
    public MainPollution Main { get; set; } = new();
    public Components Components { get; set; } = new();
    public long Dt { get; set; }
}

public class MainPollution
{
    public int Aqi { get; set; }
}

public class Components
{
    public double Co { get; set; }
    public double No { get; set; }
    public double No2 { get; set; }
    public double O3 { get; set; }
    public double So2 { get; set; }
    public double Pm2_5 { get; set; }
    public double Pm10 { get; set; }
    public double Nh3 { get; set; }
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
    public double OneHour { get; init; }
    
    [JsonPropertyName("3h")]
    public double? ThreeHours { get; init; }
}