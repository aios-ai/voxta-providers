using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<Guid, byte> InventoryNotesSentBySessionId = new();

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
        
        var authPath = PhilipsHueAuthPath.GetUserAuthPath(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.AuthPath), auth.UserId);

        var config = new PhilipsHueChatAugmentationsSettings
        {
            Actions = NormalizeActions(
                ModuleConfiguration.GetOptional<PhilipsHueActionSettings>(ModuleConfigurationProvider.Actions),
                ModuleConfiguration.GetOptional(ModuleConfigurationProvider.CharacterControlledLight)),
            Ip = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.BridgeIp),
            Username = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.BridgeUsername),
            CharacterControlledLight = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.CharacterControlledLight),
            AuthPath = authPath,
            SendInventoryAtSessionStart = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.SendInventoryAtSessionStart)
        };
        
        var hueUserInteractionWrapper = new HueUserInteractionWrapper(session);
        
        var colorConverterService = new ColorConverterService(loggerFactory.CreateLogger<ColorConverterService>());
        var bridgeConnectionService = new HueBridgeConnectionService(loggerFactory.CreateLogger<HueBridgeConnectionService>(), hueUserInteractionWrapper, config.AuthPath);
        var dataService = new HueDataService(bridgeConnectionService, loggerFactory.CreateLogger<HueDataService>());
        var commandService = new HueCommandService(bridgeConnectionService, dataService, loggerFactory.CreateLogger<HueCommandService>());
        var entityMatchingService = new HueEntityMatchingService(dataService, loggerFactory.CreateLogger<HueEntityMatchingService>());

        var manager = new HueManager(
            bridgeConnectionService,
            dataService,
            commandService,
            entityMatchingService,
            colorConverterService,
            loggerFactory.CreateLogger<HueManager>());
        
        await manager.InitializeAsync(cancellationToken);

        var instance = new PhilipsHueChatAugmentationsServiceInstance(session, manager, config, logger);
        await TrySendHueInventoryNoteOncePerSessionAsync(session, instance, config, cancellationToken);
        return instance;
    }

    private static PhilipsHueActionSettings[] NormalizeActions(PhilipsHueActionSettings[]? actions, string? characterControlledLight)
    {
        if (actions is not { Length: > 0 })
            actions = ModuleConfigurationProvider.DefaultActions;

        var defaultsByName = ModuleConfigurationProvider.DefaultActions.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        return actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Name) && defaultsByName.ContainsKey(action.Name))
            .Select(action =>
            {
                var defaultAction = defaultsByName[action.Name];
                return new PhilipsHueActionSettings
                {
                    Name = defaultAction.Name,
                    ShortDescription = string.IsNullOrWhiteSpace(action.ShortDescription) ? defaultAction.ShortDescription : action.ShortDescription,
                    Description = string.IsNullOrWhiteSpace(action.Description) ? defaultAction.Description : action.Description,
                    MatchFilter = NormalizeMultilineSetting(action.MatchFilter, defaultAction.MatchFilter),
                    FlagsFilter = string.IsNullOrWhiteSpace(action.FlagsFilter) ? defaultAction.FlagsFilter : action.FlagsFilter,
                    Disabled = NormalizeDisabled(action, characterControlledLight),
                    CancelReply = action.CancelReply,
                };
            })
            .ToArray();
    }

    private static string? NormalizeDisabled(PhilipsHueActionSettings action, string? characterControlledLight)
    {
        if (!string.Equals(action.Name, "show_emotion", StringComparison.OrdinalIgnoreCase))
            return action.Disabled;

        return string.IsNullOrWhiteSpace(action.Disabled) && string.IsNullOrWhiteSpace(characterControlledLight)
            ? "true"
            : action.Disabled;
    }

    private static string? NormalizeMultilineSetting(string? value, string? fallback)
    {
        var values = (value ?? "")
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return values.Length == 0 ? fallback : string.Join(Environment.NewLine, values);
    }

    public static bool ParseActionBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static Task TrySendHueInventoryNoteOncePerSessionAsync(
        IChatSessionChatAugmentationApi session,
        PhilipsHueChatAugmentationsServiceInstance instance,
        PhilipsHueChatAugmentationsSettings config,
        CancellationToken cancellationToken)
    {
        if (!config.SendInventoryAtSessionStart)
            return Task.CompletedTask;

        return InventoryNotesSentBySessionId.TryAdd(session.SessionId, 0)
            ? instance.SendHueInventoryNoteAsync(cancellationToken)
            : Task.CompletedTask;
    }
}
