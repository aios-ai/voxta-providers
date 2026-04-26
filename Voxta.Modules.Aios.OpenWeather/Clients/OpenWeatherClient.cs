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
    Success,
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
    public static OpenWeatherResult<T> Ok(T value) => new(true, value, OpenWeatherOperationState.Success, string.Empty);

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
            var maybePlan = CreateWeatherMapPlan(target);
            if (maybePlan is not { } plan)
            {
                logger.LogWarning("No map bounds found for {TargetType} {Identifier}", target.Type, target.Identifier);
                return Fail<byte[]>(OpenWeatherOperationState.MissingResource, $"No map target was found for {target.Identifier}.");
            }

            var compositeCacheKey =
                $"{target.Type}_{target.Identifier}_{layer}_z{plan.Zoom}_x{plan.MinX}-{plan.MaxX}_y{plan.MinY}-{plan.MaxY}";
            var cachedComposite = await tileFetcher.TryGetCompositeMapAsync(compositeCacheKey, cancellationToken);
            if (cachedComposite is { Length: > 0 })
                return Ok(cachedComposite);

            const int tileSize = 256;
            using var stitched = new Image<Rgba32>(tileSize * plan.Width, tileSize * plan.Height);

            for (var x = plan.MinX; x <= plan.MaxX; x++)
            {
                for (var y = plan.MinY; y <= plan.MaxY; y++)
                {
                    var sourceX = NormalizeTileX(x, plan.Zoom);
                    await DrawTileAsync(
                        stitched,
                        tileFetcher,
                        layer,
                        plan.Zoom,
                        sourceX,
                        y,
                        (x - plan.MinX) * tileSize,
                        (y - plan.MinY) * tileSize,
                        cancellationToken);
                }
            }

            stitched.DrawAttribution("© OpenStreetMap contributors");

            using var output = new MemoryStream();
            await stitched.SaveAsPngAsync(output, cancellationToken);
            var bytes = output.ToArray();
            await tileFetcher.SaveCompositeMapAsync(compositeCacheKey, bytes, cancellationToken);
            return Ok(bytes);
        }
        catch (TileFetchException ex)
        {
            logger.LogError(ex,
                "Weather map tile request failed. Provider: {Provider}, Status: {StatusCode}, Url: {Url}, Body: {Body}",
                ex.Provider,
                ex.StatusCode,
                ex.Url,
                ex.ResponseBody);

            var message = ex.Provider == "OpenStreetMap" && ex.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "OpenStreetMap blocked the map tile request. Check the module logs for the tile usage policy response."
                : $"{ex.Provider} could not return a map tile. Check the module logs for details.";

            return Fail<byte[]>(OpenWeatherOperationState.Unavailable, message);
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
    
    private TilePlan? CreateWeatherMapPlan(
        (OpenWeatherChatAugmentationsServiceInstance.MapTargetType Type, string Identifier) target)
    {
        return target.Type switch
        {
            OpenWeatherChatAugmentationsServiceInstance.MapTargetType.Global =>
                new TilePlan(2, 0, 3, 0, 3),
            OpenWeatherChatAugmentationsServiceInstance.MapTargetType.Continent
                when CountryCentroids.TryGetBounds(target.Identifier, out var bounds) =>
                    ChooseTilePlan(bounds, minZoom: 1, maxZoom: 5, maxTiles: 16, padding: 0),
            OpenWeatherChatAugmentationsServiceInstance.MapTargetType.Country
                when CountryCentroids.TryGetBounds(target.Identifier, out var bounds) =>
                    ChooseTilePlan(bounds, minZoom: 3, maxZoom: 7, maxTiles: 24, padding: 0),
            _ => null
        };
    }

    private TilePlan ChooseTilePlan(GeoBounds bounds, int minZoom, int maxZoom, int maxTiles, int padding)
    {
        var normalized = bounds.ClampForWebMercator();

        for (var zoom = maxZoom; zoom >= minZoom; zoom--)
        {
            var plan = TilePlan.FromBounds(normalized, zoom, padding);
            if (plan.TileCount <= maxTiles)
                return plan;
        }

        for (var zoom = maxZoom; zoom >= minZoom; zoom--)
        {
            var plan = TilePlan.FromBounds(normalized, zoom, padding: 0);
            if (plan.TileCount <= maxTiles)
                return plan;
        }

        return TilePlan.FromBounds(normalized, minZoom, padding: 0);
    }

    private static int LonToTileX(double lon, int zoom)
    {
        var maxTile = (1 << zoom) - 1;
        return Math.Clamp((int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom)), 0, maxTile);
    }

    private static int LonToUnwrappedTileX(double lon, int zoom)
    {
        return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));
    }

    private static int NormalizeTileX(int x, int zoom)
    {
        var tileCount = 1 << zoom;
        return ((x % tileCount) + tileCount) % tileCount;
    }

    private static int LatToTileY(double lat, int zoom)
    {
        lat = Math.Clamp(lat, -85.05112878, 85.05112878);
        var latRad = lat * Math.PI / 180.0;
        var maxTile = (1 << zoom) - 1;
        return Math.Clamp((int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << zoom)), 0, maxTile);
    }

    private readonly record struct TilePlan(int Zoom, int MinX, int MaxX, int MinY, int MaxY)
    {
        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
        public int TileCount => Width * Height;

        public static TilePlan FromBounds(GeoBounds bounds, int zoom, int padding)
        {
            var maxTile = (1 << zoom) - 1;
            var minX = Math.Clamp(LonToUnwrappedTileX(bounds.MinLon, zoom), 0, maxTile);
            var maxX = bounds.CrossesAntimeridian
                ? LonToUnwrappedTileX(bounds.MaxLon + 360, zoom)
                : Math.Clamp(LonToUnwrappedTileX(bounds.MaxLon, zoom), 0, maxTile);
            var minY = LatToTileY(bounds.MaxLat, zoom);
            var maxY = LatToTileY(bounds.MinLat, zoom);

            return new TilePlan(
                zoom,
                Math.Max(0, Math.Min(minX, maxX) - padding),
                Math.Min(maxTile * 2 + 1, Math.Max(minX, maxX) + padding),
                Math.Clamp(Math.Min(minY, maxY) - padding, 0, maxTile),
                Math.Clamp(Math.Max(minY, maxY) + padding, 0, maxTile));
        }
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
        LastUserVisibleError = string.Empty;
        return OpenWeatherResult<T>.Ok(value);
    }

    private OpenWeatherResult<T> Fail<T>(OpenWeatherOperationState state, string userVisibleError)
    {
        LastUserVisibleError = userVisibleError;
        return OpenWeatherResult<T>.Fail(state, userVisibleError);
    }

    private static OpenWeatherOperationState MapHttpState(System.Net.HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => OpenWeatherOperationState.ConfigurationRequired,
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
