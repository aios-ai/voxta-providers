using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Voxta.Modules.Aios.OpenWeather.ChatAugmentations;
using Voxta.Modules.Aios.OpenWeather.Helper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Voxta.Modules.Aios.OpenWeather.Clients;

public interface IOpenWeatherClientFactory
{
    IOpenWeatherClient CreateClient(string apiKey);
}

public class OpenWeatherClientFactory(
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory
) : IOpenWeatherClientFactory
{
    public IOpenWeatherClient CreateClient(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("API key is missing. Open the config file and set the variable.");
        var httpClient = httpClientFactory.CreateClient(VoxtaModule.ServiceName);
        return new OpenWeatherClient(httpClient, apiKey, loggerFactory.CreateLogger<OpenWeatherClient>());
    }
}

public interface IOpenWeatherClient
{
    Task<OpenWeatherResponse?> FetchWeatherData(string location, string? units, CancellationToken cancellationToken);
    Task<OpenWeatherForecastResponse?> FetchForecastData(string location, string? units, CancellationToken cancellationToken);
    Task<OpenWeatherAirPollutionResponse?> FetchAirPollutionData(string location, CancellationToken cancellationToken);
    Task<OpenWeatherAirPollutionResponse?> FetchAirPollutionForecastData(string location, CancellationToken cancellationToken);
    Task<byte[]?> FetchWeatherMapAsync(
        (OpenWeatherChatAugmentationsServiceInstance.MapTargetType Type, string Identifier) target,
        string layer,
        CancellationToken cancellationToken);
}

