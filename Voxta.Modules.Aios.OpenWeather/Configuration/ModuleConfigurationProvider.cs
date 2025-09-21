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
    
    public static readonly FormBooleanField ExpertMode = new()
    {
        Name = "ExpertMode",
        Label = "Show full air pollution details",
        DefaultValue = false,
        Text = "When enabled, the AI will display all available pollutant concentrations (CO, NO, NO₂, O₃, SO₂, PM2.5, PM10, NH₃) in air quality reports. " +
               "When disabled, only the most common values (AQI, PM2.5, PM10, NO₂, O₃) will be shown.",
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
           ExpertMode
       );
        return Task.FromResult(fields);
    }
}
