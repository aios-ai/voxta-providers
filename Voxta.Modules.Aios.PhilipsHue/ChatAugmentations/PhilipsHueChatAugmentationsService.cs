using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Security;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Modules.Aios.PhilipsHue.Configuration;
using Voxta.Modules.Aios.PhilipsHue.Clients;

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
        instances.Add(await CreatePhilipsHueChatAugmentationsServiceInstance(session, auth, cancellationToken));
        return instances.Acquire();
    }

    private async Task<PhilipsHueChatAugmentationsServiceInstance?> CreatePhilipsHueChatAugmentationsServiceInstance(IChatSessionChatAugmentationApi session, IAuthenticationContext auth, CancellationToken cancellationToken)
    {
        if (!session.IsAugmentationEnabled(VoxtaModule.AugmentationKey))
            return null;
        var logger = loggerFactory.CreateLogger<PhilipsHueChatAugmentationsServiceInstance>();
        logger.LogInformation("Chat session {SessionId} has been augmented with {Augmentation}", session.SessionId, VoxtaModule.AugmentationKey);
        
        var authPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.AuthPath)));
        if (!authPath.EndsWith(".json")) throw new InvalidOperationException("AuthPath must end with .json");
        authPath = authPath[..^5] + $".{auth.UserId}.json";

        var config = new PhilipsHueChatAugmentationsSettings
        {
            Ip = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.BridgeIp),
            Username = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.BridgeUsername),
            CharacterControlledLight = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.CharacterControlledLight),
            AuthPath = authPath
        };
        
        var hueUserInteractionWrapper = new HueUserInteractionWrapper(session);
        var manager = new HueManager(session, loggerFactory.CreateLogger<HueManager>(), config.AuthPath, hueUserInteractionWrapper);
        await manager.InitializeBridgeAsync(cancellationToken);
        return new PhilipsHueChatAugmentationsServiceInstance(session, manager, config, logger);
    }
}