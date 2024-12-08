using Microsoft.Extensions.Logging;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Providers.Host;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;

// This example shows how to create commands that the AI can call to retrieve weather data
// This will slow down the AI since there will be an LLM run before generating text.
namespace Voxta.SampleProviderApp.Providers
{
	public class AppConfig
	{
		public string? apiKey { get; set; }
		public string? myLocation { get; set; }
		public string? units { get; set; }
	}

    public class UserFunctionProviderWeather : ProviderBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UserFunctionProviderWeather> _logger;
		private AppConfig? _config;

        public UserFunctionProviderWeather(
            IRemoteChatSession session,
            ILogger<UserFunctionProviderWeather> logger,
            IHttpClientFactory httpClientFactory)
            : base(session, logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }
		
		
		// Call on Chat start
		protected override async Task OnStartAsync()
		{
            // Load configuration from .json
            LoadConfiguration();
			if (_config == null)
			{
				_logger.LogError("Configuration not loaded properly.");
				return;
			}
	
			await base.OnStartAsync();
						
            // Use the configuration values
            string? apiKey = _config.apiKey;
            string? myLocation = _config.myLocation;
			string? units = _config.units;

			// Register our action
			Send(new ClientUpdateContextMessage
			{
				SessionId = SessionId,
				ContextKey = "WeatherFunction",
				Actions =
				[
					// Define custom actions
					new()
					{
						// The name used by the LLM to select the action. Make sure to select a clear name.
						Name = "get_weather",
						 // A short description of the action to be included in the functions list, typically used for character action inference
						ShortDescription = "get the latest weather, temperature or rain data",
						// The condition for executing this function
						Description = "When {{ user }} asks for the weather temperature or rain in a specific location.",
						// This match will ensure user action inference is only going to be triggered if this regex matches the message.
						// For example, if you use "please" in all functions, this can avoid running user action inference at all unless
						// the user said "please".
						MatchFilter = new string[]
						{
							@"(?i)\b(weather|temperature|temperatures|rain|raining|rains|snow|snowing|snows)\b"
						},
						// Only run in response to the user messages 
						Timing = FunctionTiming.AfterUserMessage,
						// Do not generate a response, we will instead handle the action ourselves
						CancelReply = true,
						// Only allow this for characters with the assistant field enabled
						AssistantFilter = true,
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
						Name = "get_weather_myLocation",
						ShortDescription = "get the latest weather, temperature or rain data for users current location",
						Description = "When {{ user }} asks for the weather, temperature or rain data in his current location.",
						MatchFilter = new string[]
						{
							@"(?i)\b(weather|temperature|temperatures|rain|raining|rains|snow|snowing|snows)\b"
						},
						Timing = FunctionTiming.AfterUserMessage,
						CancelReply = true,
						AssistantFilter = true,
					},
				]
			});
			
			// Act when an action is called
			HandleMessage<ServerActionMessage>(async message =>
			{
				if (_config == null || string.IsNullOrWhiteSpace(_config.apiKey))
				{
					_logger.LogError("API key or configuration is missing. Open the config file and set the variable.");
					Send(new ClientSendMessage
					{
						SessionId = SessionId,
						DoUserActionInference = false,
						Text = "/event apiKey is not set, open the provider file and set the variable."
					});
					return;
				}
				
				// We only want to handle user actions
				if (message.Role != ChatMessageRole.User) return;
				
				switch (message.Value)
				{
					case "get_weather":
						_logger.LogInformation("Action called for a specific location");
						if (!message.TryGetArgument("get_weather_location", out var location) || string.IsNullOrWhiteSpace(location))
							{
							_logger.LogInformation("Could not identify the city name.");
							Send(new ClientSendMessage
							{
								SessionId = SessionId,
								// We want to avoid a loop!
								DoUserActionInference = false,
								Text = "/event Could not identify the city name."
							});
						}
						else
						{
							await FetchAndSendWeather(location);
							
						}
						break;
					case "get_weather_myLocation":
					_logger.LogInformation("Action called for my location");
						if (string.IsNullOrWhiteSpace(_config.myLocation))
						{
							_logger.LogInformation("User location is not set! Open the config file and set the variable.");
							Send(new ClientSendMessage
							{
								SessionId = SessionId,
								// We want to avoid a loop!
								DoUserActionInference = false,
								Text = "/event No weather data available as the user location is not set, open the config file and set the variable."
							});
						}
						else
						{
							await FetchAndSendWeather(_config.myLocation);
						}
						break;
				}
			});
		}
        private void LoadConfiguration()
        {
		var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Providers\\configs\\UserFunctionProviderWeatherConfig.json");

            if (!File.Exists(configPath))
            {
                _logger.LogError("Configuration file not found at {ConfigPath}", configPath);
                return;
            }

            try
            {
                // Read the file and deserialize into AppConfig object
                var json = File.ReadAllText(configPath);
                _config = JsonSerializer.Deserialize<AppConfig>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error loading configuration: {Message}", ex.Message);
            }
        }

