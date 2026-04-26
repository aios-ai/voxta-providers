using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Voxta.Modules.Aios.OpenWeather.ChatAugmentations;
using Voxta.Modules.Aios.OpenWeather.Helper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Voxta.Modules.Aios.OpenWeather.Clients;

public enum OpenWeatherOperationState
{
    Connected,
    Disconnected,
    AuthRequired,
    ConfigurationRequired,
    MissingResource,
    ApiFailure,
    Unavailable
}

public sealed record OpenWeatherResult<T>(
    bool Success,
    T? Value,
    OpenWeatherOperationState State,
    string UserVisibleError)
{
    public static OpenWeatherResult<T> Ok(T value) => new(true, value, OpenWeatherOperationState.Connected, string.Empty);

    public static OpenWeatherResult<T> Fail(OpenWeatherOperationState state, string userVisibleError) =>
        new(false, default, state, userVisibleError);
}

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
        var httpClient = httpClientFactory.CreateClient(VoxtaModule.ServiceName);
        return new OpenWeatherClient(httpClient, apiKey, loggerFactory.CreateLogger<OpenWeatherClient>());
    }
}

public interface IOpenWeatherClient
{
    string LastUserVisibleError { get; }
    OpenWeatherOperationState State { get; }
    Task<OpenWeatherResult<OpenWeatherResponse>> FetchWeatherData(string location, string? units, CancellationToken cancellationToken);
    Task<OpenWeatherResult<OpenWeatherForecastResponse>> FetchForecastData(string location, string? units, CancellationToken cancellationToken);
    Task<OpenWeatherResult<OpenWeatherAirPollutionResponse>> FetchAirPollutionData(string location, CancellationToken cancellationToken);
    Task<OpenWeatherResult<OpenWeatherAirPollutionResponse>> FetchAirPollutionForecastData(string location, CancellationToken cancellationToken);
    Task<OpenWeatherResult<byte[]>> FetchWeatherMapAsync(
        (OpenWeatherChatAugmentationsServiceInstance.MapTargetType Type, string Identifier) target,
        string layer,
        string cacheDir,
        CancellationToken cancellationToken);
}

