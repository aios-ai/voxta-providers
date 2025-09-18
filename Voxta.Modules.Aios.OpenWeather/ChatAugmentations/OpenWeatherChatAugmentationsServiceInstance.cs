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
					// Define custom actions
					new()
					{
						// The name used by the LLM to select the action. Make sure to select a clear name.
						Name = "get_weather",
                        // Layers allow you to run your actions separately from the scene
                        Layer = "Weather",
						 // A short description of the action to be included in the functions list, typically used for character action inference
						ShortDescription = "get the latest weather, temperature or rain data",
						// The condition for executing this function
						Description = "When {{ user }} asks for the weather temperature or rain in a specific location.",
						// This match will ensure user action inference is only going to be triggered if this regex matches the message.
						// For example, if you use "please" in all functions, this can avoid running user action inference at all unless
						// the user said "please".
						MatchFilter =  [@"\b(?:weather|temperature|temperatures|rain|raining|rains|snow|snowing|snows)\b"],
						// Only run in response to the user messages 
						Timing = FunctionTiming.AfterUserMessage,
						// Do not generate a response, we will instead handle the action ourselves
						CancelReply = true,
						// Define arguments the character has to choose for the action to be functional
						// In our case the location for which the weather data has been requested
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
    
	private async Task FetchAndSendWeather(string location, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {Location}", location);
		location = CleanLocationString(location);

		try
		{
			var weatherData = await client.FetchWeatherData(location, chatAugmentationSettings.Units, cancellationToken);

			// Build the message text with optional precipitation data
			var rain = weatherData.Rain?.OneHour ?? 0;
			var rainPrecipitationText = rain > 0
				? $" and estimated {rain:F1} mm/h precipitation of rain"
				: "";

			// Build the message text with optional snow data
			var snow = weatherData.Snow?.OneHour ?? 0;
			var snowPrecipitationText = snow > 0
				? $" and estimated {snow:F1} mm/h precipitation of snow"
				: "";

			var unitSuffix = chatAugmentationSettings.Units == "imperial" ? "°F" : "°C";
			var messageText = $"The current temperature in {location} ({weatherData.Sys.Country}) is {weatherData.Main.Temp}{unitSuffix} with {weatherData.Weather[0].Description}{rainPrecipitationText}{snowPrecipitationText}. The temperature ranges between {weatherData.Main.TempMin}-{weatherData.Main.TempMax}{unitSuffix}.";

			// Send the current temperature as event into the chat, the character will respond to it
		    logger.LogInformation("User location is not set! Open the config file and set the variable.");
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
	
	// Function to remove special characters from the location string
	private static string CleanLocationString(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return string.Empty;

		// Replace specific cases where hyphen should be a space
		input = Regex.Replace(input, @"(?<=[\w])-(?=[\w])", " "); // New-York -> New York

		// Remove all other unwanted characters, preserving alphanumerics and spaces
		input = Regex.Replace(input, @"[^\w\s]", "");

		// Normalize whitespace to a single space
		input = Regex.Replace(input, @"\s+", " ").Trim();

		return input;
	}

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}