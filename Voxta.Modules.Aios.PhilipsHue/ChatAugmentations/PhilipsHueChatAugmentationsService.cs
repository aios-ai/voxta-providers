using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Security;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Modules.Aios.PhilipsHue.Configuration;

namespace Voxta.Modules.Aios.PhilipsHue.ChatAugmentations;

public class PhilipsHueChatAugmentationsService(
    ILoggerFactory loggerFactory
) : ServiceBase(loggerFactory.CreateLogger<PhilipsHueChatAugmentationsService>()), IChatAugmentationsService
{
    public async Task<IChatAugmentationServiceInstanceBase[]> CreateInstanceAsync(
        IChatSessionChatAugmentationApi session,
        IAuthenticationContext auth,
        CancellationToken cancellationToken
    )
    {
        await using var instances = new ChatAugmentationServiceInitializationHolder();
        instances.Add(await CreatePhilipsHueChatAugmentationsServiceInstance(session, cancellationToken));
        return instances.Acquire();
    }

    private async Task<PhilipsHueChatAugmentationsServiceInstance?> CreatePhilipsHueChatAugmentationsServiceInstance(IChatSessionChatAugmentationApi session, CancellationToken cancellationToken)
    {
        if (!session.IsAugmentationEnabled(VoxtaModule.AugmentationKey))
            return null;
        var logger = loggerFactory.CreateLogger<PhilipsHueChatAugmentationsServiceInstance>();
        logger.LogInformation("Chat session {SessionId} has been augmented with {Augmentation}", session.SessionId, VoxtaModule.AugmentationKey);
        var config = new HueConfig
        {
            Ip = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.BridgeIp),
            Username = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.BridgeUsername),
            CharacterControlledLight = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.CharacterControlledLight)
        };
        var manager = new HueManager(session, loggerFactory.CreateLogger<HueManager>());
        await manager.InitializeBridgeAsync(cancellationToken);
        return new PhilipsHueChatAugmentationsServiceInstance(session, manager, config, logger);
    }
}