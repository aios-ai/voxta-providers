using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;
using Voxta.Modules.Aios.OpenWeather.ChatAugmentations;
using System.Text.Json;

namespace Voxta.Modules.Aios.OpenWeather.Configuration;

public class ModuleConfigurationProvider : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public static string[] FieldsRequiringReload => [ApiKey.Name, Actions.Name];
    private static readonly string DefaultTileCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voxta",
        "Aios.OpenWeather");

    public static readonly FormPasswordField ApiKey = new()
    {
        Name = "ApiKey",
        Label = "API Key",
        Required = true,
        //language=markdown
        Text = "Create an API key at [openweathermap.org](https://openweathermap.org/api).",
    };

    public static readonly FormTextField MyLocation = new()
    {
        Name = "MyLocation",
        Label = "My Location",
        DefaultValue = "",
        Text = "A default location if you want the AI to know where you are. E.g. 'New York, United States' or 'Berlin, Germany'.",
    };

    public static readonly FormChoicesField Units = new()
    {
        Name = "Units",
        Label = "Units",
        Choices =
        [
            new FormChoice { Value = "metric", Label = "Metric (Celsius, m/s)"},
            new FormChoice { Value = "imperial", Label = "Imperial (Fahrenheit, miles/hour)"},
        ],
        DefaultValue = "metric",
    };

    public static readonly FormMultipleChoicesField WeatherDetails = new()
    {
        Name = "WeatherDetails",
        Label = "Weather details to show",
        Choices =
        [
            new() { Label = "Temp", Value = "Temp", Metadata = new FormChoiceMetadata { Subtitle = "Current temperature." } },
            new() { Label = "FeelsLike", Value = "FeelsLike", Metadata = new FormChoiceMetadata { Subtitle = "Feels-like temperature." } },
            new() { Label = "Precipitation", Value = "Precipitation", Metadata = new FormChoiceMetadata { Subtitle = "Rain and snow precipitation." } },
            new() { Label = "TempMinMax", Value = "TempMinMax", Metadata = new FormChoiceMetadata { Subtitle = "Minimum and maximum temperature." } },
            new() { Label = "Wind", Value = "Wind", Metadata = new FormChoiceMetadata { Subtitle = "Wind speed and direction." } },
            new() { Label = "CloudCover", Value = "CloudCover", Metadata = new FormChoiceMetadata { Subtitle = "Cloud coverage percentage." } },
            new() { Label = "Visibility", Value = "Visibility", Metadata = new FormChoiceMetadata { Subtitle = "Visibility in kilometers." } },
        ],
        StartValue = ["Temp", "TempMinMax", "Precipitation"],
    };

    public static readonly FormMultipleChoicesField PollutionDetails = new()
    {
        Name = "PollutionDetails",
        Label = "Air pollution details to show",
        Choices =
        [
            new() { Label = "AQI", Value = "AQI", Metadata = new FormChoiceMetadata { Subtitle = "Air Quality Index." } },
            new() { Label = "CO", Value = "CO", Metadata = new FormChoiceMetadata { Subtitle = "Carbon monoxide." } },
            new() { Label = "NO", Value = "NO", Metadata = new FormChoiceMetadata { Subtitle = "Nitric oxide." } },
            new() { Label = "NO2", Value = "NO2", Metadata = new FormChoiceMetadata { Subtitle = "Nitrogen dioxide." } },
            new() { Label = "O3", Value = "O3", Metadata = new FormChoiceMetadata { Subtitle = "Ozone." } },
            new() { Label = "SO2", Value = "SO2", Metadata = new FormChoiceMetadata { Subtitle = "Sulfur dioxide." } },
            new() { Label = "PM2.5", Value = "PM2.5", Metadata = new FormChoiceMetadata { Subtitle = "Particulate matter smaller than 2.5 micrometers." } },
            new() { Label = "PM10", Value = "PM10", Metadata = new FormChoiceMetadata { Subtitle = "Particulate matter smaller than 10 micrometers." } },
            new() { Label = "NH3", Value = "NH3", Metadata = new FormChoiceMetadata { Subtitle = "Ammonia." } },
        ],
        StartValue = ["AQI", "PM2.5", "PM10", "NO2", "O3"],
    };

    public static readonly OpenWeatherActionSettings[] DefaultActions =
    [
        new()
        {
            Name = "get_weather",
            Layer = "Weather",
            ShortDescription = "get the latest weather, temperature or rain data",
            Description = "When {{ user }} asks for the weather temperature, rain or snow.",
            MatchFilter = @"\b(?:weather|temperature|temperatures|rain|raining|rains|snow|snowing|snows)\b(?![^.]*\b(?:forecast|outlook|next|tomorrow|weekend|days?|hours?)\b)",
            CancelReply = "false",
        },
        new()
        {
            Name = "get_weather_forecast",
            Layer = "Weather",
            ShortDescription = "Get the weather forecast and timeframe",
            Description = "When {{ user }} asks for the weather forecast for a certain period of time.",
            MatchFilter = @"\b(?:forecast|next|tomorrow|weekend|days|hours)\b",
            CancelReply = "false",
        },
        new()
        {
            Name = "get_air_pollution",
            Layer = "Weather",
            ShortDescription = "Get the current air pollution data",
            Description = "When {{ user }} asks for the current air quality or pollution.",
            MatchFilter = @"\b(?:air\s?quality|pollution|AQI|air\s?pollution)\b(?![^.]*\b(?:forecast|next|tomorrow|weekend|days?|hours?)\b)",
            CancelReply = "false",
        },
        new()
        {
            Name = "get_air_pollution_forecast",
            Layer = "Weather",
            ShortDescription = "Get the air pollution forecast",
            Description = "When {{ user }} asks for the air quality or pollution forecast.",
            MatchFilter = @"\b(?:air\s?quality\s?forecast|pollution\s?forecast|AQI\s?forecast)\b",
            CancelReply = "false",
        },
        new()
        {
            Name = "get_weather_map",
            Layer = "Weather",
            ShortDescription = "Get a weather map for a given location and layer",
            Description = "When {{ user }} asks to see a weather map (e.g. temperature, clouds, wind, pressure, precipitation).",
            MatchFilter = @"\b(?:map|radar|satellite|clouds|temperature|wind|pressure|precipitation|rain|snow)\b",
            CancelReply = "false",
        },
    ];

    public static readonly FormArrayOfObjectsField Actions = new()
    {
        Name = "Actions",
        Label = "Actions",
        Text = "Configure OpenWeather actions exposed to action inference.",
        AllowAddRemove = false,
        AllowRename = false,
        AllowDisable = true,
        UniqueFields = [nameof(OpenWeatherActionSettings.Name)],
        FieldTemplate =
        [
            new FormTextField
            {
                Name = nameof(OpenWeatherActionSettings.Name),
                Label = "Name",
                Required = true,
            },
            new FormTextField
            {
                Name = nameof(OpenWeatherActionSettings.ShortDescription),
                Label = "Short Description",
                Required = true,
            },
            new FormMultilineField
            {
                Name = nameof(OpenWeatherActionSettings.Description),
                Label = "Description",
                Required = true,
            },
            new FormMultilineField
            {
                Name = nameof(OpenWeatherActionSettings.MatchFilter),
                Label = "Match Filter",
                Text = "One regular expression per line.",
                Required = true,
            },
            new FormBooleanField
            {
                Name = nameof(OpenWeatherActionSettings.CancelReply),
                Label = "Cancel Reply",
                DefaultValue = false,
            },
        ],
        DefaultValue = JsonSerializer.Serialize(DefaultActions),
    };

    public static readonly FormTextField TileCachePath = new()
    {
        Name = "TileCachePath",
        Label = "Tile Cache Path",
        Required = true,
        Text = "The path to store the OpenStreetMap tile cache.",
        DefaultValue = DefaultTileCachePath,
        Advanced = true,
    };

    public Task<FormField[]> GetModuleConfigurationFieldsAsync(
         IAuthenticationContext auth,
         ISettingsSource settings,
         CancellationToken cancellationToken
     )
    {
        var fields = FormBuilder.Build(
            FormDocumentationField.Create(
                //language=markdown
                """
               For this provider to work we rely on a free external API service: [openweathermap.org](https://openweathermap.org/). They have a free plan which is rate limited to max 60 calls per minute.

               1. Open the URL and register
               2. Once you are registered and signed in, go to your profile and click on "My API keys"
               3. Give your key a custom name and hit Generate (It can take a while till the API key is activated, check your emails)
               """),
            ApiKey,
            MyLocation,
            Units,
            Actions,
            WeatherDetails,
            PollutionDetails,
            TileCachePath
        );
        return Task.FromResult(fields);
    }
}
