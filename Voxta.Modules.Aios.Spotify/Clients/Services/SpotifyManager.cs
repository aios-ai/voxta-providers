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
            logger.LogWarning("Spotify client could not be initialized. The chat session will report the authorization problem to the user.");
        }

        return spotifyManager;
    }
}

public interface ISpotifyManager
{
    string? LastUserVisibleError { get; }
    bool IsAuthorizationRequired { get; }
    Task<CurrentlyPlayingContext?> GetCurrentPlaybackState(CancellationToken cancellationToken);
    Task<Paging<FullTrack>?> GetUsersTopTracks(CancellationToken cancellationToken);
    Task<bool> ControlSpotifyPlayback(bool playback, CancellationToken cancellationToken);
    Task<bool> PlaySpecificUri(string uri, CancellationToken cancellationToken, string? type = null);
    Task<bool> QueueTrack(string uri, CancellationToken cancellationToken);
    Task<SearchResponse?> SearchSpotify(string query, SearchRequest.Types type, string? market = null);
    Task<string?> GetSpotifyUserIdAsync();
    Task<bool> ChangeVolume(int volumePercent, CancellationToken cancellationToken);
    Task<bool> SkipToPreviousOrNextTrack(string skipToPrevious, CancellationToken cancellationToken);
    Task<bool> SeekPlayback(int positionMs, CancellationToken cancellationToken);
    Task<bool> SetShuffle(bool shuffleState, CancellationToken cancellationToken);
    Task<bool> SetRepeatMode(string repeatMode, CancellationToken cancellationToken);
    Task<Dictionary<string, string>> ListAvailablePlaylists(CancellationToken cancellationToken);
    Task<bool> AddItems(string playlistId, PlaylistAddItemsRequest request, CancellationToken cancellationToken);
    Task<bool> AddTrackToLibraryAsync(string trackUri, string trackFriendlyName, CancellationToken cancellationToken);
    Task<Dictionary<string, string>> ListAvailableDevices(CancellationToken cancellationToken);
    Task<bool> TransferPlayback(string deviceId, CancellationToken cancellationToken);
}
    