public class OpenWeatherClient(
    HttpClient httpClient,
    string apiKey,
    ILogger<OpenWeatherClient> logger
) : IOpenWeatherClient
{
    public string LastUserVisibleError { get; private set; } = string.Empty;
    public OpenWeatherOperationState State { get; private set; } =
        string.IsNullOrWhiteSpace(apiKey) ? OpenWeatherOperationState.ConfigurationRequired : OpenWeatherOperationState.Disconnected;

    public async Task<OpenWeatherResult<OpenWeatherResponse>> FetchWeatherData(
        string location, string? units, CancellationToken cancellationToken)
    {
        try
        {
            var ready = EnsureConfigured<OpenWeatherResponse>();
            if (!ready.Success)
                return ready;

            logger.LogInformation("Resolving location '{Location}'...", location);
            var geo = await ResolveLocationAsync(location, cancellationToken);

            if (!geo.Success)
            {
                logger.LogWarning("Could not resolve location '{Location}'", location);
                return Fail<OpenWeatherResponse>(geo.State, geo.UserVisibleError);
            }

            logger.LogInformation("Fetching weather for {Name}, {Country} ({Lat}, {Lon})",
                geo.Value!.Name, geo.Value.Country, geo.Value.Lat, geo.Value.Lon);

            // https://openweathermap.org/current#geo
            var weatherUrl = $"http://api.openweathermap.org/data/2.5/weather?lat={geo.Value.Lat}&lon={geo.Value.Lon}&appid={apiKey}&units={units}";
            var response = await httpClient.GetAsync(weatherUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch weather data. Status Code: {StatusCode}, Body: {Body}",
                    response.StatusCode, body);
                return Fail<OpenWeatherResponse>(MapHttpState(response.StatusCode), BuildApiError("weather data", response.StatusCode));
            }

            var content = await response.Content.ReadFromJsonAsync<OpenWeatherResponse>(cancellationToken);
            if (content == null)
            {
                logger.LogError("Failed to parse weather data for {Location}", location);
                return Fail<OpenWeatherResponse>(OpenWeatherOperationState.ApiFailure, "OpenWeather returned an unreadable weather response.");
            }

            return Ok(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error fetching weather data for {Location}", location);
            return Fail<OpenWeatherResponse>(OpenWeatherOperationState.Unavailable, "OpenWeather is currently unavailable. Try again later.");
        }
    }
    
    public async Task<OpenWeatherResult<OpenWeatherForecastResponse>> FetchForecastData(
        string location,
        string? units,
        CancellationToken cancellationToken)
    {
        try
        {
            var ready = EnsureConfigured<OpenWeatherForecastResponse>();
            if (!ready.Success)
                return ready;

            logger.LogInformation("Resolving location '{Location}' for forecast...", location);
            var geo = await ResolveLocationAsync(location, cancellationToken);

            if (!geo.Success)
            {
                logger.LogWarning("Could not resolve location '{Location}' for forecast", location);
                return Fail<OpenWeatherForecastResponse>(geo.State, geo.UserVisibleError);
            }

            logger.LogInformation("Fetching forecast for {Name}, {Country} ({Lat}, {Lon})",
                geo.Value!.Name, geo.Value.Country, geo.Value.Lat, geo.Value.Lon);

            // https://openweathermap.org/forecast5
            var forecastUrl = $"http://api.openweathermap.org/data/2.5/forecast?lat={geo.Value.Lat}&lon={geo.Value.Lon}&appid={apiKey}&units={units}";
            var response = await httpClient.GetAsync(forecastUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch forecast data. Status Code: {StatusCode}, Body: {Body}",
                    response.StatusCode, body);
                return Fail<OpenWeatherForecastResponse>(MapHttpState(response.StatusCode), BuildApiError("weather forecast", response.StatusCode));
            }

            var content = await response.Content.ReadFromJsonAsync<OpenWeatherForecastResponse>(cancellationToken);
            if (content == null)
            {
                logger.LogError("Failed to parse forecast data for {Location}", location);
                return Fail<OpenWeatherForecastResponse>(OpenWeatherOperationState.ApiFailure, "OpenWeather returned an unreadable forecast response.");
            }

            return Ok(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error fetching forecast data for {Location}", location);
            return Fail<OpenWeatherForecastResponse>(OpenWeatherOperationState.Unavailable, "OpenWeather is currently unavailable. Try again later.");
        }
    }
    
    public async Task<OpenWeatherResult<OpenWeatherAirPollutionResponse>> FetchAirPollutionData(
        string location,
        CancellationToken cancellationToken)
    {
        try
        {
            var ready = EnsureConfigured<OpenWeatherAirPollutionResponse>();
            if (!ready.Success)
                return ready;

            var geo = await ResolveLocationAsync(location, cancellationToken);
            if (!geo.Success)
            {
                logger.LogWarning("Could not resolve location '{Location}' for air pollution data", location);
                return Fail<OpenWeatherAirPollutionResponse>(geo.State, geo.UserVisibleError);
            }

            // https://openweathermap.org/api/air-pollution#current
            var url = $"http://api.openweathermap.org/data/2.5/air_pollution?lat={geo.Value!.Lat}&lon={geo.Value.Lon}&appid={apiKey}";
            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch air pollution data. Status Code: {StatusCode}, Body: {Body}",
                    response.StatusCode, body);
                return Fail<OpenWeatherAirPollutionResponse>(MapHttpState(response.StatusCode), BuildApiError("air pollution data", response.StatusCode));
            }

            var content = await response.Content.ReadFromJsonAsync<OpenWeatherAirPollutionResponse>(cancellationToken);
            if (content == null)
            {
                logger.LogError("Failed to parse air pollution data for {Location}", location);
                return Fail<OpenWeatherAirPollutionResponse>(OpenWeatherOperationState.ApiFailure, "OpenWeather returned an unreadable air pollution response.");
            }

            return Ok(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error fetching air pollution data for {Location}", location);
            return Fail<OpenWeatherAirPollutionResponse>(OpenWeatherOperationState.Unavailable, "OpenWeather is currently unavailable. Try again later.");
        }
    }
    
    public async Task<OpenWeatherResult<OpenWeatherAirPollutionResponse>> FetchAirPollutionForecastData(
        string location,
        CancellationToken cancellationToken)
    {
        try
        {
            var ready = EnsureConfigured<OpenWeatherAirPollutionResponse>();
            if (!ready.Success)
                return ready;

            var geo = await ResolveLocationAsync(location, cancellationToken);
            if (!geo.Success)
            {
                logger.LogWarning("Could not resolve location '{Location}' for air pollution forecast", location);
                return Fail<OpenWeatherAirPollutionResponse>(geo.State, geo.UserVisibleError);
            }

            // https://openweathermap.org/api/air-pollution#forecast
            var url = $"http://api.openweathermap.org/data/2.5/air_pollution/forecast?lat={geo.Value!.Lat}&lon={geo.Value.Lon}&appid={apiKey}";
            var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to fetch air pollution forecast data. Status Code: {StatusCode}, Body: {Body}",
                    response.StatusCode, body);
                return Fail<OpenWeatherAirPollutionResponse>(MapHttpState(response.StatusCode), BuildApiError("air pollution forecast", response.StatusCode));
            }

            var content = await response.Content.ReadFromJsonAsync<OpenWeatherAirPollutionResponse>(cancellationToken);
            if (content == null)
            {
                logger.LogError("Failed to parse air pollution forecast data for {Location}", location);
                return Fail<OpenWeatherAirPollutionResponse>(OpenWeatherOperationState.ApiFailure, "OpenWeather returned an unreadable air pollution forecast response.");
            }

            return Ok(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error fetching air pollution forecast data for {Location}", location);
            return Fail<OpenWeatherAirPollutionResponse>(OpenWeatherOperationState.Unavailable, "OpenWeather is currently unavailable. Try again later.");
        }
    }
    
    public async Task<OpenWeatherResult<byte[]>> FetchWeatherMapAsync(
        (OpenWeatherChatAugmentationsServiceInstance.MapTargetType Type, string Identifier) target,
        string layer,
        string cacheDir,
        CancellationToken cancellationToken)
    {
        var ready = EnsureConfigured<byte[]>();
        if (!ready.Success)
            return ready;

        try
        {
        var tileFetcher = new TileFetcher(httpClient, cacheDir);
        
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
                    return Fail<byte[]>(OpenWeatherOperationState.MissingResource, $"No map target was found for {target.Identifier}.");
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
                    return Fail<byte[]>(OpenWeatherOperationState.MissingResource, $"No map target was found for {target.Identifier}.");
                }
                break;

            default:
                logger.LogWarning("Unsupported map target type");
                return Fail<byte[]>(OpenWeatherOperationState.MissingResource, "The requested weather map target is not supported.");
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
        return Ok(output.ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error generating weather map for {Target}", target.Identifier);
            return Fail<byte[]>(OpenWeatherOperationState.Unavailable, "OpenWeather map data is currently unavailable. Try again later.");
        }
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
    
    public async Task<OpenWeatherResult<GeoResult>> ResolveLocationAsync(string location, CancellationToken cancellationToken)
    {
        try
        {
            var ready = EnsureConfigured<GeoResult>();
            if (!ready.Success)
                return ready;

            var parts = location.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return Fail<GeoResult>(OpenWeatherOperationState.ConfigurationRequired, "No location was provided.");

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
                return Fail<GeoResult>(OpenWeatherOperationState.MissingResource, $"OpenWeather could not find a location named {location}.");
            }

            return Ok(geoResults[0]);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "OpenWeather geocoding request failed for '{Location}'", location);
            return Fail<GeoResult>(OpenWeatherOperationState.Unavailable, "OpenWeather is currently unreachable. Try again later.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error resolving location '{Location}'", location);
            return Fail<GeoResult>(OpenWeatherOperationState.Unavailable, "OpenWeather is currently unavailable. Try again later.");
        }
    }

    private OpenWeatherResult<T> EnsureConfigured<T>()
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            return OpenWeatherResult<T>.Ok(default!);

        return Fail<T>(
            OpenWeatherOperationState.ConfigurationRequired,
            "OpenWeather is not configured. Add an OpenWeather API key in the module settings, then reload the module.");
    }

    private OpenWeatherResult<T> Ok<T>(T value)
    {
        State = OpenWeatherOperationState.Connected;
        LastUserVisibleError = string.Empty;
        return OpenWeatherResult<T>.Ok(value);
    }

    private OpenWeatherResult<T> Fail<T>(OpenWeatherOperationState state, string userVisibleError)
    {
        State = state;
        LastUserVisibleError = userVisibleError;
        return OpenWeatherResult<T>.Fail(state, userVisibleError);
    }

    private static OpenWeatherOperationState MapHttpState(System.Net.HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => OpenWeatherOperationState.AuthRequired,
            System.Net.HttpStatusCode.NotFound => OpenWeatherOperationState.MissingResource,
            System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests => OpenWeatherOperationState.Unavailable,
            >= System.Net.HttpStatusCode.InternalServerError => OpenWeatherOperationState.Unavailable,
            _ => OpenWeatherOperationState.ApiFailure
        };
    }

    private static string BuildApiError(string operation, System.Net.HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                "OpenWeather rejected the API key. Check the OpenWeather API key in the module settings, then reload the module.",
            System.Net.HttpStatusCode.NotFound =>
                $"OpenWeather could not find the requested {operation}.",
            System.Net.HttpStatusCode.TooManyRequests =>
                "OpenWeather rate-limited the request. Wait a bit and try again.",
            >= System.Net.HttpStatusCode.InternalServerError =>
                "OpenWeather is currently unavailable. Try again later.",
            _ =>
                $"OpenWeather could not return {operation}. HTTP status: {(int)statusCode} {statusCode}."
        };
    }
}
