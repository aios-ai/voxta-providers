using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Utils;
using Voxta.Modules.Aios.Spotify.Clients.Models;

namespace Voxta.Modules.Aios.Spotify.Clients.Services;

public interface ISpotifyUserInteractionWrapper
{
    CancellationToken Abort { get; }
    Task<IUserInteractionRequestToken> RequestUserInteraction(Uri url, CancellationToken cancellationToken);
}

public class SpotifyUserInteractionWrapper(IChatSessionChatAugmentationApi session) : ISpotifyUserInteractionWrapper
{
    public CancellationToken Abort => session.Chat.Abort;

    public Task<IUserInteractionRequestToken> RequestUserInteraction(Uri url, CancellationToken cancellationToken)
    {
        return session.RequestUserAction(new UserInteractionRequestInput
        {
            Message = "Please authorize the Spotify integration by visiting the following URL in your browser:",
            Url = url.ToString(),
        }, cancellationToken);
    }
}

public class SpotifyManagerConfig
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required Uri RedirectUri { get; init; }
    public required string TokenPath { get; init; }
}

public interface ISpotifyManagerFactory
{
    Task<ISpotifyManager> CreateSpotifyManager(ISpotifyUserInteractionWrapper userInteractionWrapper, SpotifyManagerConfig config, CancellationToken cancellationToken);
}
    
public class SpotifyManagerFactory(
    ISpotifyAuthCallbackManager spotifyAuthCallbackManager,
    ILoggerFactory loggerFactory
) : ISpotifyManagerFactory
{
    public async Task<ISpotifyManager> CreateSpotifyManager(ISpotifyUserInteractionWrapper userInteractionWrapper, SpotifyManagerConfig config, CancellationToken cancellationToken)
    {
        var tokenFolder = Path.GetDirectoryName(Path.GetFullPath(config.TokenPath)) ?? throw new InvalidOperationException("Token path is invalid");
        if (!Directory.Exists(tokenFolder))
            Directory.CreateDirectory(tokenFolder);

        var logger = loggerFactory.CreateLogger<SpotifyManager>();
        var spotifyManager = new SpotifyManager(spotifyAuthCallbackManager, userInteractionWrapper, config, logger);
        await spotifyManager.InitializeSpotifyClient(cancellationToken);
            
        if (!spotifyManager.HasClient)
        {
            logger.LogError("Spotify client could not be initialized.");
            throw new InvalidOperationException("Spotify client could not be initialized.");
        }

        return spotifyManager;
    }
}

public interface ISpotifyManager
{
    Task<CurrentlyPlayingContext?> GetCurrentPlaybackState(CancellationToken cancellationToken);
    Task<Paging<FullTrack>?> GetUsersTopTracks(CancellationToken cancellationToken);
    Task ControlSpotifyPlayback(bool playback, CancellationToken cancellationToken);
    Task PlaySpecificUri(string uri, CancellationToken cancellationToken, string? type = null);
    Task QueueTrack(string uri, CancellationToken cancellationToken);
    Task<SearchResponse?> SearchSpotify(string query, SearchRequest.Types type, string? market = null);
    Task<string?> GetSpotifyUserIdAsync();
    Task<string?> GetUserMarketAsync();
    Task<bool> ChangeVolume(int volumePercent, CancellationToken cancellationToken);
    Task SkipToPreviousOrNextTrack(string skipToPrevious, CancellationToken cancellationToken);
    Task SeekPlayback(int positionMs, CancellationToken cancellationToken);
    Task SetShuffle(bool shuffleState, CancellationToken cancellationToken);
    Task SetRepeatMode(string repeatMode, CancellationToken cancellationToken);
    Task<Dictionary<string, string>> ListAvailablePlaylists(CancellationToken cancellationToken);
    Task AddItems(string playlistId, PlaylistAddItemsRequest request, CancellationToken cancellationToken);
    Task AddTrackToLibraryAsync(string trackId, string trackFriendlyName, CancellationToken cancellationToken);
    Task<Dictionary<string, string>> ListAvailableDevices(CancellationToken cancellationToken);
    Task TransferPlayback(string deviceId, CancellationToken cancellationToken);
}
    