public class SpotifyManager(
    ISpotifyAuthCallbackManager spotifyAuthCallbackManager,
    ISpotifyUserInteractionWrapper userInteractionWrapper,
    SpotifyManagerConfig config,
    ILogger<SpotifyManager> logger) : ISpotifyManager
{
    private const string AuthorizationRequiredMessage = "Spotify authorization is required. Please open Voxta on this machine and authorize Spotify again.";
    private const string NoActiveDeviceMessage = "No active Spotify device was found. Please start Spotify on your browser, desktop, or mobile app, then try again.";
    private SpotifyClient? _spotifyClient;
    private SpotifyAuthToken? _spotifyAuthToken;
    public string? LastUserVisibleError { get; private set; }
    public bool IsAuthorizationRequired { get; private set; }

    public async Task InitializeSpotifyClient(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initialize Spotify Client");
        var accessToken = await GetValidAccessTokenAsync(cancellationToken);
        if (accessToken != null)
        {
            _spotifyClient = new SpotifyClient(accessToken);
            ClearUserVisibleError();
            logger.LogInformation("Spotify client authenticated and created successfully");
            logger.LogWarning("Note: This plugin acts solely as an interface between Voxta and your Spotify player. You must have an active Spotify device or playback session that the plugin can connect to and control. Once connected, you can pause and resume playback freely, until the device becomes inactive for a certain period of time.");
        }
        else
        {
            logger.LogError("Failed to initialize Spotify client. Access token could not be retrieved.");
            SetAuthorizationRequired();
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
                    logger.LogWarning(ex, "Failed to refresh Spotify token. User authorization is required.");
                    SetAuthorizationRequired();
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
                string code;
                try
                {
                    code = await GetAuthCodeAsync(authUri, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    SetAuthorizationRequired();
                    return null;
                }

                var tokenRequest = new AuthorizationCodeTokenRequest(
                    config.ClientId,
                    config.ClientSecret,
                    code,
                    config.RedirectUri
                );
                AuthorizationCodeTokenResponse response;
                try
                {
                    response = await auth.RequestToken(tokenRequest, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to exchange Spotify authorization code.");
                    SetAuthorizationRequired();
                    return null;
                }

                token = new SpotifyAuthToken
                {
                    AccessToken = response.AccessToken,
                    RefreshToken = response.RefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn)
                };

                await SaveTokenAsync(token);
            }
        }
        ClearUserVisibleError();
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

        try
        {
            var json = await File.ReadAllTextAsync(config.TokenPath);
            _spotifyAuthToken = JsonSerializer.Deserialize<SpotifyAuthToken>(json);
            return _spotifyAuthToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Spotify token. User authorization is required.");
            SetAuthorizationRequired();
            return null;
        }
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
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "retrieve playback state");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving playback state: {ExMessage}", ex.Message);
            LastUserVisibleError = "Spotify playback state could not be retrieved. Please check Spotify and try again.";
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
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "retrieve top tracks");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving top tracks: {ExMessage}", ex.Message);
            LastUserVisibleError = "Spotify top tracks could not be retrieved. Please check Spotify and try again.";
            return null;
        }
    }

    public async Task<bool> ControlSpotifyPlayback(bool playback, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Playback cannot be controlled.");
            return false;
        }

        try
        {
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
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "control playback");
            return false;
        }
    }

    public async Task<bool> PlaySpecificUri(string uri, CancellationToken cancellationToken, string? type = null)
    {
        try
        {
            if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
            {
                logger.LogError("Spotify client not valid. Cannot play specific URI.");
                return false;
            }

            var request = new PlayerResumePlaybackRequest();

            if (type == "track")
                request.Uris = new List<string> { uri };
            else
                request.ContextUri = uri;

            await _spotifyClient.Player.ResumePlayback(request, cancellationToken);
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "start playback");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError("Error starting playback: {ExMessage}", ex.Message);
            LastUserVisibleError = "Spotify could not start playback. Please check the selected item and active device, then try again.";
            return false;
        }
    }

    public async Task<bool> QueueTrack(string uri, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot queue track.");
            return false;
        }

        try
        {
            await _spotifyClient.Player.AddToQueue(new PlayerAddToQueueRequest(uri), cancellationToken);
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "queue track");
            return false;
        }
    }

    public async Task<SearchResponse?> SearchSpotify(string query, SearchRequest.Types type, string? market = null)
    {
        if (!await EnsureValidSpotifyClient(userInteractionWrapper.Abort) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot search Spotify.");
            return null;
        }

        var request = new SearchRequest(type, query)
        {
            Market = market,
            Limit = 10
        };

        try
        {
            var response = await _spotifyClient.Search.Item(request);
            ClearUserVisibleError();
            return response;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "search Spotify");
            return null;
        }
    }

    public async Task<string?> GetSpotifyUserIdAsync()
    {
        if (_spotifyClient == null)
        {
            SetAuthorizationRequired();
            return null;
        }

        try
        {
            var me = await _spotifyClient.UserProfile.Current();
            logger.LogInformation("Retrieved Spotify user ID: {MeId}", me.Id);
            return me.Id;
        }
        catch (APIException ex)
        {
            logger.LogError(ex, "Failed to retrieve Spotify user ID.");
            SetSpotifyApiError(ex, "retrieve Spotify user profile");
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

        try
        {
            await _spotifyClient.Player.SetVolume(new PlayerVolumeRequest(volumePercent), cancellationToken);
            logger.LogInformation("Volume set to {VolumePercent}%", volumePercent);
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "change volume");
            return false;
        }
    }

    public async Task<bool> SkipToPreviousOrNextTrack(string skipToPrevious, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot skip track.");
            return false;
        }

        try
        {
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
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "skip track");
            return false;
        }
    }

    public async Task<bool> SeekPlayback(int positionMs, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot seek playback.");
            return false;
        }

        try
        {
            await _spotifyClient.Player.SeekTo(new PlayerSeekToRequest(positionMs), cancellationToken);
            logger.LogInformation("Playback position set to {PositionMs} ms.", positionMs);
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "seek playback");
            return false;
        }
    }

    public async Task<bool> SetShuffle(bool shuffleState, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot set shuffle mode.");
            return false;
        }

        try
        {
            await _spotifyClient.Player.SetShuffle(new PlayerShuffleRequest(shuffleState), cancellationToken);
            logger.LogInformation("Shuffle mode set to {Off}", shuffleState ? "on" : "off");
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "set shuffle mode");
            return false;
        }
    }

    public async Task<bool> SetRepeatMode(string repeatMode, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot set repeat mode.");
            return false;
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
                LastUserVisibleError = "Invalid Spotify repeat mode. Please use track, context, or off.";
                return false;
        }

        try
        {
            var request = new PlayerSetRepeatRequest(repeatState);
            var result = await _spotifyClient.Player.SetRepeat(request, cancellationToken);
            if (result)
            {
                logger.LogInformation("Repeat mode set to {RepeatMode}", repeatMode);
                ClearUserVisibleError();
                return true;
            }

            logger.LogError("Failed to set repeat mode to {RepeatMode}.", repeatMode);
            LastUserVisibleError = "Spotify did not accept the repeat mode change. Please check the active device and try again.";
            return false;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "set repeat mode");
            return false;
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

        try
        {
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
            ClearUserVisibleError();
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "list playlists");
        }

        return playlistMap;
    }

    public async Task<bool> AddItems(string playlistId, PlaylistAddItemsRequest request, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot add items to playlist.");
            return false;
        }

        try
        {
            await _spotifyClient.Playlists.AddPlaylistItems(playlistId, request, cancellationToken);
            logger.LogInformation("Track added to playlist: {PlaylistId}", playlistId);
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "add track to playlist");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to add track to playlist: {ExMessage}", ex.Message);
            LastUserVisibleError = "Spotify could not add the track to the playlist. Please check the playlist and try again.";
            return false;
        }
    }

    public async Task<bool> AddTrackToLibraryAsync(string trackUri, string trackFriendlyName,
        CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot save track to library.");
            return false;
        }

        try
        {
            await _spotifyClient.Library.SaveItems(
                new LibrarySaveItemsRequest([trackUri]), cancellationToken);

            logger.LogInformation("Track {TrackFriendlyName} added to Liked Songs.", trackFriendlyName);
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "save track to Liked Songs");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to save track to library: {ExMessage}", ex.Message);
            LastUserVisibleError = "Spotify could not save the track to Liked Songs. Please try again.";
            return false;
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

        try
        {
            var response = await _spotifyClient.Player.GetAvailableDevices(cancellationToken);
            if (response.Devices.Count > 0)
            {
                foreach (var device in response.Devices)
                {
                    if (string.IsNullOrEmpty(device.Name) || string.IsNullOrEmpty(device.Id))
                        continue;

                    deviceMap[device.Name] = device.Id;
                }
                ClearUserVisibleError();
            }
            else
            {
                logger.LogWarning("No devices available.");
                LastUserVisibleError = NoActiveDeviceMessage;
            }
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "list available devices");
        }

        return deviceMap;
    }

    public async Task<bool> TransferPlayback(string deviceId, CancellationToken cancellationToken)
    {
        if (!await EnsureValidSpotifyClient(cancellationToken) || _spotifyClient == null)
        {
            logger.LogError("Spotify client not valid. Cannot transfer playback.");
            return false;
        }

        try
        {
            await _spotifyClient.Player.TransferPlayback(new PlayerTransferPlaybackRequest(new List<string> { deviceId }), cancellationToken);
            logger.LogInformation("Playback transferred to device: {DeviceId}", deviceId);
            ClearUserVisibleError();
            return true;
        }
        catch (APIException ex)
        {
            SetSpotifyApiError(ex, "transfer playback");
            return false;
        }
    }

    private async Task<bool> EnsureValidSpotifyClient(CancellationToken cancellationToken)
    {
        var newToken = await GetValidAccessTokenAsync(cancellationToken);

        if (newToken == null)
        {
            logger.LogError("Unable to refresh access token. Spotify client cannot be used.");
            SetAuthorizationRequired();
            return false;
        }

        if (_spotifyAuthToken == null)
        {
            SetAuthorizationRequired();
            return false;
        }

        if (_spotifyClient == null || _spotifyAuthToken.AccessToken != newToken)
        {
            _spotifyClient = new SpotifyClient(newToken);
        }

        return true;
    }

    private void ClearUserVisibleError()
    {
        LastUserVisibleError = null;
        IsAuthorizationRequired = false;
    }

    private void SetAuthorizationRequired()
    {
        IsAuthorizationRequired = true;
        LastUserVisibleError = AuthorizationRequiredMessage;
    }

    private void SetSpotifyApiError(APIException ex, string operation)
    {
        logger.LogWarning(ex, "Spotify API failed while trying to {Operation}.", operation);

        var message = ex.Message;
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("invalid token", StringComparison.OrdinalIgnoreCase))
        {
            _spotifyClient = null;
            SetAuthorizationRequired();
            return;
        }

        if (message.Contains("404", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("no active device", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("device", StringComparison.OrdinalIgnoreCase))
        {
            LastUserVisibleError = NoActiveDeviceMessage;
            return;
        }

        LastUserVisibleError = $"Spotify could not {operation}. Please check Spotify and try again.";
    }
}
