using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
	OpenWeatherChatAugmentationsSettings chatAugmentationsSettings,
	ILogger<OpenWeatherChatAugmentationsServiceInstance> logger
	) : IActionInferenceAugmentation
{
	public ServiceTypes[] GetRequiredServiceTypes() => [ServiceTypes.ActionInference];
	public string[] GetAugmentationNames() => [VoxtaModule.AugmentationKey];
	private readonly CultureInfo _culture = session.MainCharacter.Culture;
	private readonly HashSet<string> _weatherDetails =
		(chatAugmentationsSettings.WeatherDetails ?? Array.Empty<string>())
		.ToHashSet(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _pollutionDetails =
		(chatAugmentationsSettings.PollutionDetails ?? Array.Empty<string>())
		.ToHashSet(StringComparer.OrdinalIgnoreCase);
	public string cacheDir = chatAugmentationsSettings.TileCachePath;
	public enum MapTargetType { Global, Continent, Country }

	public IEnumerable<ClientUpdateContextMessage> RegisterChatContext()
	{
		return
		[
			new ClientUpdateContextMessage
			{
				ContextKey = VoxtaModule.ServiceName,
				SessionId = session.SessionId,
				Actions = BuildActions()
			}
		];
	}

	private ScenarioActionDefinition[] BuildActions()
	{
		return chatAugmentationsSettings.Actions
			.Where(action => !OpenWeatherChatAugmentationsService.ParseActionBoolean(action.Disabled, false))
			.Select(action => new ScenarioActionDefinition
			{
				Name = action.Name,
				Layer = action.Layer ?? "Weather",
				ShortDescription = action.ShortDescription ?? "",
				Description = action.Description ?? "",
				MatchFilter = ParseMatchFilter(action.MatchFilter),
				Timing = FunctionTiming.AfterUserMessage,
				CancelReply = OpenWeatherChatAugmentationsService.ParseActionBoolean(action.CancelReply, true),
				Arguments = GetActionArguments(action.Name),
			})
			.ToArray();
	}

	private static string[] ParseMatchFilter(string? matchFilter)
	{
		return (matchFilter ?? "")
			.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
			.Select(value => value.Trim())
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.ToArray();
	}

	private static FunctionArgumentDefinition[] GetActionArguments(string actionName)
	{
		return actionName switch
		{
			"get_weather" =>
			[
				new FunctionArgumentDefinition
				{
					Name = "get_weather_location",
					Description = "The exact location for which to retrieve weather data. If the user has not explicitly provided a location in their request, leave this value empty. Do not guess, infer, or reuse any previous location.",
					Required = false,
					Type = FunctionArgumentType.String,
				}
			],
			"get_weather_forecast" =>
			[
				new FunctionArgumentDefinition
				{
					Name = "get_forecast_location",
					Description = "The exact location for which to retrieve weather forecast data. If the user has not explicitly provided a location in their request, leave this value empty. Do not guess, infer, or reuse any previous location.",
					Required = false,
					Type = FunctionArgumentType.String,
				},
			],
			"get_air_pollution" =>
			[
				new FunctionArgumentDefinition
				{
					Name = "get_air_pollution_location",
					Description = "The exact location for which to retrieve pollution data. If the user has not explicitly provided a location in their request, leave this value empty. Do not guess, infer, or reuse any previous location.",
					Required = false,
					Type = FunctionArgumentType.String,
				}
			],
			"get_air_pollution_forecast" =>
			[
				new FunctionArgumentDefinition
				{
					Name = "get_air_pollution_forecast_location",
					Description = "The exact location for which to retrieve pollution forecast data. If the user has not explicitly provided a location in their request, leave this value empty. Do not guess, infer, or reuse any previous location.",
					Required = false,
					Type = FunctionArgumentType.String,
				}
			],
			"get_weather_map" =>
			[
				new FunctionArgumentDefinition
				{
					Name = "layer",
					Description = "The weather map layer requested by the user. Possible values: clouds, precipitation, pressure, wind, temp",
					Required = true,
					Type = FunctionArgumentType.String,
				},
				new FunctionArgumentDefinition
				{
					Name = "location",
					Description = "The continent or country for which to retrieve a weather map. If the user has not explicitly provided a location in their request, select global.",
					Required = true,
					Type = FunctionArgumentType.String,
				},
			],
			_ => [],
		};
	}

	public async ValueTask<bool> TryHandleActionInference(
		ChatMessageData? message,
		ServerActionMessage serverActionMessage,
		CancellationToken cancellationToken
	)
	{
		var cancelReply = true;
		try
		{
			if (serverActionMessage.ContextKey != VoxtaModule.ServiceName)
				return false;
			if (serverActionMessage.Role != ChatMessageRole.User)
				return false;

			cancelReply = IsCancelReplyEnabled(serverActionMessage.Value);
			string? location;
			string? loc;

			switch (serverActionMessage.Value)
			{
				case "get_weather":
					location = await ResolveLocationNameAsync(
					serverActionMessage.TryGetArgument("get_weather_location", out loc) ? loc : null,
					"No weather data available as the user location is not set and no location was specified.",
					cancelReply,
					cancellationToken);

					if (location != null)
						await GetWeather(location, cancelReply, cancellationToken);
					return true;
				case "get_weather_forecast":
					location = await ResolveLocationNameAsync(
						serverActionMessage.TryGetArgument("get_forecast_location", out loc) ? loc : null,
						"No weather forecast data available as the user location is not set and no location was specified.",
						cancelReply,
						cancellationToken);

					if (location != null)
						await GetWeatherForecast(location, cancelReply, cancellationToken);
					return true;
				case "get_air_pollution":
					location = await ResolveLocationNameAsync(
						serverActionMessage.TryGetArgument("get_air_pollution_location", out loc) ? loc : null,
						"No air pollution data available as the location is not set and no location was specified.",
						cancelReply,
						cancellationToken);

					if (location != null)
						await GetAirPollution(location, cancelReply, cancellationToken);
					return true;
				case "get_air_pollution_forecast":
					location = await ResolveLocationNameAsync(
						serverActionMessage.TryGetArgument("get_air_pollution_forecast_location", out loc) ? loc : null,
						"No air pollution forecast data available as the user location is not set and no location was specified.",
						cancelReply,
						cancellationToken);

					if (location != null)
						await GetAirPollutionForecast(location, cancelReply, cancellationToken);
					return true;
				case "get_weather_map":
					var mapRequest = ResolveMapRequest(serverActionMessage);
					await GetWeatherMapAsync(mapRequest.Target, mapRequest.Layer, cancelReply, cancellationToken);
					return true;
				default:
					return false;
			}
		}
		catch (Exception exc) when (exc is not OperationCanceledException)
		{
			logger.LogError(exc, "Unexpected OpenWeather action failure for {Action}", serverActionMessage.Value);
			await SendActionFeedbackAsync("OpenWeather hit an unexpected error while handling that request. Please try again.", cancelReply, cancellationToken);
			return true;
		}
	}

	private bool IsCancelReplyEnabled(string actionName)
	{
		return chatAugmentationsSettings.Actions
			.FirstOrDefault(action => string.Equals(action.Name, actionName, StringComparison.OrdinalIgnoreCase))
			?.CancelReply is { } value
			? OpenWeatherChatAugmentationsService.ParseActionBoolean(value, true)
			: true;
	}

	private async Task GetWeather(string location, bool cancelReply, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {Location}", location);
		location = CleanLocationString(location);

		try
		{
			var weatherData = await client.FetchWeatherData(location, chatAugmentationsSettings.Units, cancellationToken);
			if (!weatherData.Success)
			{
				logger.LogWarning("No weather data returned for {Location}", location);
				await SendFailureAsync(weatherData.UserVisibleError, cancelReply, cancellationToken);
				return;
			}

			var data = weatherData.Value!;
			var rain = data.Rain?.OneHour ?? 0;
			var rainPrecipitationText = rain > 0
				? $" and estimated {rain:0.#} mm/h precipitation of rain"
				: "";

			var snow = data.Snow?.OneHour ?? 0;
			var snowPrecipitationText = snow > 0
				? $" and estimated {snow:0.#} mm/h precipitation of snow"
				: "";

			var unitSuffix = chatAugmentationsSettings.Units == "imperial" ? "°F" : "°C";

			string messageText;
			var sb = new StringBuilder();

			if (_weatherDetails.Contains("Temp"))
				sb.Append($"The current temperature in {location} ({data.Sys.Country}) is {FormatWholeNumber(data.Main.Temp)}{unitSuffix} ");
			else
				sb.Append($"The current weather in {location} ({data.Sys.Country}) is ");
			if (_weatherDetails.Contains("FeelsLike"))
				sb.Append($"(feels like {FormatWholeNumber(data.Main.FeelsLike)}{unitSuffix}) ");
			if (_weatherDetails.Contains("Condition"))
				sb.Append($"with {data.Weather[0].Description}{rainPrecipitationText}{snowPrecipitationText}. ");
			if (_weatherDetails.Contains("TempMinMax"))
				sb.Append($"The temperature ranges between {FormatWholeNumber(data.Main.TempMin)}-{FormatWholeNumber(data.Main.TempMax)}{unitSuffix}. ");
			if (_weatherDetails.Contains("Wind"))
				sb.Append($"Wind: {data.Wind.Speed:0.#} m/s at {FormatWholeNumber(data.Wind.Deg)}°. ");
			if (_weatherDetails.Contains("CloudCover"))
				sb.Append($"Cloud cover: {data.Clouds.All}%. ");
			if (_weatherDetails.Contains("Visibility") && data.Visibility is { } visibility)
				sb.Append($"Visibility: {FormatWholeNumber(visibility / 1000.0)} km. ");

			messageText = sb.ToString().Trim();

			await SendActionResultAsync(messageText, cancelReply, cancellationToken);
		}
		catch (Exception exc)
		{
			logger.LogError(exc, "Failed to fetch weather data");
			await SendActionFeedbackAsync("OpenWeather hit an unexpected error while fetching weather data. Please try again.", cancelReply, cancellationToken);
		}
	}

	private async ValueTask GetWeatherForecast(string location, bool cancelReply, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {location}", location);
		location = CleanLocationString(location);

		try
		{
			var forecast = await client.FetchForecastData(location, chatAugmentationsSettings.Units, cancellationToken);
			if (!forecast.Success)
			{
				logger.LogWarning("No weather forecast data returned for {Location}", location);
				await SendFailureAsync(forecast.UserVisibleError, cancelReply, cancellationToken);
				return;
			}

			var unitSuffix = chatAugmentationsSettings.Units == "imperial" ? "°F" : "°C";
			var data = forecast.Value!;
			var summaryText = WeatherForecastSummariser.Summarise(data.List, _culture, _weatherDetails, days: 5, unitSuffix);

			var introText = $"Weather forecast for {location} ({data.City.Country}):";
			var messageText = $"{introText}\n{summaryText}";

			await SendActionResultAsync(messageText, cancelReply, cancellationToken);
		}
		catch (Exception exc)
		{
			logger.LogError(exc, "Failed to fetch weather forecast data");
			await SendActionFeedbackAsync("OpenWeather hit an unexpected error while fetching forecast data. Please try again.", cancelReply, cancellationToken);
		}
	}


	private async ValueTask GetAirPollution(string location, bool cancelReply, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {location}", location);
		location = CleanLocationString(location);

		try
		{
			var pollutionData = await client.FetchAirPollutionData(location, cancellationToken);
			if (!pollutionData.Success)
			{
				logger.LogWarning("No pollution data returned for {Location}", location);
				await SendFailureAsync(pollutionData.UserVisibleError, cancelReply, cancellationToken);
				return;
			}

			var data = pollutionData.Value!;
			var aqi = data.List[0].Main.Aqi;
			var components = data.List[0].Components;

			string messageText;
			var sb = new StringBuilder();
			sb.Append($"Current air quality in {location}: ");

			if (_pollutionDetails.Contains("AQI"))
			{
				sb.Append($"AQI {aqi} ({AirPollutionForecastSummariser.GetAqiLabel(aqi)}) ");
			}
			if (_pollutionDetails.Contains("CO"))
				sb.Append($"CO: {components.Co:0.#} µg/m³, ");
			if (_pollutionDetails.Contains("NO"))
				sb.Append($"NO: {components.No:0.#} µg/m³, ");
			if (_pollutionDetails.Contains("NO2"))
				sb.Append($"NO₂: {components.No2:0.#} µg/m³, ");
			if (_pollutionDetails.Contains("O3"))
				sb.Append($"O₃: {components.O3:0.#} µg/m³, ");
			if (_pollutionDetails.Contains("SO2"))
				sb.Append($"SO₂: {components.So2:0.#} µg/m³, ");
			if (_pollutionDetails.Contains("PM2.5"))
				sb.Append($"PM2.5: {components.Pm2_5:0.#} µg/m³, ");
			if (_pollutionDetails.Contains("PM10"))
				sb.Append($"PM10: {components.Pm10:0.#} µg/m³, ");
			if (_pollutionDetails.Contains("NH3"))
				sb.Append($"NH₃: {components.Nh3:0.#} µg/m³, ");

			messageText = sb.ToString().Trim().TrimEnd(',');

			await SendActionResultAsync(messageText, cancelReply, cancellationToken);
		}
		catch (Exception exc)
		{
			logger.LogError(exc, "Failed to fetch air pollution data");
			await SendActionFeedbackAsync("OpenWeather hit an unexpected error while fetching air pollution data. Please try again.", cancelReply, cancellationToken);
		}
	}

	private async ValueTask GetAirPollutionForecast(string location, bool cancelReply, CancellationToken cancellationToken)
	{
		logger.LogInformation("Identified city name: {location}", location);
		location = CleanLocationString(location);

		try
		{
			var forecastData = await client.FetchAirPollutionForecastData(location, cancellationToken);
			if (!forecastData.Success)
			{
				logger.LogWarning("No forecast pollution data returned for {Location}", location);
				await SendFailureAsync(forecastData.UserVisibleError, cancelReply, cancellationToken);
				return;
			}

			var summaryText = AirPollutionForecastSummariser.Summarise(forecastData.Value!.List, _culture, _pollutionDetails, days: 5);
			var introText = $"Air pollution forecast for {location}:";
			var messageText = $"{introText}\n{summaryText}";

			await SendActionResultAsync(messageText, cancelReply, cancellationToken);
		}
		catch (Exception exc)
		{
			logger.LogError(exc, "Failed to fetch air pollution forecast data");
			await SendActionFeedbackAsync("OpenWeather hit an unexpected error while fetching air pollution forecast data. Please try again.", cancelReply, cancellationToken);
		}
	}

	private async ValueTask GetWeatherMapAsync(
		(MapTargetType Type, string Identifier) target,
		string normalizedLayer,
		bool cancelReply,
		CancellationToken cancellationToken)
	{
		OpenWeatherResult<byte[]> bytes;
		try
		{
			bytes = await client.FetchWeatherMapAsync(target, normalizedLayer, cacheDir, cancellationToken);
		}
		catch (Exception exc) when (exc is not OperationCanceledException)
		{
			logger.LogError(exc, "Failed to generate weather map");
			await SendActionFeedbackAsync("OpenWeather hit an unexpected error while generating the weather map. Please try again.", cancelReply, cancellationToken);
			return;
		}

		if (!bytes.Success || bytes.Value is not { Length: > 0 })
		{
			logger.LogWarning("No weather map could be generated for {Target}", target.Identifier);
			await SendFailureAsync(bytes.UserVisibleError, cancelReply, cancellationToken);
			return;
		}

		var image = new BytesImage("image/png", bytes.Value, ComputerVisionSource.Screen)
		{
			FileName = $"weathermap_{normalizedLayer}_{target.Identifier}.png"
		};

		var message =
			$"OpenWeather fetched the weather map for {target.Identifier} ({WeatherMapHelper.ToDisplayName(normalizedLayer)}).";
		await SendActionFeedbackAsync(message, cancelReply, cancellationToken);
		await session.SendNoteAttachmentAsync(
			"Weather map",
			image,
			cancellationToken
		);
	}

	private async Task<string?> ResolveLocationNameAsync(
		string? providedLocation,
		string missingLocationMessage,
		bool cancelReply,
		CancellationToken cancellationToken)
	{
		var location = !string.IsNullOrWhiteSpace(providedLocation)
			? providedLocation
			: chatAugmentationsSettings.MyLocation;

		if (string.IsNullOrWhiteSpace(location))
		{
			logger.LogInformation("Location is not set!");
			await SendActionFeedbackAsync(missingLocationMessage, cancelReply, cancellationToken);
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

	private ((MapTargetType Type, string Identifier) Target, string Layer) ResolveMapRequest(ServerActionMessage message)
	{
		var namedLayer = GetSafeArgument(message, "layer");
		var namedLocation = GetSafeArgument(message, "location");
		string? selectedLayer = null;
		string? selectedLocation = null;

		if (WeatherMapHelper.TryNormalizeLayer(namedLayer, out var layerFromLayerArg))
			selectedLayer = layerFromLayerArg;

		if (WeatherMapHelper.TryNormalizeLayer(namedLocation, out var layerFromLocationArg))
		{
			selectedLayer ??= layerFromLocationArg;
			if (TryResolveMapTarget(namedLayer, out _))
				selectedLocation = namedLayer;
		}
		else if (TryResolveMapTarget(namedLocation, out _))
		{
			selectedLocation = namedLocation;
		}

		if (selectedLocation == null && TryResolveMapTarget(namedLayer, out _))
			selectedLocation = namedLayer;

		foreach (var value in GetActionArgumentValues(message))
		{
			if (selectedLayer == null && WeatherMapHelper.TryNormalizeLayer(value, out var layerFromValue))
			{
				selectedLayer = layerFromValue;
				continue;
			}

			if (selectedLocation == null && TryResolveMapTarget(value, out _))
				selectedLocation = value;
		}

		return (ResolveMapTarget(selectedLocation), selectedLayer ?? "temp_new");
	}

	private bool TryResolveMapTarget(string? providedLocation, out (MapTargetType Type, string Identifier) target)
	{
		target = ResolveMapTarget(providedLocation);
		return !string.IsNullOrWhiteSpace(providedLocation) &&
			   (target.Type != MapTargetType.Global ||
				string.Equals(target.Identifier, "Global", StringComparison.OrdinalIgnoreCase) &&
				IsGlobalMapTarget(providedLocation));
	}

	private static bool IsGlobalMapTarget(string? value)
	{
		return string.Equals(value?.Trim(), "global", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(value?.Trim(), "world", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(value?.Trim(), "earth", StringComparison.OrdinalIgnoreCase);
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
			   IsSafeArgumentValue(value)
			? value
			: null;
	}

	private static bool IsSafeArgumentValue(string? value)
	{
		return !string.IsNullOrWhiteSpace(value) &&
			   !string.Equals(value, "undefined", StringComparison.OrdinalIgnoreCase) &&
			   !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);
	}

	private static IEnumerable<string> GetActionArgumentValues(ServerActionMessage message)
	{
		var argumentsProperty = message.GetType().GetProperty("Arguments");
		var arguments = argumentsProperty?.GetValue(message);

		if (arguments is JsonElement jsonElement)
		{
			foreach (var jsonValue in GetJsonArgumentValues(jsonElement))
				yield return jsonValue;
			yield break;
		}

		if (arguments is not IEnumerable enumerable || arguments is string)
			yield break;

		foreach (var item in enumerable)
		{
			foreach (var value in GetObjectArgumentValues(item))
				yield return value;
		}
	}

	private static IEnumerable<string> GetObjectArgumentValues(object? item)
	{
		if (item == null)
			yield break;

		if (item is string stringValue)
		{
			if (IsSafeArgumentValue(stringValue))
				yield return stringValue;
			yield break;
		}

		if (item is JsonElement jsonElement)
		{
			foreach (var jsonValue in GetJsonArgumentValues(jsonElement))
				yield return jsonValue;
			yield break;
		}

		var valueProperty = item.GetType().GetProperty("Value");
		var propertyValue = valueProperty?.GetValue(item);
		if (propertyValue is string itemValue && IsSafeArgumentValue(itemValue))
			yield return itemValue;
	}

	private static IEnumerable<string> GetJsonArgumentValues(JsonElement jsonElement)
	{
		if (jsonElement.ValueKind == JsonValueKind.Object)
		{
			foreach (var property in jsonElement.EnumerateObject())
			{
				if (property.Value.ValueKind == JsonValueKind.String &&
					IsSafeArgumentValue(property.Value.GetString()))
				{
					yield return property.Value.GetString()!;
				}
			}
		}
		else if (jsonElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in jsonElement.EnumerateArray())
			{
				foreach (var value in GetJsonArgumentValues(item))
					yield return value;
			}
		}
		else if (jsonElement.ValueKind == JsonValueKind.String && IsSafeArgumentValue(jsonElement.GetString()))
		{
			yield return jsonElement.GetString()!;
		}
	}

	private static string FormatWholeNumber(double value)
	{
		return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
	}

	private async Task SendFailureAsync(string? userVisibleError, bool cancelReply, CancellationToken cancellationToken)
	{
		await SendActionFeedbackAsync(string.IsNullOrWhiteSpace(userVisibleError)
			? "OpenWeather could not complete the request. Check the module settings or try again later."
			: userVisibleError, cancelReply, cancellationToken);
	}

	private async Task SendActionResultAsync(string message, bool cancelReply, CancellationToken cancellationToken)
	{
		if (cancelReply)
		{
			await session.SendNoteAsync(message, cancellationToken);
			return;
		}

		await session.SendSecretAsync(message, cancellationToken);
	}

	private async Task SendActionFeedbackAsync(string message, bool cancelReply, CancellationToken cancellationToken)
	{
		if (cancelReply)
		{
			await session.SendNoteAsync(message, cancellationToken);
			return;
		}

		await session.SendSecretAsync(message, cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}
