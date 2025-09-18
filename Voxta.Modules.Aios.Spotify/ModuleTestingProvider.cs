using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Encryption;
using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Modules.Aios.Spotify.Clients.Services;
using Voxta.Modules.Aios.Spotify.Configuration;

namespace Voxta.Modules.Aios.Spotify;

public class ModuleTestingProvider(
    ILocalEncryptionProvider localEncryptionProvider,
    ISpotifyManagerFactory spotifyManagerFactory,
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
        var config = new SpotifyManagerConfig
        {
            ClientId = settings.GetRequired(ModuleConfigurationProvider.ClientId),
            ClientSecret = localEncryptionProvider.Decrypt(settings.GetRequired(ModuleConfigurationProvider.ClientSecret)),
            RedirectUri = new Uri(settings.GetRequired(ModuleConfigurationProvider.RedirectUri)),
            TokenPath = Path.GetFullPath(settings.GetRequired(ModuleConfigurationProvider.TokenPath)),
        };
        var client = await spotifyManagerFactory.CreateSpotifyManager(config, cancellationToken);
        try
        {
            var spotifyUserId = await client.GetSpotifyUserIdAsync();
            return
            [
                new ModuleTestResultItem
                {
                    Success = true,
                    Message = $"Successfully connect to Spotify as user {spotifyUserId}",
                }
            ];
        }
        catch (Exception exc)
        {
            logger.LogError(exc, "Failed to connect to Spotify");
            return
            [
                new ModuleTestResultItem
                {
                    Success = false,
                    Message = "Failed to connect to Spotify: " + exc.Message,
                }
            ];
        }

    }
}