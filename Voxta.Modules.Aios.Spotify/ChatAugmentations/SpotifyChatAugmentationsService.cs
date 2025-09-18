using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Encryption;
using Voxta.Abstractions.Security;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Modules.Aios.Spotify.Clients.Handlers;
using Voxta.Modules.Aios.Spotify.Clients.Services;
using Voxta.Modules.Aios.Spotify.Configuration;

namespace Voxta.Modules.Aios.Spotify.ChatAugmentations;

public class SpotifyChatAugmentationsService(
    ILocalEncryptionProvider localEncryptionProvider,
    ISpotifyManagerFactory spotifyManagerFactory,
    ILoggerFactory loggerFactory
) : ServiceBase(loggerFactory.CreateLogger<SpotifyChatAugmentationsService>()), IChatAugmentationsService
{
    public async Task<IChatAugmentationServiceInstanceBase[]> CreateInstanceAsync(
        IChatSessionChatAugmentationApi session,
        IAuthenticationContext auth,
        CancellationToken cancellationToken
    )
    {
        await using var instances = new ChatAugmentationServiceInitializationHolder();
        instances.Add(await CreateSpotifyChatAugmentationsServiceInstance(session, cancellationToken));
        return instances.Acquire();
    }

    private async Task<SpotifyChatAugmentationsServiceInstance?> CreateSpotifyChatAugmentationsServiceInstance(IChatSessionChatAugmentationApi session, CancellationToken cancellationToken)
    {
        if (!session.IsAugmentationEnabled(VoxtaModule.AugmentationKey))
            return null;
        var logger = loggerFactory.CreateLogger<SpotifyChatAugmentationsServiceInstance>();
        logger.LogInformation("Chat session {SessionId} has been augmented with {Augmentation}", session.SessionId, VoxtaModule.AugmentationKey);
        var config = new SpotifyChatAugmentationSettings
        {
            MatchFilterWakeWord = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.MatchFilterWakeWord),
            EnableMatchFilter = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.EnableMatchFilter),
            EnableCharacterReplies = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.EnableCharacterReplies),
        };
        var spotifyManagerConfig = new SpotifyManagerConfig
        {
            ClientId = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.ClientId),
            ClientSecret = localEncryptionProvider.Decrypt(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.ClientSecret)),
            RedirectUri = new Uri(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.RedirectUri)),
            TokenPath = Path.GetFullPath(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.TokenPath)),
        };
        var spotifyManager = await spotifyManagerFactory.CreateSpotifyManager(spotifyManagerConfig, cancellationToken);

        var spotifySearchService = new SpotifySearchService(spotifyManager, loggerFactory.CreateLogger<SpotifySearchService>());
        await spotifySearchService.InitializeAsync();

        var spotifyPlaybackMonitor = new SpotifyPlaybackMonitor(spotifyManager, session, loggerFactory.CreateLogger<SpotifyPlaybackMonitor>(), config.EnableCharacterReplies);
        
        var spotifyActionHandler = new SpotifyActionHandler(spotifyManager, spotifySearchService, session, loggerFactory.CreateLogger<SpotifyActionHandler>(), () => spotifyPlaybackMonitor.PlaybackState, config.EnableCharacterReplies);

        var instance = new SpotifyChatAugmentationsServiceInstance(
            session,
            config,
            spotifyPlaybackMonitor,
            spotifyActionHandler
        );
        try
        {
            instance.Initialize();
        }
        catch
        {
            await instance.DisposeAsync();
            throw;
        }
        return instance;
    }
}