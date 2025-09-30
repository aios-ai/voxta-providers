using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Encryption;
using Voxta.Abstractions.Security;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Modules.Aios.OpenWeather.Clients;
using Voxta.Modules.Aios.OpenWeather.Configuration;

namespace Voxta.Modules.Aios.OpenWeather.ChatAugmentations;

public class OpenWeatherChatAugmentationsService(
    IOpenWeatherClientFactory clientFactory,
    ILocalEncryptionProvider localEncryptionProvider,
    ILoggerFactory loggerFactory
) : ServiceBase(loggerFactory.CreateLogger<OpenWeatherChatAugmentationsService>()), IChatAugmentationsService
{
    public async Task<IChatAugmentationServiceInstanceBase[]> CreateInstanceAsync(
        IChatSessionChatAugmentationApi session,
        IAuthenticationContext auth,
        CancellationToken cancellationToken
    )
    {
        await using var instances = new ChatAugmentationServiceInitializationHolder();
        instances.Add(CreateOpenWeatherChatAugmentationsServiceInstance(session));
        return instances.Acquire();
    }

    private OpenWeatherChatAugmentationsServiceInstance? CreateOpenWeatherChatAugmentationsServiceInstance(IChatSessionChatAugmentationApi session)
    {
        if (!session.IsAugmentationEnabled(VoxtaModule.AugmentationKey))
            return null;
        var logger = loggerFactory.CreateLogger<OpenWeatherChatAugmentationsServiceInstance>();
        var apiKey = localEncryptionProvider.Decrypt(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.ApiKey));
        var client = clientFactory.CreateClient(apiKey);
        var rawSelectedWeather = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.WeatherDetails) ?? "";
        var rawSelectedPollution = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.PollutionDetails) ?? "";
        var selectedWeather = ParseKeys(rawSelectedWeather, new[] { "Temp" });
        var selectedPollution = ParseKeys(rawSelectedPollution, new[] { "AQI" });
        var tileCachePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.TileCachePath)));
        var config = new OpenWeatherChatAugmentationSettings
        {
            MyLocation = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.MyLocation),
            Units = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.Units),
            WeatherDetails = selectedWeather.ToArray(),
            PollutionDetails = selectedPollution.ToArray(),
            TileCachePath = tileCachePath,
        };
        logger.LogInformation("Chat session {SessionId} has been augmented with {Augmentation}", session.SessionId, VoxtaModule.AugmentationKey);
        return new OpenWeatherChatAugmentationsServiceInstance(session, client, config, logger);
    }
    
    private static HashSet<string> ParseKeys(string? raw, IEnumerable<string> defaults)
    {
        var keys = new HashSet<string>(
            (raw ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s)),
            StringComparer.OrdinalIgnoreCase);
        
        if (keys.Count == 0)
            return defaults.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return keys;
    }

}