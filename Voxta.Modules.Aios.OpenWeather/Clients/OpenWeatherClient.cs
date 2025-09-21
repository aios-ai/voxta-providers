using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Voxta.Modules.Aios.OpenWeather.Helper;

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
    Task<OpenWeatherResponse> FetchWeatherData(string location, string? units, CancellationToken cancellationToken);
    Task<OpenWeatherForecastResponse> FetchForecastData(string location, string? units, CancellationToken cancellationToken);
    Task<OpenWeatherAirPollutionResponse> FetchAirPollutionData(string location, CancellationToken cancellationToken);
    Task<OpenWeatherAirPollutionResponse> FetchAirPollutionForecastData(string location, CancellationToken cancellationToken);
}

public class OpenWeatherClient(
    HttpClient httpClient,
    string apiKey,
    ILogger<OpenWeatherClient> logger
) : IOpenWeatherClient
{
    public async Task<OpenWeatherResponse> FetchWeatherData(string location, string? units, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resolving location '{Location}'...", location);
        var geo = await ResolveLocationAsync(location, cancellationToken);
        logger.LogInformation("Fetching weather for {Name}, {Country} ({Lat}, {Lon})", geo.Name, geo.Country, geo.Lat, geo.Lon);

        // https://openweathermap.org/current#geo
        var weatherUrl = $"http://api.openweathermap.org/data/2.5/weather?lat={geo.Lat}&lon={geo.Lon}&appid={apiKey}&units={units}";
        var response = await httpClient.GetAsync(weatherUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Failed to fetch weather data. Status Code: {response.StatusCode}, Body: {body}");
        }

        var content = await response.Content.ReadFromJsonAsync<OpenWeatherResponse>(cancellationToken);
        return content ?? throw new InvalidOperationException("Failed to parse weather data from response.");
    }
    
    public async Task<OpenWeatherForecastResponse> FetchForecastData(string location, string? units, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resolving location '{Location}' for forecast...", location);
        var geo = await ResolveLocationAsync(location, cancellationToken);
        logger.LogInformation("Fetching forecast for {Name}, {Country} ({Lat}, {Lon})", geo.Name, geo.Country, geo.Lat, geo.Lon);

        // https://openweathermap.org/forecast5
        var forecastUrl = $"http://api.openweathermap.org/data/2.5/forecast?lat={geo.Lat}&lon={geo.Lon}&appid={apiKey}&units={units}";
        var response = await httpClient.GetAsync(forecastUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Failed to fetch forecast data. Status Code: {response.StatusCode}, Body: {body}");
        }

        var content = await response.Content.ReadFromJsonAsync<OpenWeatherForecastResponse>(cancellationToken);
        return content ?? throw new InvalidOperationException("Failed to parse forecast data from response.");
    }
    
    public async Task<OpenWeatherAirPollutionResponse> FetchAirPollutionData(string location, CancellationToken cancellationToken)
    {
        var geo = await ResolveLocationAsync(location, cancellationToken);
        
        // https://openweathermap.org/api/air-pollution#current
        var url = $"http://api.openweathermap.org/data/2.5/air_pollution?lat={geo.Lat}&lon={geo.Lon}&appid={apiKey}";
        var response = await httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Failed to fetch air pollution data: {response.StatusCode}");

        var content = await response.Content.ReadFromJsonAsync<OpenWeatherAirPollutionResponse>(cancellationToken);
        return content ?? throw new InvalidOperationException("Failed to parse air pollution data.");
    }

    public async Task<OpenWeatherAirPollutionResponse> FetchAirPollutionForecastData(string location, CancellationToken cancellationToken)
    {
        var geo = await ResolveLocationAsync(location, cancellationToken);
        
        // https://openweathermap.org/api/air-pollution#forecast
        var url = $"http://api.openweathermap.org/data/2.5/air_pollution/forecast?lat={geo.Lat}&lon={geo.Lon}&appid={apiKey}";
        var response = await httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Failed to fetch air pollution forecast data: {response.StatusCode}");

        var content = await response.Content.ReadFromJsonAsync<OpenWeatherAirPollutionResponse>(cancellationToken);
        return content ?? throw new InvalidOperationException("Failed to parse air pollution forecast data.");
    }
    

    public async Task<GeoResult> ResolveLocationAsync(string location, CancellationToken cancellationToken)
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
        var geoUrl = $"http://api.openweathermap.org/geo/1.0/direct?q={Uri.EscapeDataString(query)}&limit=1&appid={apiKey}";
        var geoResults = await httpClient.GetFromJsonAsync<List<GeoResult>>(geoUrl, cancellationToken);

        if (geoResults == null || geoResults.Count == 0)
            throw new InvalidOperationException($"Could not resolve location: {location}");

        return geoResults[0];
    }
}
