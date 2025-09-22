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
        var config = new OpenWeatherChatAugmentationSettings
        {
            MyLocation = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.MyLocation),
            Units = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.Units),
            WeatherExpertMode = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.WeatherExpertMode),
            PollutionExpertMode = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.PollutionExpertMode),
        };
        logger.LogInformation("Chat session {SessionId} has been augmented with {Augmentation}", session.SessionId, VoxtaModule.AugmentationKey);
        return new OpenWeatherChatAugmentationsServiceInstance(session, client, config, logger);
    }
}