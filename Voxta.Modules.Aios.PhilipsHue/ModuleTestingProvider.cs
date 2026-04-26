using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Connections;
using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Abstractions.Utils;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Modules.Aios.PhilipsHue.Clients;
using Voxta.Modules.Aios.PhilipsHue.Configuration;

namespace Voxta.Modules.Aios.PhilipsHue;

public class ModuleTestingProvider(
    IUserInteractionRequestsManager userInteractionRequestsManager,
    IIVoxtaWebsocketBroadcasterFactory broadcastFactory,
    ILoggerFactory loggerFactory,
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
        try
        {
            var authPath = PhilipsHueAuthPath.GetUserAuthPath(
                settings.GetRequired(ModuleConfigurationProvider.AuthPath),
                auth.UserId);

            var ws = broadcastFactory.Create(auth.UserId);
            var interaction = new TestHueUserInteractionWrapper(userInteractionRequestsManager, ws, moduleId);
            var bridgeConnectionService = new HueBridgeConnectionService(
                loggerFactory.CreateLogger<HueBridgeConnectionService>(),
                interaction,
                authPath);
            var dataService = new HueDataService(bridgeConnectionService, loggerFactory.CreateLogger<HueDataService>());
            var commandService = new HueCommandService(bridgeConnectionService, dataService, loggerFactory.CreateLogger<HueCommandService>());
            var entityMatchingService = new HueEntityMatchingService(dataService, loggerFactory.CreateLogger<HueEntityMatchingService>());
            var colorConverterService = new ColorConverterService(loggerFactory.CreateLogger<ColorConverterService>());

            var manager = new HueManager(
                bridgeConnectionService,
                dataService,
                commandService,
                entityMatchingService,
                colorConverterService,
                loggerFactory.CreateLogger<HueManager>());

            var connected = await manager.InitializeAsync(cancellationToken);
            if (!connected)
            {
                return
                [
                    new ModuleTestResultItem
                    {
                        Success = false,
                        Message = manager.LastUserVisibleError ?? $"Failed to connect to Philips Hue bridge. State: {manager.State}",
                    }
                ];
            }

            return
            [
                new ModuleTestResultItem
                {
                    Success = true,
                    Message = $"Successfully connected to Philips Hue bridge. Found {manager.GetLights().Count} lights, {manager.GetRooms().Count} rooms, {manager.GetZones().Count} zones.",
                }
            ];
        }
        catch (Exception exc)
        {
            logger.LogError(exc, "Failed to connect to Philips Hue bridge");
            return
            [
                new ModuleTestResultItem
                {
                    Success = false,
                    Message = "Failed to connect to Philips Hue bridge: " + exc.Message,
                }
            ];
        }
    }
}

public class TestHueUserInteractionWrapper(
    IUserInteractionRequestsManager userInteractionRequestsManager,
    IVoxtaWebsocketBroadcaster ws,
    Guid moduleId) : IHueUserInteractionWrapper
{
    public async Task<IUserInteractionRequestToken> RequestUserInteraction(CancellationToken cancellationToken)
    {
        var request = await userInteractionRequestsManager.RequestUserInteractionAsync(cancellationToken);
        ws.SendToUser(new ServerUserInteractionRequestMessage
        {
            RequestId = request.RequestId,
            ModuleId = moduleId,
            Message = "Please press the link button on your Hue bridge to authorize the connection.",
        });
        return request;
    }

    public Task SetBridgeConnectionStateAsync(bool connected, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
