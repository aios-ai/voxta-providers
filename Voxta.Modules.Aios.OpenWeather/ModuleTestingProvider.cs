using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Encryption;
using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Modules.Aios.OpenWeather.Clients;
using Voxta.Modules.Aios.OpenWeather.Configuration;

namespace Voxta.Modules.Aios.OpenWeather;

public class ModuleTestingProvider(
    IOpenWeatherClientFactory openWeatherClientFactory,
    ILocalEncryptionProvider localEncryptionProvider,
    ILogger<ModuleTestingProvider> logger
    ) : IVoxtaModuleTestingProvider
{
    public async Task<ModuleTestResultItem[]> TestModuleAsync(
        IAuthenticationContext auth,
        Guid moduleId,
        ISettingsSource settings,
        CancellationToken cancellationToken
        )
    {
        var apiKey = localEncryptionProvider.Decrypt(settings.GetRequired(ModuleConfigurationProvider.ApiKey));
        var client = openWeatherClientFactory.CreateClient(apiKey);
        try
        {
            var weatherData = await client.FetchWeatherData("New York, United States", "imperial", cancellationToken);
            return
            [
                new ModuleTestResultItem
                {
                    Success = true,
                    Message = $"Successfully fetched weather data for New York, United States: {weatherData.Weather[0].Description}, {weatherData.Main.Temp}°F",
                }
            ];
        }
        catch (Exception exc)
        {
            logger.LogError(exc, "Failed to fetch weather data");
            return
            [
                new ModuleTestResultItem
                {
                    Success = false,
                    Message = "Failed to fetch weather data: " + exc.Message,
                }
            ];
        }

    }
}