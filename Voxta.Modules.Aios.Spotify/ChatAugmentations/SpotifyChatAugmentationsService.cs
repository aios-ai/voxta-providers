using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
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
        
        var rawPlaylists = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.SpecialPlaylists) ?? "";
        var playlistMap = rawPlaylists
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            .ToDictionary(
                parts => StringUtils.NormaliseSpecialName(parts[0]),
                parts => parts[1]
            );
        
        var config = new SpotifyChatAugmentationsSettings
        {
            Actions = NormalizeActions(ModuleConfiguration.GetOptional<SpotifyActionSettings>(ModuleConfigurationProvider.Actions)),
            MatchFilterWakeWord = ModuleConfiguration.GetOptional(ModuleConfigurationProvider.MatchFilterWakeWord),
            SpeechDuckingVolumePercent = ModuleConfiguration.GetRequired(ModuleConfigurationProvider.SpeechDuckingVolumePercent),
            SpecialPlaylists = playlistMap
        };

        var tokenPath = SpotifyAuthPath.GetUserTokenPath(ModuleConfiguration.GetRequired(ModuleConfigurationProvider.TokenPath), Auth.UserId);
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

        var spotifyPlaybackMonitor = new SpotifyPlaybackMonitor(spotifyManager, session, loggerFactory.CreateLogger<SpotifyPlaybackMonitor>());
        
        var spotifyActionHandler = new SpotifyActionHandler(spotifyManager, spotifySearchService, session, config, loggerFactory.CreateLogger<SpotifyActionHandler>(), () => spotifyPlaybackMonitor.PlaybackState, spotifyPlaybackMonitor.SetLastActionAsync);
        
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

    private static SpotifyActionSettings[] NormalizeActions(SpotifyActionSettings[]? actions)
    {
        if (actions is not { Length: > 0 })
            return ModuleConfigurationProvider.DefaultActions;

        var defaultsByName = ModuleConfigurationProvider.DefaultActions.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        return actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Name) && defaultsByName.ContainsKey(action.Name))
            .Select(action =>
            {
                var defaultAction = defaultsByName[action.Name];
                return new SpotifyActionSettings
                {
                    Name = defaultAction.Name,
                    Layer = defaultAction.Layer,
                    ShortDescription = string.IsNullOrWhiteSpace(action.ShortDescription) ? defaultAction.ShortDescription : action.ShortDescription,
                    Description = string.IsNullOrWhiteSpace(action.Description) ? defaultAction.Description : action.Description,
                    MatchFilter = NormalizeMultilineSetting(action.MatchFilter, defaultAction.MatchFilter),
                    FlagsFilter = string.IsNullOrWhiteSpace(action.FlagsFilter) ? defaultAction.FlagsFilter : action.FlagsFilter,
                    Disabled = action.Disabled,
                    CancelReply = action.CancelReply,
                };
            })
            .ToArray();
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
}
