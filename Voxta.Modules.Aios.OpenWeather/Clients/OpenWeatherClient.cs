using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

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
}

public class OpenWeatherClient(
	HttpClient httpClient,
	string apiKey,
	ILogger<OpenWeatherClient> logger
	) : IOpenWeatherClient
{
	public async Task<OpenWeatherResponse> FetchWeatherData(string location, string? units, CancellationToken cancellationToken)
	{
		logger.LogInformation("Fetching weather data...");

		var url = $"http://api.openweathermap.org/data/2.5/weather?q={location}&appid={apiKey}&units={units}";
		var response = await httpClient.GetAsync(url, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException($"Failed to fetch weather data. Status Code: {response.StatusCode}, Body: {body}");
		}

		var content = await response.Content.ReadFromJsonAsync<OpenWeatherResponse>(cancellationToken);

		return content ?? throw new InvalidOperationException("Failed to parse weather data from response.");
	}
}