		// Function to call the API endpoint to retrieve the weather data
		public async Task<(bool IsSuccess, JsonElement Data, string ErrorMessage)> FetchWeatherData(string location)
		{
			_logger.LogInformation("Fetching weather data...");

			var httpClient = _httpClientFactory.CreateClient("weatherClient");

			// Fetch API with timeout
			using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
			{
					if (_config == null || string.IsNullOrWhiteSpace(_config.apiKey))
					{
						_logger.LogError("API key or configuration is missing. Open the config file and set the variable.");
						return (false, default, "API key or configuration is missing. Open the config file and set the variable.");
					}
					var url = $"http://api.openweathermap.org/data/2.5/weather?q={location}&appid={_config.apiKey}&units={_config.units}";
					try
					{
						var response = await httpClient.GetAsync(url, cts.Token);
						if (!response.IsSuccessStatusCode)
						{
							_logger.LogError("Failed to fetch weather data. Status Code: {StatusCode}", response.StatusCode);
							return (false, default, $"Failed to fetch weather data. Status Code: {response.StatusCode}");
						}

						var content = await response.Content.ReadAsStringAsync();
						var weatherJson = JsonDocument.Parse(content).RootElement;

						return (true, weatherJson, string.Empty);
					}
					catch (TaskCanceledException ex)
					{
						_logger.LogError("Request timed out: {Message}", ex.Message);
						return (false, default, "Request timed out");
					}
					catch (Exception ex)
					{
						_logger.LogError("Error occurred while fetching weather data: {Message}", ex.Message);
						return (false, default, "An error occurred while fetching weather data");
					}
				}
			
		}

		// Function to remove special characters from the location string
		private	 static string CleanLocationString(string input)
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
		
		private async Task FetchAndSendWeather(string location)
		{
			_logger.LogInformation("Identified city name: {Location}", location);
			location = CleanLocationString(location);

			var (isSuccess, weatherData, errorMessage) = await FetchWeatherData(location);

			// Send the error message as event into the chat, the character will respond to it
			if (!isSuccess)
			{
				Send(new ClientSendMessage
				{
					SessionId = SessionId,
					Text = $"[{errorMessage}]"
				});
				return;
			}

			if (_config == null || string.IsNullOrWhiteSpace(_config.units))
			{
				_logger.LogError("Units parameter or configuration is missing. Open the config file and set the variable.");
				return;
			}
			
			// Extract data from the returned json Object
			//_logger.LogInformation("weatherData: '{weatherData}'", weatherData);
			var temperature = weatherData.GetProperty("main").GetProperty("temp").GetDouble();
			var feels_like = weatherData.GetProperty("main").GetProperty("feels_like").GetDouble();
			var temp_min = weatherData.GetProperty("main").GetProperty("temp_min").GetDouble();
			var temp_max = weatherData.GetProperty("main").GetProperty("temp_max").GetDouble();
			var weatherDescription = weatherData.GetProperty("weather")[0].GetProperty("description").GetString();
			var icon = weatherData.GetProperty("weather")[0].GetProperty("icon").GetString();
			var country = weatherData.GetProperty("sys").GetProperty("country").GetString();

			double rain = 0;
			if (weatherData.TryGetProperty("rain", out var rainProperty) &&
				rainProperty.TryGetProperty("1h", out var rainValue))
			{
				rain = rainValue.GetDouble();
			}

			double snow = 0;
			if (weatherData.TryGetProperty("snow", out var snowProperty) &&
				snowProperty.TryGetProperty("1h", out var snowValue))
			{
				snow = snowValue.GetDouble();
			}

			// Build the message text with optional precipitation data
			string rainPrecipitationText = rain > 0
				? $" and estimated {rain:F1} mm/h precipitation of rain"
				: "";

			// Build the message text with optional snow data
			string snowPrecipitationText = snow > 0
				? $" and estimated {snow:F1} mm/h precipitation of snow"
				: "";

			string unitSuffix = _config.units == "imperial" ? "°F" : "°C";
			string messageText = $"[The current temperature in {location} ({country}) is {temperature}{unitSuffix} with {weatherDescription}{rainPrecipitationText}{snowPrecipitationText}. The temperature ranges between {temp_min}-{temp_max}{unitSuffix}.]";

			// Send the current temperature as event into the chat, the character will respond to it
			Send(new ClientSendMessage
			{
				SessionId = SessionId,
				Text = messageText,
			});
		}
	}
}