public class SpotifyManager(
    ISpotifyAuthCallbackManager spotifyAuthCallbackManager,
    ISpotifyUserInteractionWrapper userInteractionWrapper,
    SpotifyManagerConfig config,
    ILogger<SpotifyManager> logger) : ISpotifyManager
{
    private SpotifyClient? _spotifyClient;
    private SpotifyAuthToken? _spotifyAuthToken;

    public async Task InitializeSpotifyClient(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initialize Spotify Client");
        var accessToken = await GetValidAccessTokenAsync(cancellationToken);
        if (accessToken != null)
        {
            _spotifyClient = new SpotifyClient(accessToken);
            logger.LogInformation("Spotify client authenticated and created successfully");
            logger.LogWarning("Note: This plugin acts solely as an interface between Voxta and your Spotify player. You must have an active Spotify device or playback session that the plugin can connect to and control. Once connected, you can pause and resume playback freely, until the device becomes inactive for a certain period of time.");
        }
        else
        {
            logger.LogError("Failed to initialize Spotify client. Access token could not be retrieved.");
        }
    }

    public bool HasClient => _spotifyClient != null;

    private async Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await LoadTokenAsync();

        if (token == null || IsTokenExpired(token))
        {
            if (token?.RefreshToken != null)
            {
                try
                {
                    token = await RefreshTokenAsync(token.RefreshToken);
                    await SaveTokenAsync(token);
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed to refresh token: {ExMessage}", ex.Message);
                    token = null;
                }
            }
            if (config == null)
            {
                throw new InvalidOperationException("_spotifyConfig null.");
            }

            if (token == null)
            {
                var auth = new OAuthClient();
                var loginRequest = new LoginRequest(
                    config.RedirectUri,
                    config.ClientId,
                    LoginRequest.ResponseType.Code
                )
                {
                    Scope =
                    [
                        Scopes.UserReadPrivate,
                        Scopes.UserReadPlaybackState,
                        Scopes.UserModifyPlaybackState,
                        Scopes.UserTopRead,
                        Scopes.PlaylistReadPrivate,
                        Scopes.PlaylistModifyPrivate,
                        Scopes.PlaylistReadCollaborative,
                        Scopes.UserLibraryModify,
                        Scopes.UserLibraryRead
                    ]

                };

                var authUri = loginRequest.ToUri();
                var code = await GetAuthCodeAsync(authUri, cancellationToken);

                var tokenRequest = new AuthorizationCodeTokenRequest(
                    config.ClientId,
                    config.ClientSecret,
                    code,
                    config.RedirectUri
                );
                var response = await auth.RequestToken(tokenRequest, cancellationToken);

                token = new SpotifyAuthToken
                {
                    AccessToken = response.AccessToken,
                    RefreshToken = response.RefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn)
                };

                await SaveTokenAsync(token);
            }
        }
        return token.AccessToken;
    }

    private async Task<string> GetAuthCodeAsync(Uri authUri, CancellationToken cancellationToken)
    {
        try
        {
            var codeTask = spotifyAuthCallbackManager.WaitForCodeAsync(cancellationToken);
            await using var userInteractionToken = await userInteractionWrapper.RequestUserInteraction(authUri, cancellationToken);

            logger.LogInformation("Waiting for Spotify authentication...");

            await Task.WhenAny(userInteractionToken.Task, codeTask);

            if (!codeTask.IsCompleted)
                throw new OperationCanceledException("User interaction was cancelled or timed out.");

            var code = await codeTask;

            logger.LogInformation("Authorization code received!");

            return code;
        }
        finally
        {
            spotifyAuthCallbackManager.Release();
        }
    }

    private bool IsTokenExpired(SpotifyAuthToken token)
    {
        return DateTime.UtcNow >= token.ExpiresAt;
    }

    private async Task SaveTokenAsync(SpotifyAuthToken token)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(token, options);
        await File.WriteAllTextAsync(config.TokenPath, json);
    }

    private async Task<SpotifyAuthToken> RefreshTokenAsync(string refreshToken)
    {
        var auth = new OAuthClient();
        var refreshRequest = new AuthorizationCodeRefreshRequest(config.ClientId, config.ClientSecret, refreshToken);

        var response = await auth.RequestToken(refreshRequest);

        return new SpotifyAuthToken
        {
            AccessToken = response.AccessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn)
        };
    }

    private async Task<SpotifyAuthToken?> LoadTokenAsync()
    {
        if (!File.Exists(config.TokenPath))
            return null;

        var json = await File.ReadAllTextAsync(config.TokenPath);
        _spotifyAuthToken = JsonSerializer.Deserialize<SpotifyAuthToken>(json);
        return JsonSerializer.Deserialize<SpotifyAuthToken>(json);
    }

    public async Task<CurrentlyPlayingContext?> GetCurrentPlaybackState(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
            {
                logger.LogError("Spotify client not valid. Playback state cannot be retrieved.");
                return null;
            }

            return await _spotifyClient.Player.GetCurrentPlayback(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving playback state: {ExMessage}", ex.Message);
            return null;
        }
    }

    public async Task<Paging<FullTrack>?> GetUsersTopTracks(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
            {
                logger.LogError("Spotify client not valid. Playback state cannot be retrieved.");
                return null;
            }

            return await _spotifyClient.Personalization.GetTopTracks(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving top tracks: {ExMessage}", ex.Message);
            return null;
        }
    }

    public async Task ControlSpotifyPlayback(bool playback, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Playback cannot be controlled.");
            return;
        }

        if (playback)
        {
            await _spotifyClient.Player.ResumePlayback(cancellationToken);
            logger.LogInformation("Playback resumed.");
        }
        else
        {
            await _spotifyClient.Player.PausePlayback(cancellationToken);
            logger.LogInformation("Playback paused.");
        }
    }

    public async Task PlaySpecificUri(string uri, CancellationToken cancellationToken, string? type = null)
    {
        try
        {
            if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
            {
                logger.LogError("Spotify client not valid. Cannot play specific URI.");
                return;
            }

            var request = new PlayerResumePlaybackRequest();

            if (type == "track")
                request.Uris = new List<string> { uri };
            else
                request.ContextUri = uri;

            await _spotifyClient.Player.ResumePlayback(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError("Error starting playback: {ExMessage}", ex.Message);
        }
    }

    public async Task QueueTrack(string uri, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot queue track.");
            return;
        }

        await _spotifyClient.Player.AddToQueue(new PlayerAddToQueueRequest(uri), cancellationToken);
    }

    public async Task<SearchResponse?> SearchSpotify(string query, SearchRequest.Types type, string? market = null)
    {
        if (_spotifyClient == null)
            throw new InvalidOperationException("Spotify client not initialized.");

        var request = new SearchRequest(type, query)
        {
            Market = market
        };

        return await _spotifyClient.Search.Item(request);
    }

    public async Task<string?> GetSpotifyUserIdAsync()
    {
        if (_spotifyClient == null)
            throw new InvalidOperationException("Spotify client not initialized.");

        try
        {
            var me = await _spotifyClient.UserProfile.Current();
            logger.LogInformation("Retrieved Spotify user ID: {MeId}", me.Id);
            return me.Id;
        }
        catch (APIException ex)
        {
            logger.LogError(ex, "Failed to retrieve Spotify user ID.");
            return null;
        }
    }

    public async Task<string?> GetUserMarketAsync()
    {
        if (_spotifyClient == null)
            throw new InvalidOperationException("Spotify client not initialized.");

        try
        {
            var me = await _spotifyClient.UserProfile.Current();
            logger.LogInformation("Retrieved user market: {MeCountry}", me.Country);
            return me.Country;
        }
        catch (APIException ex)
        {
            logger.LogError(ex, "Failed to retrieve user profile for market detection.");
            return null;
        }
    }

    public async Task<bool> ChangeVolume(int volumePercent, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot change volume.");
            return false;
        }

        await _spotifyClient.Player.SetVolume(new PlayerVolumeRequest(volumePercent), cancellationToken);
        logger.LogInformation("Volume set to {VolumePercent}%", volumePercent);
        return true;
    }

    public async Task SkipToPreviousOrNextTrack(string skipToPrevious, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot skip track.");
            return;
        }

        if (skipToPrevious == "previous")
        {
            await _spotifyClient.Player.SkipPrevious(cancellationToken);
            logger.LogInformation("Skipped to previous track");
        }
        else
        {
            await _spotifyClient.Player.SkipNext(cancellationToken);
            logger.LogInformation("Skipped to next track");
        }
    }

    public async Task SeekPlayback(int positionMs, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot seek playback.");
            return;
        }

        await _spotifyClient.Player.SeekTo(new PlayerSeekToRequest(positionMs), cancellationToken);
        logger.LogInformation("Playback position set to {PositionMs} ms.", positionMs);
    }

    public async Task SetShuffle(bool shuffleState, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot set shuffle mode.");
            return;
        }

        await _spotifyClient.Player.SetShuffle(new PlayerShuffleRequest(shuffleState), cancellationToken);
        logger.LogInformation("Shuffle mode set to {Off}", shuffleState ? "on" : "off");
    }

    public async Task SetRepeatMode(string repeatMode, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot set repeat mode.");
            return;
        }
        PlayerSetRepeatRequest.State repeatState;

        switch (repeatMode.ToLower())
        {
            case "track":
                repeatState = PlayerSetRepeatRequest.State.Track;
                break;
            case "context":
                repeatState = PlayerSetRepeatRequest.State.Context;
                break;
            case "off":
                repeatState = PlayerSetRepeatRequest.State.Off;
                break;
            default:
                logger.LogError("Invalid repeat mode: {RepeatMode}", repeatMode);
                return;
        }

        var request = new PlayerSetRepeatRequest(repeatState);
        var result = await _spotifyClient.Player.SetRepeat(request, cancellationToken);
        if (result)
        {
            logger.LogInformation("Repeat mode set to {RepeatMode}", repeatMode);
        }
        else
        {
            logger.LogError("Failed to set repeat mode to {RepeatMode}.", repeatMode);
        }
    }

    public async Task<Dictionary<string, string>> ListAvailablePlaylists(CancellationToken cancellationToken)
    {
        var playlistMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot list playlists.");
            return playlistMap;
        }

        var page = await _spotifyClient.Playlists.CurrentUsers(cancellationToken);

        while (page.Items is { Count: > 0 })
        {
            foreach (var playlist in page.Items)
            {
                if (string.IsNullOrEmpty(playlist.Name) || string.IsNullOrEmpty(playlist.Uri))
                    continue;

                playlistMap[playlist.Name] = playlist.Uri;
            }

            if (string.IsNullOrEmpty(page.Next))
                break;

            page = await _spotifyClient.NextPage(page);
        }

        return playlistMap;
    }

    public async Task AddItems(string playlistId, PlaylistAddItemsRequest request, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot add items to playlist.");
            return;
        }

        try
        {
            await _spotifyClient.Playlists.AddItems(playlistId, request, cancellationToken);
            logger.LogInformation("Track added to playlist: {PlaylistId}", playlistId);
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to add track to playlist: {ExMessage}", ex.Message);
        }
    }

    public async Task AddTrackToLibraryAsync(string trackId, string trackFriendlyName,
        CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot save track to library.");
            return;
        }

        try
        {
            await _spotifyClient.Library.SaveTracks(
                new LibrarySaveTracksRequest([trackId]), cancellationToken);

            logger.LogInformation("Track {TrackFriendlyName} added to Liked Songs.", trackFriendlyName);
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to save track to library: {ExMessage}", ex.Message);
        }
    }

    public async Task<Dictionary<string, string>> ListAvailableDevices(CancellationToken cancellationToken)
    {
        var deviceMap = new Dictionary<string, string>();

        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot list available devices.");
            return deviceMap;
        }

        var response = await _spotifyClient.Player.GetAvailableDevices(cancellationToken);
        if (response.Devices.Count > 0)
        {
            foreach (var device in response.Devices)
            {
                deviceMap[device.Name] = device.Id;
            }
        }
        else
        {
            logger.LogWarning("No devices available.");
        }

        return deviceMap;
    }

    public async Task TransferPlayback(string deviceId, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot transfer playback.");
            return;
        }

        await _spotifyClient.Player.TransferPlayback(new PlayerTransferPlaybackRequest(new List<string> { deviceId }), cancellationToken);
        logger.LogInformation("Playback transferred to device: {DeviceId}", deviceId);
    }

    private async Task<bool> EnsureValidSpotifyClient(CancellationToken cancellationToken)
    {
        var newToken = await GetValidAccessTokenAsync(cancellationToken);

        if (newToken == null)
        {
            logger.LogError("Unable to refresh access token. Spotify client cannot be used.");
            return false;
        }

        if (_spotifyAuthToken == null)
        {
            throw new InvalidOperationException("_spotifyAuthToken null");
        }

        if (_spotifyClient == null || _spotifyAuthToken.AccessToken != newToken)
        {
            _spotifyClient = new SpotifyClient(newToken);
        }

        return true;
    }
}