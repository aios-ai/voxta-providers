using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;

namespace Voxta.Modules.Aios.OpenWeather.Configuration;

public class ModuleConfigurationProvider : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public static string[] FieldsRequiringReload => [ApiKey.Name];
    
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
        DefaultValue = "imperial",
    };
    
    public static readonly FormMultipleChoicesField WeatherDetails = new()
    {
        Name = "WeatherDetails",
        Label = "Weather details to show",
        Text = """
               Available keys:

               - `Temp` = current temperature  
               - `FeelsLike` = feels-like temperature  
               - `Precipitation` = rain/snow  
               - `TempMinMax` = min/max temperature  
               - `Wind` = speed and direction  
               - `CloudCover` = percentage  
               - `Visibility` = in km
               """,
        Choices =
        [
            new() { Label = "Temp", Value = "Temp" },
            new() { Label = "FeelsLike", Value = "FeelsLike" },
            new() { Label = "Precipitation", Value = "Precipitation" },
            new() { Label = "TempMinMax", Value = "TempMinMax" },
            new() { Label = "Wind", Value = "Wind" },
            new() { Label = "CloudCover", Value = "CloudCover" },
            new() { Label = "Visibility", Value = "Visibility" },
        ],
        StartValue = ["Temp", "TempMinMax", "Precipitation"],
    };
    
    public static readonly FormMultipleChoicesField PollutionDetails = new()
    {
        Name = "PollutionDetails",
        Label = "Air pollution details to show",
        Text = """
               Available keys:

               - `AQI` = Air Quality Index  
               - `CO` = Carbon monoxide  
               - `NO` = Nitric oxide  
               - `NO2` = Nitrogen dioxide  
               - `O3` = Ozone  
               - `SO2` = Sulfur dioxide  
               - `PM2.5` = Particulate matter <2.5µm  
               - `PM10` = Particulate matter <10µm  
               - `NH3` = Ammonia
               """,
        Choices =
        [
            new() { Label = "AQI", Value = "AQI" },
            new() { Label = "CO", Value = "CO" },
            new() { Label = "NO", Value = "NO" },
            new() { Label = "NO2", Value = "NO2" },
            new() { Label = "O3", Value = "O3" },
            new() { Label = "SO2", Value = "SO2" },
            new() { Label = "PM2.5", Value = "PM2.5" },
            new() { Label = "PM10", Value = "PM10" },
            new() { Label = "NH3", Value = "NH3" },
        ],
        StartValue = ["AQI", "PM2.5", "PM10", "NO2", "O3"],
    };
    
    public static readonly FormTextField TileCachePath = new()
    {
        Name = "TileCachePath",
        Label = "Tile Cache Path",
        Required = true,
        Text = "The path to store the OpenStreetMap tile cache.",
        DefaultValue = @"%LOCALAPPDATA%\Voxta\Aios.OpenWeather",
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
           WeatherDetails,
           PollutionDetails,
           TileCachePath
       );
        return Task.FromResult(fields);
    }
}