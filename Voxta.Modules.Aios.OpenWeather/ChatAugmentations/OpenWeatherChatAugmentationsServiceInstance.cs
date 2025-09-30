using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Abstractions.Services.VisionCapture;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Modules.Aios.OpenWeather.Clients;
using Voxta.Modules.Aios.OpenWeather.Helper;

// ReSharper disable InconsistentNaming

namespace Voxta.Modules.Aios.OpenWeather.ChatAugmentations;

public class OpenWeatherChatAugmentationsServiceInstance(
	IChatSessionChatAugmentationApi session,
	IOpenWeatherClient client,
	OpenWeatherChatAugmentationSettings chatAugmentationSettings,
	ILogger<OpenWeatherChatAugmentationsServiceInstance> logger
	) : IActionInferenceAugmentation
{
    public ServiceTypes[] GetRequiredServiceTypes() => [ServiceTypes.ActionInference];
    public string[] GetAugmentationNames() => [VoxtaModule.AugmentationKey];
    private readonly CultureInfo _culture = session.MainCharacter.Culture;
    private readonly bool _weatherExpertMode = chatAugmentationSettings.WeatherExpertMode;
    private readonly bool _pollutionExpertMode = chatAugmentationSettings.PollutionExpertMode;
    public enum MapTargetType { Global, Continent, Country }

    public IEnumerable<ClientUpdateContextMessage> RegisterChatContext()
    {
        return
        [
            new ClientUpdateContextMessage
            {
                ContextKey = VoxtaModule.ServiceName,
                SessionId = session.SessionId,
				Actions =
				[
					new()
					{
						Name = "get_weather",
                        Layer = "Weather",
						ShortDescription = "get the latest weather, temperature or rain data",
						Description = "When {{ user }} asks for the weather temperature, rain or snow.",
						MatchFilter = [@"\b(?:weather|temperature|temperatures|rain|raining|rains|snow|snowing|snows)\b(?![^.]*\b(?:forecast|next|tomorrow|weekend|days?|hours?)\b)"],
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
						Arguments =
						[
							new FunctionArgumentDefinition
							{
								Name = "get_weather_location",
								Description = "The exact location for which to retrieve weather data. If the user has not explicitly provided a location in their request, leave this value empty. Do not guess, infer, or reuse any previous location.",
								Required = false,
								Type = FunctionArgumentType.String,
							}
						],
					},
					new ()
					{
						Name = "get_weather_forecast",
						Layer = "Weather",
						ShortDescription = "Get the weather forecast and timeframe",
						Description = "When {{ user }} asks for the weather forecast for a certain period of time.",
						MatchFilter = [@"\b(?:forecast|next|tomorrow|weekend|days|hours)\b"],
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
						Arguments =
						[
							new FunctionArgumentDefinition
							{
								Name = "get_forecast_location",
								Description = "The exact location for which to retrieve weather forecast data. If the user has not explicitly provided a location in their request, leave this value empty. Do not guess, infer, or reuse any previous location.",
								Required = false,
								Type = FunctionArgumentType.String,
							},
						],
					},
					new()
					{
						Name = "get_air_pollution",
						Layer = "Weather",
						ShortDescription = "Get the current air pollution data",
						Description = "When {{ user }} asks for the current air quality or pollution.",
						MatchFilter = [@"\b(?:air\s?quality|pollution|AQI|air\s?pollution)\b(?![^.]*\b(?:forecast|next|tomorrow|weekend|days?|hours?)\b)"],
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
						Arguments =
						[
							new FunctionArgumentDefinition
							{
								Name = "get_air_pollution_location",
								Description = "The exact location for which to retrieve pollution data. If the user has not explicitly provided a location in their request, leave this value empty. Do not guess, infer, or reuse any previous location.",
								Required = false,
								Type = FunctionArgumentType.String,
							}
						],
					},
					new()
					{
						Name = "get_air_pollution_forecast",
						Layer = "Weather",
						ShortDescription = "Get the air pollution forecast",
						Description = "When {{ user }} asks for the air quality or pollution forecast.",
						MatchFilter = [@"\b(?:air\s?quality\s?forecast|pollution\s?forecast|AQI\s?forecast)\b"],
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
						Arguments =
						[
							new FunctionArgumentDefinition
							{
								Name = "get_air_pollution_forecast_location",
								Description = "The exact location for which to retrieve pollution forecast data. If the user has not explicitly provided a location in their request, leave this value empty. Do not guess, infer, or reuse any previous location.",
								Required = false,
								Type = FunctionArgumentType.String,
							}
						],
					},
					new ()
					{
						Name = "get_weather_map",
						Layer = "Weather",
						ShortDescription = "Get a weather map for a given location and layer",
						Description = "When {{ user }} asks to see a weather map (e.g. temperature, clouds, wind, pressure, precipitation).",
						MatchFilter = [@"\b(?:map|radar|satellite|clouds|temperature|wind|pressure|precipitation|rain|snow)\b"],
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
						Arguments =
						[
							new FunctionArgumentDefinition
							{
								Name = "get_map_layer",
								Description = "The weather map layer requested by the user. Possible values: clouds, precipitation, pressure, wind, temp",
								Required = true,
								Type = FunctionArgumentType.String,
							},
							new FunctionArgumentDefinition
							{
								Name = "get_map_location",
								Description = "The continent or country for which to retrieve a weather map. If the user has not explicitly provided a location in their request, select global.",
								Required = false,
								Type = FunctionArgumentType.String,
							},
						],
					},

				]
            }
        ];
    }

    public async ValueTask<bool> TryHandleActionInference(
        ChatMessageData? message,
        ServerActionMessage serverActionMessage,
        CancellationToken cancellationToken
    )
    {
        if (serverActionMessage.ContextKey != VoxtaModule.ServiceName)
            return false;
        if (serverActionMessage.Role != ChatMessageRole.User)
	        return false;
        
        string? location;
        string? loc;
        
        switch (serverActionMessage.Value)
        {
            case "get_weather":
					location = await ResolveLocationNameAsync(
		            serverActionMessage.TryGetArgument("get_weather_location", out loc) ? loc : null,
		            "No weather data available as the user location is not set and no location was specified.",
		            cancellationToken);

	            if (location != null)
		            await GetWeather(location, cancellationToken);
                return true;
            case "get_weather_forecast":
	            location = await ResolveLocationNameAsync(
		            serverActionMessage.TryGetArgument("get_forecast_location", out loc) ? loc : null,
		            "No weather forecast data available as the user location is not set and no location was specified.",
		            cancellationToken);

	            if (location != null)
					await GetWeatherForecast(location, cancellationToken);
	            return true;
            case "get_air_pollution":
	            location = await ResolveLocationNameAsync(
		            serverActionMessage.TryGetArgument("get_air_pollution_location", out loc) ? loc : null,
		            "No air pollution data available as the location is not set and no location was specified.",
		            cancellationToken);

	            if (location != null)
		            await GetAirPollution(location, cancellationToken);
	            return true;
            case "get_air_pollution_forecast":
	            location = await ResolveLocationNameAsync(
		            serverActionMessage.TryGetArgument("get_air_pollution_forecast_location", out loc) ? loc : null,
		            "No air pollution forecast data available as the user location is not set and no location was specified.",
		            cancellationToken);

	            if (location != null)
					await GetAirPollutionForecast(location, cancellationToken);
	            return true;
            case "get_weather_map":
	            var locArg = GetSafeArgument(serverActionMessage, "get_map_location");
	            var target = ResolveMapTarget(locArg);

	            var rawLayer = GetSafeArgument(serverActionMessage, "get_map_layer");
	            var normalizedLayer = WeatherMapHelper.NormalizeLayer(rawLayer);
	            
	            await GetWeatherMapAsync(target, normalizedLayer, cancellationToken);
	            return true;
            default:
                return false;
        }
    }
    
	private async Task GetWeather(string location, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {Location}", location);
		location = CleanLocationString(location);

		try
		{
			var weatherData = await client.FetchWeatherData(location, chatAugmentationSettings.Units, cancellationToken);
			if (weatherData == null)
			{
				logger.LogWarning("No weather data returned for {Location}", location);
				await session.SendSecretAsync($"Sorry, I couldn’t retrieve weather data for {location}.", cancellationToken);
				await session.TriggerReplyAsync(cancellationToken);
				return;
			}
			
			var rain = weatherData.Rain?.OneHour ?? 0;
			var rainPrecipitationText = rain > 0
				? $" and estimated {rain:F1} mm/h precipitation of rain"
				: "";
			
			var snow = weatherData.Snow?.OneHour ?? 0;
			var snowPrecipitationText = snow > 0
				? $" and estimated {snow:F1} mm/h precipitation of snow"
				: "";

			var unitSuffix = chatAugmentationSettings.Units == "imperial" ? "°F" : "°C";
			
			string messageText;
			if (_weatherExpertMode)
			{
				messageText =
					$"The current temperature in {location} ({weatherData.Sys.Country}) is {weatherData.Main.Temp}{unitSuffix} " +
					$"(feels like {weatherData.Main.FeelsLike}{unitSuffix}) with {weatherData.Weather[0].Description}{rainPrecipitationText}{snowPrecipitationText}. " +
					$"The temperature ranges between {weatherData.Main.TempMin}-{weatherData.Main.TempMax}{unitSuffix}. " +
					$"Wind: {weatherData.Wind.Speed:0.#} m/s at {weatherData.Wind.Deg}°, " +
					$"Cloud cover: {weatherData.Clouds.All}%, " +
					$"Visibility: {weatherData.Visibility / 1000.0:0.#} km.";
			}
			else
			{
				messageText =
					$"The current temperature in {location} ({weatherData.Sys.Country}) is {weatherData.Main.Temp}{unitSuffix} " +
					$"with {weatherData.Weather[0].Description}{rainPrecipitationText}{snowPrecipitationText}. " +
					$"The temperature ranges between {weatherData.Main.TempMin}-{weatherData.Main.TempMax}{unitSuffix}.";
			}
			
		    await session.SendSecretAsync(messageText, cancellationToken);
		    await session.TriggerReplyAsync(cancellationToken);
		}
		catch (Exception exc)
		{
			logger.LogError(exc, "Failed to fetch weather data");
			await session.SendSecretAsync($"Failed to fetch weather data: {exc.Message}", cancellationToken);
			await session.TriggerReplyAsync(cancellationToken);
		}
	}
	
	private async ValueTask GetWeatherForecast(string location, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {location}", location);
		location = CleanLocationString(location);
	    
		var forecast = await client.FetchForecastData(location, chatAugmentationSettings.Units, cancellationToken);
		if (forecast == null)
		{
			logger.LogWarning("No weather forecast data returned for {Location}", location);
			await session.SendSecretAsync($"Sorry, I couldn’t retrieve weather forecast data for {location}.", cancellationToken);
			await session.TriggerReplyAsync(cancellationToken);
			return;
		}
		
		var unitSuffix = chatAugmentationSettings.Units == "imperial" ? "°F" : "°C";
		var summaryText = WeatherForecastSummariser.Summarise(forecast.List, _culture,  _weatherExpertMode,days: 5, unitSuffix);
		var introText = $"Weather forecast for {location} ({forecast.City.Country}):";
		var messageText = $"{introText}\n{summaryText}";
	    
		await session.SendSecretAsync(messageText, cancellationToken);
		await session.TriggerReplyAsync(cancellationToken);
	}

	
	private async ValueTask GetAirPollution(string location, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {location}", location);
		location = CleanLocationString(location);

		try
		{
			var pollutionData = await client.FetchAirPollutionData(location, cancellationToken);
			if (pollutionData == null)
			{
				logger.LogWarning("No pollution data returned for {Location}", location);
				await session.SendSecretAsync($"Sorry, I couldn’t retrieve pollution data for {location}.", cancellationToken);
				await session.TriggerReplyAsync(cancellationToken);
				return;
			}
			
			var aqi = pollutionData.List[0].Main.Aqi;
			var components = pollutionData.List[0].Components;

			string messageText;
			if (_pollutionExpertMode)
			{
				messageText = $"Current air quality in {location}: AQI {aqi} ({AirPollutionForecastSummariser.GetAqiLabel(aqi)})\n" +
				              $"CO: {components.Co} µg/m³, NO: {components.No} µg/m³, NO₂: {components.No2} µg/m³, " +
				              $"O₃: {components.O3} µg/m³, SO₂: {components.So2} µg/m³, PM2.5: {components.Pm2_5} µg/m³, " +
				              $"PM10: {components.Pm10} µg/m³, NH₃: {components.Nh3} µg/m³";
			}
			else
			{
				messageText = $"Current air quality in {location}: AQI {aqi} ({AirPollutionForecastSummariser.GetAqiLabel(aqi)})\n" +
				              $"PM2.5: {components.Pm2_5} µg/m³, PM10: {components.Pm10} µg/m³, " +
				              $"NO₂: {components.No2} µg/m³, O₃: {components.O3} µg/m³";
			}

			await session.SendSecretAsync(messageText, cancellationToken);
			await session.TriggerReplyAsync(cancellationToken);
		}
		catch (Exception exc)
		{
			logger.LogError(exc, "Failed to fetch air pollution data");
			await session.SendSecretAsync($"Failed to fetch air pollution data: {exc.Message}", cancellationToken);
			await session.TriggerReplyAsync(cancellationToken);
		}
	}

	private async ValueTask GetAirPollutionForecast(string location, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {location}", location);
		location = CleanLocationString(location);

	    try
	    {
	        var forecastData = await client.FetchAirPollutionForecastData(location, cancellationToken);
	        if (forecastData == null)
	        {
		        logger.LogWarning("No forecast pollution data returned for {Location}", location);
		        await session.SendSecretAsync($"Sorry, I couldn’t retrieve forecast pollution data for {location}.", cancellationToken);
		        await session.TriggerReplyAsync(cancellationToken);
		        return;
	        }
	        
	        var summaryText = AirPollutionForecastSummariser.Summarise(forecastData.List, _culture, days: 5, _pollutionExpertMode);
	        var introText = $"Air pollution forecast for {location}:";
	        var messageText = $"{introText}\n{summaryText}";

	        await session.SendSecretAsync(messageText, cancellationToken);
	        await session.TriggerReplyAsync(cancellationToken);
	    }
	    catch (Exception exc)
	    {
	        logger.LogError(exc, "Failed to fetch air pollution forecast data");
	        await session.SendSecretAsync($"Failed to fetch air pollution forecast data: {exc.Message}", cancellationToken);
	        await session.TriggerReplyAsync(cancellationToken);
	    }
	}
	
	private async ValueTask GetWeatherMapAsync(
		(MapTargetType Type, string Identifier) target,
		string normalizedLayer,
		CancellationToken cancellationToken)
	{
		var bytes = await client.FetchWeatherMapAsync(target, normalizedLayer, cancellationToken);

		if (bytes == null || bytes.Length == 0)
		{
			logger.LogWarning("No weather map could be generated for {Target}", target.Identifier);

			await session.SendSecretAsync(
				$"Couldn’t generate a weather map for {target.Identifier} ({WeatherMapHelper.ToDisplayName(normalizedLayer)}).",
				cancellationToken
			);
			await session.TriggerReplyAsync(cancellationToken);
			return;
		}

		var image = new BytesImage("image/png", bytes, ComputerVisionSource.Screen)
		{
			FileName = $"weathermap_{normalizedLayer}_{target.Identifier}.png"
		};

		await session.SendNoteAttachmentAsync(
			$"{{{{ char }}}} fetched the weather map for {target.Identifier} ({WeatherMapHelper.ToDisplayName(normalizedLayer)}). ",
			image,
			cancellationToken
		);
	}
	
	private async Task<string?> ResolveLocationNameAsync(
		string? providedLocation,
		string missingLocationMessage,
		CancellationToken cancellationToken)
	{
		var location = !string.IsNullOrWhiteSpace(providedLocation)
			? providedLocation
			: chatAugmentationSettings.MyLocation;

		if (string.IsNullOrWhiteSpace(location))
		{
			logger.LogInformation("Location is not set!");
			await session.SendSecretAsync(missingLocationMessage, cancellationToken);
			await session.TriggerReplyAsync(cancellationToken);
			return null;
		}

		return location;
	}
	
	private (MapTargetType Type, string Identifier) ResolveMapTarget(string? providedLocation)
	{
		if (string.IsNullOrWhiteSpace(providedLocation))
			return (MapTargetType.Global, "Global");

		var normalized = providedLocation.Trim();
		
		if (string.Equals(normalized, "global", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(normalized, "world", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(normalized, "earth", StringComparison.OrdinalIgnoreCase))
		{
			return (MapTargetType.Global, "Global");
		}
		
		if (WeatherMapHelper.ContinentMap.TryGetValue(normalized.Replace(" ", ""), out var continentCode))
		{
			return (MapTargetType.Continent, continentCode);
		}
		
		if (CountryCodeMap.TryGetAlpha2(normalized, out var alpha2))
		{
			return (MapTargetType.Country, alpha2!);
		}
		
		return (MapTargetType.Global, "Global");
	}

	
	private static string CleanLocationString(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return string.Empty;

		input = Regex.Replace(input, @"(?<=[\w])-(?=[\w])", " ");
		input = Regex.Replace(input, @"[^\w\s]", "");
		input = Regex.Replace(input, @"\s+", " ").Trim();

		return input;
	}
	
	string? GetSafeArgument(ServerActionMessage msg, string argName)
	{
		return msg.TryGetArgument(argName, out var value) && 
		       !string.IsNullOrWhiteSpace(value) && 
		       !string.Equals(value, "undefined", StringComparison.OrdinalIgnoreCase)
			? value
			: null;
	}
	
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}