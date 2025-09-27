using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Configuration;
using Voxta.Abstractions.Encryption;
using Voxta.Abstractions.Security;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Modules.Aios.Spotify.Clients.Handlers;
using Voxta.Modules.Aios.Spotify.Clients.Services;
using Voxta.Modules.Aios.Spotify.Configuration;
using Voxta.Modules.Aios.Spotify.Helpers;

namespace Voxta.Modules.Aios.Spotify.ChatAugmentations;

public class SpotifyChatAugmentationsService(
    ILocalEncryptionProvider localEncryptionProvider,
    ISpotifyManagerFactory spotifyManagerFactory,
    ILoggerFactory loggerFactory,
    IServicesConfigurationsSetResolver servicesConfigurationsSetResolver
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
        
        var rawPlaylists = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.SpecialPlaylists) ?? "";
        var playlistMap = rawPlaylists
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            .ToDictionary(
                parts => StringUtils.NormaliseSpecialName(parts[0]),
                parts => parts[1]
            );
        
        var config = new SpotifyChatAugmentationSettings
        {
            MatchFilterWakeWord = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.MatchFilterWakeWord),
            EnableMatchFilter = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.EnableMatchFilter),
            EnableVolumeControlDuringSpeech = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.EnableVolumeControlDuringSpeech),
            EnableCharacterReplies = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.EnableCharacterReplies),
            SpecialPlaylists = playlistMap
        };

        var tokenPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.TokenPath)));
        if (!tokenPath.EndsWith(".json")) throw new InvalidOperationException("TokenPath must end with .json");
        tokenPath = tokenPath[..^5] + $".{Auth.UserId}.json";
        var spotifyManagerConfig = new SpotifyManagerConfig
        {
            ClientId = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.ClientId),
            ClientSecret = localEncryptionProvider.Decrypt(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.ClientSecret)),
            RedirectUri = new Uri(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.RedirectUri)),
            TokenPath = tokenPath,
        };
        var sessionWrapper = new SpotifyUserInteractionWrapper(session);
        var spotifyManager = await spotifyManagerFactory.CreateSpotifyManager(sessionWrapper, spotifyManagerConfig, cancellationToken);

        var spotifySearchService = new SpotifySearchService(spotifyManager, loggerFactory.CreateLogger<SpotifySearchService>());
        await spotifySearchService.InitializeAsync();

        var spotifyPlaybackMonitor = new SpotifyPlaybackMonitor(spotifyManager, session, loggerFactory.CreateLogger<SpotifyPlaybackMonitor>(), config.EnableCharacterReplies);
        
        var spotifyActionHandler = new SpotifyActionHandler(spotifyManager, spotifySearchService, session, config, loggerFactory.CreateLogger<SpotifyActionHandler>(), () => spotifyPlaybackMonitor.PlaybackState, config.EnableCharacterReplies);
        
        var instance = new SpotifyChatAugmentationsServiceInstance(
            session,
            config,
            spotifyPlaybackMonitor,
            spotifyActionHandler,
            servicesConfigurationsSetResolver
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