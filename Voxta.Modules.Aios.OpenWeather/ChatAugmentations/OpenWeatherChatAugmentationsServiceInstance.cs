using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Modules.Aios.OpenWeather.Clients;
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
						Description = "When {{ user }} asks for the weather temperature or rain in a specific location.",
						MatchFilter =  [@"\b(?:weather|temperature|temperatures|rain|raining|rains|snow|snowing|snows)\b"],
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
						Arguments =
						[
							new FunctionArgumentDefinition
							{
								Name = "get_weather_location",
								Description = "The location for which the weather data will be retrieved",
								Required = true,
								Type = FunctionArgumentType.String,
							}
						],
					},
					new()
					{
						Name = "get_weather_mylocation",
						Layer = "Weather",
						ShortDescription = "get the latest weather, temperature or rain data for user's current location",
						Description = "When {{ user }} asks for the weather, temperature or rain data in his current location.",
						MatchFilter =  [@"\b(?:weather|temperature|temperatures|rain|raining|rains|snow|snowing|snows)\b"],
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
					},
					new ()
					{
						Name = "get_weather_forecast",
						Layer = "Weather",
						ShortDescription = "Get the weather forecast for a specific location and timeframe",
						Description = "When {{ user }} asks for the weather forecast in a specific location for a certain period of time.",
						MatchFilter = [@"\b(?:forecast|next|tomorrow|weekend|days|hours)\b"],
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
						Arguments =
						[
							new FunctionArgumentDefinition
							{
								Name = "forecast_location",
								Description = "The location for which the forecast will be retrieved",
								Required = true,
								Type = FunctionArgumentType.String,
							},
							/*new FunctionArgumentDefinition
							{
								Name = "forecast_timeframe",
								Description = "The timeframe for the forecast (e.g., '3 days', '5 days', 'tomorrow', 'next weekend')",
								Required = false, // optional, defaults to full API range if not provided
								Type = FunctionArgumentType.String,
							}*/
						],
					}
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
        
        switch (serverActionMessage.Value)
        {
            case "get_weather":
	            var location = serverActionMessage.TryGetArgument("get_weather_location", out var locationArg) ? locationArg : null;
	            if (string.IsNullOrEmpty(location))
		            throw new ArgumentException("Location argument is missing for get_weather action");
	            await GetWeather(location, cancellationToken);
                return true;
            case "get_weather_mylocation":
	            await GetWeatherMyLocation(cancellationToken);
	            return true;
            case "get_weather_forecast":
	            var forecastLocation = serverActionMessage.TryGetArgument("forecast_location", out var forecastLocationArg) ? forecastLocationArg : null;
	            if (string.IsNullOrEmpty(forecastLocation))
		            throw new ArgumentException("Location argument is missing for get_weather action");
	            await GetWeatherForecast(forecastLocation, cancellationToken);
	            return true;
            default:
                return false;
        }
    }

    private async ValueTask GetWeatherMyLocation(CancellationToken cancellationToken)
    {
	    logger.LogInformation("Action called for my location");

	    await GetWeather(chatAugmentationSettings.MyLocation, cancellationToken);
    }

    private async ValueTask GetWeather(string? location, CancellationToken cancellationToken)
    {
	    logger.LogInformation("Weather requested for {Location}", location);
	    if (string.IsNullOrWhiteSpace(location))
	    {
		    logger.LogInformation("Location is not set!");
		    await session.SendSecretAsync("No weather data available as the user location is not set and no location was specified.", cancellationToken);
		    await session.TriggerReplyAsync(cancellationToken);
		    return;
	    }
	    
		await FetchAndSendWeather(location, cancellationToken);
    }
    
    private async ValueTask GetWeatherForecast(string? forecastLocation, CancellationToken cancellationToken)
    {
	    logger.LogInformation("Weather forecast requested for {Location}", forecastLocation);
	    if (string.IsNullOrWhiteSpace(forecastLocation))
	    {
		    logger.LogInformation("Location is not set!");
		    await session.SendSecretAsync("No weather data available as the user location is not set and no location was specified.", cancellationToken);
		    await session.TriggerReplyAsync(cancellationToken);
		    return;
	    }
	    
	    var forecast = await client.FetchForecastData(forecastLocation, chatAugmentationSettings.Units, cancellationToken);
	    var unitSuffix = chatAugmentationSettings.Units == "imperial" ? "°F" : "°C";
	    var summaryText = ForecastSummariser.Summarise(forecast.List, days: 5, unitSuffix);
	    var introText = $"Weather forecast for {forecastLocation} ({forecast.City.Country}):";
	    var messageText = $"{introText}\n{summaryText}";
	    
	    await session.SendSecretAsync(messageText, cancellationToken);
	    await session.TriggerReplyAsync(cancellationToken);
    }
    
	private async Task FetchAndSendWeather(string location, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {Location}", location);
		location = CleanLocationString(location);

		try
		{
			var weatherData = await client.FetchWeatherData(location, chatAugmentationSettings.Units, cancellationToken);
			
			var rain = weatherData.Rain?.OneHour ?? 0;
			var rainPrecipitationText = rain > 0
				? $" and estimated {rain:F1} mm/h precipitation of rain"
				: "";
			
			var snow = weatherData.Snow?.OneHour ?? 0;
			var snowPrecipitationText = snow > 0
				? $" and estimated {snow:F1} mm/h precipitation of snow"
				: "";

			var unitSuffix = chatAugmentationSettings.Units == "imperial" ? "°F" : "°C";
			var messageText = $"The current temperature in {location} ({weatherData.Sys.Country}) is {weatherData.Main.Temp}{unitSuffix} with {weatherData.Weather[0].Description}{rainPrecipitationText}{snowPrecipitationText}. The temperature ranges between {weatherData.Main.TempMin}-{weatherData.Main.TempMax}{unitSuffix}.";
			
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
	
	private static string CleanLocationString(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return string.Empty;
		
		input = Regex.Replace(input, @"(?<=[\w])-(?=[\w])", " ");
		
		input = Regex.Replace(input, @"[^\w\s]", "");
		
		input = Regex.Replace(input, @"\s+", " ").Trim();

		return input;
	}

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}