public class OpenWeatherClient(
    HttpClient httpClient,
    string apiKey,
    ILogger<OpenWeatherClient> logger
) : IOpenWeatherClient
{
    public async Task<OpenWeatherResponse?> FetchWeatherData(
        string location, string? units, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Resolving location '{Location}'...", location);
            var geo = await ResolveLocationAsync(location, cancellationToken);

            if (geo == null)
            {
                logger.LogWarning("Could not resolve location '{Location}'", location);
                return null;
            }

            logger.LogInformation("Fetching weather for {Name}, {Country} ({Lat}, {Lon})",
                geo.Name, geo.Country, geo.Lat, geo.Lon);

            // https://openweathermap.org/current#geo
            var weatherUrl = $"http://api.openweathermap.org/data/2.5/weather?lat={geo.Lat}&lon={geo.Lon}&appid={apiKey}&units={units}";
            var response = await httpClient.GetAsync(weatherUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch weather data. Status Code: {StatusCode}, Body: {Body}",
                    response.StatusCode, body);
                return null;
            }

            var content = await response.Content.ReadFromJsonAsync<OpenWeatherResponse>(cancellationToken);
            if (content == null)
            {
                logger.LogError("Failed to parse weather data for {Location}", location);
                return null;
            }

            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching weather data for {Location}", location);
            return null;
        }
    }
    
    public async Task<OpenWeatherForecastResponse?> FetchForecastData(
        string location,
        string? units,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Resolving location '{Location}' for forecast...", location);
            var geo = await ResolveLocationAsync(location, cancellationToken);

            if (geo == null)
            {
                logger.LogWarning("Could not resolve location '{Location}' for forecast", location);
                return null;
            }

            logger.LogInformation("Fetching forecast for {Name}, {Country} ({Lat}, {Lon})",
                geo.Name, geo.Country, geo.Lat, geo.Lon);

            // https://openweathermap.org/forecast5
            var forecastUrl = $"http://api.openweathermap.org/data/2.5/forecast?lat={geo.Lat}&lon={geo.Lon}&appid={apiKey}&units={units}";
            var response = await httpClient.GetAsync(forecastUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch forecast data. Status Code: {StatusCode}, Body: {Body}",
                    response.StatusCode, body);
                return null;
            }

            var content = await response.Content.ReadFromJsonAsync<OpenWeatherForecastResponse>(cancellationToken);
            if (content == null)
            {
                logger.LogError("Failed to parse forecast data for {Location}", location);
                return null;
            }

            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching forecast data for {Location}", location);
            return null;
        }
    }
    
    public async Task<OpenWeatherAirPollutionResponse?> FetchAirPollutionData(
        string location,
        CancellationToken cancellationToken)
    {
        try
        {
            var geo = await ResolveLocationAsync(location, cancellationToken);
            if (geo == null)
            {
                logger.LogWarning("Could not resolve location '{Location}' for air pollution data", location);
                return null;
            }

            // https://openweathermap.org/api/air-pollution#current
            var url = $"http://api.openweathermap.org/data/2.5/air_pollution?lat={geo.Lat}&lon={geo.Lon}&appid={apiKey}";
            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch air pollution data. Status Code: {StatusCode}, Body: {Body}",
                    response.StatusCode, body);
                return null;
            }

            var content = await response.Content.ReadFromJsonAsync<OpenWeatherAirPollutionResponse>(cancellationToken);
            if (content == null)
            {
                logger.LogError("Failed to parse air pollution data for {Location}", location);
                return null;
            }

            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching air pollution data for {Location}", location);
            return null;
        }
    }
    
    public async Task<OpenWeatherAirPollutionResponse?> FetchAirPollutionForecastData(
        string location,
        CancellationToken cancellationToken)
    {
        try
        {
            var geo = await ResolveLocationAsync(location, cancellationToken);
            if (geo == null)
            {
                logger.LogWarning("Could not resolve location '{Location}' for air pollution forecast", location);
                return null;
            }

            // https://openweathermap.org/api/air-pollution#forecast
            var url = $"http://api.openweathermap.org/data/2.5/air_pollution/forecast?lat={geo.Lat}&lon={geo.Lon}&appid={apiKey}";
            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch air pollution forecast data. Status Code: {StatusCode}, Body: {Body}",
                    response.StatusCode, body);
                return null;
            }

            var content = await response.Content.ReadFromJsonAsync<OpenWeatherAirPollutionResponse>(cancellationToken);
            if (content == null)
            {
                logger.LogError("Failed to parse air pollution forecast data for {Location}", location);
                return null;
            }

            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching air pollution forecast data for {Location}", location);
            return null;
        }
    }
    
    public async Task<byte[]?> FetchWeatherMapAsync(
        (OpenWeatherChatAugmentationsServiceInstance.MapTargetType Type, string Identifier) target,
        string layer,
        CancellationToken cancellationToken)
    {
        var tileFetcher = new TileFetcher(httpClient);
        
        int zoom;
        int gridSize;
        int tileSize = 256;
        int centerX = 0, centerY = 0;
        (double Lat, double Lon) centroid;

        switch (target.Type)
        {
            case OpenWeatherChatAugmentationsServiceInstance.MapTargetType.Global:
                zoom = 2;
                gridSize = 1 << zoom;
                break;

            case OpenWeatherChatAugmentationsServiceInstance.MapTargetType.Continent:
                zoom = 3;
                gridSize = 4;

                if (CountryCentroids.TryGet(target.Identifier, out centroid))
                {
                    centerX = LonToTileX(centroid.Lon, zoom);
                    centerY = LatToTileY(centroid.Lat, zoom);
                }
                else
                {
                    logger.LogWarning("No centroid found for continent code {Code}", target.Identifier);
                    return Array.Empty<byte>();
                }
                break;
            case OpenWeatherChatAugmentationsServiceInstance.MapTargetType.Country:
                zoom = 5;
                gridSize = 3;

                if (CountryCentroids.TryGet(target.Identifier, out centroid))
                {
                    centerX = LonToTileX(centroid.Lon, zoom);
                    centerY = LatToTileY(centroid.Lat, zoom);
                }
                else
                {
                    logger.LogWarning("No centroid found for country code {Code}", target.Identifier);
                    return Array.Empty<byte>();
                }
                break;

            default:
                logger.LogWarning("Unsupported map target type");
                return Array.Empty<byte>();
        }

        using var stitched = new Image<Rgba32>(tileSize * gridSize, tileSize * gridSize);

        if (target.Type == OpenWeatherChatAugmentationsServiceInstance.MapTargetType.Global)
        {
            // Render the whole world
            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    await DrawTileAsync(stitched, tileFetcher, layer, zoom, x, y, x * tileSize, y * tileSize, cancellationToken);
                }
            }
        }
        else
        {
            // Render a grid around the centroid
            int half = gridSize / 2;
            for (int dx = -half; dx <= half; dx++)
            {
                for (int dy = -half; dy <= half; dy++)
                {
                    int tileX = centerX + dx;
                    int tileY = centerY + dy;

                    if (tileX < 0 || tileY < 0 || tileX >= (1 << zoom) || tileY >= (1 << zoom))
                        continue;

                    int targetX = (dx + half) * tileSize;
                    int targetY = (dy + half) * tileSize;

                    await DrawTileAsync(stitched, tileFetcher, layer, zoom, tileX, tileY, targetX, targetY, cancellationToken);
                }
            }
        }

        stitched.DrawAttribution("© OpenStreetMap contributors");

        using var output = new MemoryStream();
        await stitched.SaveAsPngAsync(output, cancellationToken);
        return output.ToArray();
    }

    private async Task DrawTileAsync(
        Image<Rgba32> stitched,
        TileFetcher tileFetcher,
        string layer,
        int zoom,
        int tileX,
        int tileY,
        int targetX,
        int targetY,
        CancellationToken cancellationToken)
    {
        using var osmTile = await tileFetcher.GetOsmTileAsync(zoom, tileX, tileY, cancellationToken);
        using var weatherTile = await tileFetcher.GetWeatherTileAsync(apiKey, layer, zoom, tileX, tileY, cancellationToken);

        stitched.Mutate(ctx =>
        {
            ctx.DrawImage(osmTile, new SixLabors.ImageSharp.Point(targetX, targetY), 1f);
            ctx.DrawImage(weatherTile, new SixLabors.ImageSharp.Point(targetX, targetY), 1f);
        });
    }
    
    private int LonToTileX(double lon, int zoom)
    {
        return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));
    }

    private int LatToTileY(double lat, int zoom)
    {
        var latRad = lat * Math.PI / 180.0;
        return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << zoom));
    }
    
    public async Task<GeoResult?> ResolveLocationAsync(string location, CancellationToken cancellationToken)
    {
        try
        {
            var parts = location.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string city = parts[0];
            string? countryCode = null;

            if (parts.Length > 1)
            {
                var possibleCountry = parts[^1];
                if (CountryCodeMap.TryGetAlpha2(possibleCountry, out var code))
                {
                    countryCode = code;
                }
            }

            var query = countryCode != null ? $"{city},{countryCode}" : city;

            // https://openweathermap.org/api/geocoding-api#direct
            var geoUrl =
                $"http://api.openweathermap.org/geo/1.0/direct?q={Uri.EscapeDataString(query)}&limit=1&appid={apiKey}";

            var geoResults = await httpClient.GetFromJsonAsync<List<GeoResult>>(geoUrl, cancellationToken);

            if (geoResults == null || geoResults.Count == 0)
            {
                logger.LogWarning("Could not resolve location '{Location}'", location);
                return null;
            }

            return geoResults[0];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error resolving location '{Location}'", location);
            return null;
        }
    }

}
