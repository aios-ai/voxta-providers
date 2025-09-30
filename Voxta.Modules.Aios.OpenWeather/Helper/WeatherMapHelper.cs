public static class WeatherMapHelper
{
    private static readonly Dictionary<string, string> LayerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Temperature
        { "temp", "temp_new" },
        { "temperature", "temp_new" },
        { "heat", "temp_new" },

        // Clouds
        { "clouds", "clouds_new" },
        { "cloud", "clouds_new" },
        { "overcast", "clouds_new" },

        // Precipitation
        { "precipitation", "precipitation_new" },
        { "precip", "precipitation_new" },
        { "rain", "precipitation_new" },
        { "snow", "precipitation_new" },
        { "drizzle", "precipitation_new" },

        // Pressure
        { "pressure", "pressure_new" },
        { "barometric", "pressure_new" },
        { "barometer", "pressure_new" },

        // Wind
        { "wind", "wind_new" },
        { "winds", "wind_new" },
        { "breeze", "wind_new" },

        // Already-normalized values
        { "temp_new", "temp_new" },
        { "clouds_new", "clouds_new" },
        { "precipitation_new", "precipitation_new" },
        { "pressure_new", "pressure_new" },
        { "wind_new", "wind_new" }
    };
    
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "clouds_new", "clouds" },
        { "precipitation_new", "precipitation" },
        { "pressure_new", "pressure" },
        { "wind_new", "wind" },
        { "temp_new", "temperature" }
    };
    
    public static string NormalizeLayer(string? requestedLayer)
    {
        if (string.IsNullOrWhiteSpace(requestedLayer))
            return "temp_new";

        var key = requestedLayer.Trim();

        return LayerMap.TryGetValue(key, out var mapped)
            ? mapped
            : "temp_new";
    }
    
    public static string ToDisplayName(string normalizedLayer)
    {
        return DisplayNames.TryGetValue(normalizedLayer, out var display)
            ? display
            : normalizedLayer;
    }
    
    public static readonly Dictionary<string, string> ContinentMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // North America
        { "NA", "NA" },
        { "NorthAmerica", "NA" },
        { "North America", "NA" },

        // South America
        { "SA", "SA" },
        { "SouthAmerica", "SA" },
        { "South America", "SA" },

        // Europe
        { "EU", "EU" },
        { "Europe", "EU" },

        // Africa
        { "AF", "AF" },
        { "Africa", "AF" },

        // Asia
        { "AS", "AS" },
        { "Asia", "AS" },

        // Oceania
        { "OC", "OC" },
        { "Oceania", "OC" },
        { "Australia", "OC" },

        // Antarctica
        { "AN", "AN" },
        { "Antarctica", "AN" }
    };
    
}