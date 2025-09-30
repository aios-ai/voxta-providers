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
        Text = "Create an API key at <a href=\"https://openweathermap.org/api/\" target=\"_blank\" rel=\"external\">openweathermap.org</a>.",
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
            new FormChoicesField.Choice{ Value = "metric", Label = "Metric (Celsius, m/s)"},
            new FormChoicesField.Choice{ Value = "imperial", Label = "Imperial (Fahrenheit, miles/hour)"},
        ],
        DefaultValue = "imperial",
    };
    
    public static readonly FormMultilineField WeatherDetails = new()
    {
        Name = "WeatherDetails",
        Label = "Weather details to show",
        Text = "Available keys. One per line: " +
               "<code>Temp FeelsLike Precipitation TempMinMax Wind CloudCover Visibility</code> " +
               "(Explanations: Precipitation = rain/snow; Temp = current temperature; " +
               "FeelsLike = feels-like temperature; TempMinMax = min/max temperature; Wind = speed and direction; " +
               "CloudCover = percentage; Visibility = in km.)",
        Rows = 7,
        DefaultValue =
@"Temp
TempMinMax
Precipitation"
    };
    
    public static readonly FormMultilineField PollutionDetails = new()
    {
        Name = "PollutionDetails",
        Label = "Air pollution details to show",
        Text = "Available keys. One per line: " +
               "<code>AQI CO NO NO2 O3 SO2 PM2.5 PM10 NH3</code> " +
               "(Explanations: AQI = Air Quality Index; CO = Carbon monoxide; NO = Nitric oxide; " +
               "NO2 = Nitrogen dioxide; O3 = Ozone; SO2 = Sulfur dioxide; PM2.5 = Particulate matter <2.5µm; " +
               "PM10 = Particulate matter <10µm; NH3 = Ammonia.)",
        Rows = 9,
        DefaultValue = 
@"AQI
PM2.5
PM10
NO2
O3"
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
               """
               For this provider to work we rely on a free external API service: <a href="https://openweathermap.org/" target="blank" rel="external">openweathermap.org</a>. They have a free plan which is rate limited to max 60 calls per minute.

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