using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Voxta.SampleProviderApp.Providers.Spotify.Models;

namespace Voxta.SampleProviderApp.Providers.Spotify.Services
{
    public class SpotifyManager
    {
        private SpotifyClient? _spotifyClient;
        private SpotifyAuthToken? _spotifyAuthToken;
        private string? _tokenFilePath;
        private SpotifyConfig? _spotifyConfig;
        private readonly ILogger _logger;

        public SpotifyManager(SpotifyConfig config, string tokenFilePath, ILogger logger)
        {
            _spotifyConfig = config;
            _tokenFilePath = tokenFilePath;
            _logger = logger;
        }

        public async Task InitializeSpotifyClient()
        {
            _logger.LogInformation("Initialize Spotify Client");
            var accessToken = await GetValidAccessTokenAsync().ConfigureAwait(false);
            if (accessToken != null)
            {
                _spotifyClient = new SpotifyClient(accessToken);
                _logger.LogInformation("Spotify client authenticated and created successfully");
                _logger.LogWarning("Note: This plugin acts solely as an interface between Voxta and your Spotify player. You must have an active Spotify device or playback session that the plugin can connect to and control. Once connected, you can pause and resume playback freely, until the device becomes inactive for a certain period of time.");
            }
            else
            {
                _logger.LogError("Failed to initialize Spotify client. Access token could not be retrieved.");
            }
        }

        public bool HasClient => _spotifyClient != null;

        private async Task<string?> GetValidAccessTokenAsync()
        {
            var token = await LoadTokenAsync().ConfigureAwait(false);

            if (token == null || IsTokenExpired(token))
            {
                if (token?.RefreshToken != null)
                {
                    try
                    {
                        token = await RefreshTokenAsync(token.RefreshToken).ConfigureAwait(false);
                        await SaveTokenAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to refresh token: {ex.Message}");
                        token = null;
                    }
                }
                if (_spotifyConfig == null)
                {
                    throw new InvalidOperationException("_spotifyConfig null.");
                }

                if (string.IsNullOrEmpty(_spotifyConfig.clientId) ||
                    string.IsNullOrEmpty(_spotifyConfig.clientSecret) ||
                    string.IsNullOrEmpty(_spotifyConfig.redirectUri))
                {
                    throw new InvalidOperationException("Spotify configuration is incomplete.");
                }

                if (token == null)
                {
                    var auth = new OAuthClient();
                    var loginRequest = new LoginRequest(
                        new Uri(_spotifyConfig.redirectUri),
                        _spotifyConfig.clientId,
                        LoginRequest.ResponseType.Code
                    )
                    {
                        Scope = new[]
                        {
                            Scopes.UserReadPrivate,
                            Scopes.UserReadPlaybackState,
                            Scopes.UserModifyPlaybackState,
                            Scopes.UserTopRead,
                            Scopes.PlaylistReadPrivate,
                            Scopes.PlaylistModifyPrivate,
                            Scopes.PlaylistReadCollaborative,
                            Scopes.UserLibraryModify,
                            Scopes.UserLibraryRead
                        }

                    };

                    var authUri = loginRequest.ToUri();
                    var code = await GetAuthCodeAsync(authUri, _spotifyConfig.redirectUri).ConfigureAwait(false);

                    var tokenRequest = new AuthorizationCodeTokenRequest(
                        _spotifyConfig.clientId,
                        _spotifyConfig.clientSecret,
                        code,
                        new Uri(_spotifyConfig.redirectUri)
                    );
                    var response = await auth.RequestToken(tokenRequest).ConfigureAwait(false);

                    token = new SpotifyAuthToken
                    {
                        AccessToken = response.AccessToken,
                        RefreshToken = response.RefreshToken,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn)
                    };

                    await SaveTokenAsync(token).ConfigureAwait(false);
                }
            }
            return token?.AccessToken;
        }

        private async Task<string> GetAuthCodeAsync(Uri authUri, string redirectUri)
        {
            _logger.LogInformation($"Please authorize your app by visiting: {authUri}");

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri.EndsWith("/") ? redirectUri : redirectUri + "/");
            listener.Start();

            _logger.LogInformation($"Waiting for Spotify authentication...");

            var context = await listener.GetContextAsync().ConfigureAwait(false);
            var query = context.Request.QueryString;
            string? code = query["code"];

            var response = context.Response;
            string responseString = @"
            <html>
            <head>
                <style>
                    body {
                        background-color: #121212; /* Dark background */
                        color: #ffffff; /* White text */
                        font-family: Arial, sans-serif;
                        height: 100vh;
                        margin: 0;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        text-align: center;
                    }
                </style>
            </head>
            <body>
                <div>
                    Authentication successful! You can close this tab.
                </div>
            </body>
            </html>";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
            response.OutputStream.Close();

            listener.Stop();

            _logger.LogInformation($"Authorization code received");
            return code ?? throw new InvalidOperationException("Authorization code not received.");
        }

        private bool IsTokenExpired(SpotifyAuthToken token)
        {
            return DateTime.UtcNow >= token.ExpiresAt;
        }

        private async Task SaveTokenAsync(SpotifyAuthToken token)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(token, options);
            await File.WriteAllTextAsync(_tokenFilePath!, json).ConfigureAwait(false);
        }

        private async Task<SpotifyAuthToken> RefreshTokenAsync(string refreshToken)
        {
            var auth = new OAuthClient();
            if (_spotifyConfig == null || _spotifyConfig.clientId == null || _spotifyConfig.clientSecret == null)
            {
                throw new InvalidOperationException("Client ID or Client Secret is not set in the configuration.");
            }

            var refreshRequest = new AuthorizationCodeRefreshRequest(_spotifyConfig.clientId, _spotifyConfig.clientSecret, refreshToken);

            var response = await auth.RequestToken(refreshRequest).ConfigureAwait(false);

            return new SpotifyAuthToken
            {
                AccessToken = response.AccessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn)
            };
        }

        private async Task<SpotifyAuthToken?> LoadTokenAsync()
        {
            if (!File.Exists(_tokenFilePath))
                return null;

            var json = await File.ReadAllTextAsync(_tokenFilePath).ConfigureAwait(false);
            _spotifyAuthToken = JsonSerializer.Deserialize<SpotifyAuthToken>(json);
            return JsonSerializer.Deserialize<SpotifyAuthToken>(json);
        }

        public async Task<CurrentlyPlayingContext?> GetCurrentPlaybackState()
        {
            try
            {
                if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
                {
                    _logger.LogError("Spotify client not valid. Playback state cannot be retrieved.");
                    return null;
                }

                return await _spotifyClient.Player.GetCurrentPlayback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving playback state: {ex.Message}");
                return null;
            }
        }

        public async Task<Paging<FullTrack>?> GetUsersTopTracks()
        {
            try
            {
                if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
                {
                    _logger.LogError("Spotify client not valid. Playback state cannot be retrieved.");
                    return null;
                }

                return await _spotifyClient.Personalization.GetTopTracks().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving top tracks: {ex.Message}");
                return null;
            }
        }

        public async Task ControlSpotifyPlayback(bool playback)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Playback cannot be controlled.");
                return;
            }

            if (playback)
            {
                await _spotifyClient.Player.ResumePlayback().ConfigureAwait(false);
                _logger.LogInformation("Playback resumed.");
            }
            else
            {
                await _spotifyClient.Player.PausePlayback().ConfigureAwait(false);
                _logger.LogInformation("Playback paused.");
            }
        }

        public async Task PlaySpecificUri(string uri, string? type = null)
        {
            try
            {
                if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
                {
                    _logger.LogError("Spotify client not valid. Cannot play specific URI.");
                    return;
                }

                var request = new PlayerResumePlaybackRequest();

                if (type == "track")
                    request.Uris = new List<string> { uri };
                else
                    request.ContextUri = uri;

                await _spotifyClient.Player.ResumePlayback(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error starting playback: {ex.Message}");
            }
        }

        public async Task QueueTrack(string uri)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot queue track.");
                return;
            }

            await _spotifyClient.Player.AddToQueue(new PlayerAddToQueueRequest(uri)).ConfigureAwait(false);
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
                _logger.LogInformation($"Retrieved Spotify user ID: {me.Id}");
                return me.Id;
            }
            catch (APIException ex)
            {
                _logger.LogError(ex, "Failed to retrieve Spotify user ID.");
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
                _logger.LogInformation($"Retrieved user market: {me.Country}");
                return me.Country;
            }
            catch (APIException ex)
            {
                _logger.LogError(ex, "Failed to retrieve user profile for market detection.");
                return null;
            }
        }

        public async Task<bool> ChangeVolume(int volumePercent)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot change volume.");
                return false;
            }

            await _spotifyClient.Player.SetVolume(new PlayerVolumeRequest(volumePercent)).ConfigureAwait(false);
            _logger.LogInformation($"Volume set to {volumePercent}%");
            return true;
        }

        public async Task SkipToPreviousOrNextTrack(string skipToPrevious)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot skip track.");
                return;
            }

            if (skipToPrevious == "previous")
            {
                await _spotifyClient.Player.SkipPrevious().ConfigureAwait(false);
                _logger.LogInformation("Skipped to previous track");
            }
            else
            {
                await _spotifyClient.Player.SkipNext().ConfigureAwait(false);
                _logger.LogInformation("Skipped to next track");
            }
        }

        public async Task SeekPlayback(int positionMs)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot seek playback.");
                return;
            }

            await _spotifyClient.Player.SeekTo(new PlayerSeekToRequest(positionMs)).ConfigureAwait(false);
            _logger.LogInformation($"Playback position set to {positionMs} ms.");
        }

        public async Task SetShuffle(bool shuffleState)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot set shuffle mode.");
                return;
            }

            await _spotifyClient.Player.SetShuffle(new PlayerShuffleRequest(shuffleState)).ConfigureAwait(false);
            _logger.LogInformation($"Shuffle mode set to {(shuffleState ? "on" : "off")}");
        }

        public async Task SetRepeatMode(string repeatMode)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot set repeat mode.");
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
                    _logger.LogError($"Invalid repeat mode: {repeatMode}");
                    return;
            }

            var request = new PlayerSetRepeatRequest(repeatState);
            var result = await _spotifyClient.Player.SetRepeat(request).ConfigureAwait(false);
            if (result)
            {
                _logger.LogInformation($"Repeat mode set to {repeatMode}");
            }
            else
            {
                _logger.LogError($"Failed to set repeat mode to {repeatMode}.");
            }
        }

        public async Task<Dictionary<string, string>> ListAvailablePlaylists()
        {
            var playlistMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot list playlists.");
                return playlistMap;
            }

            var page = await _spotifyClient.Playlists.CurrentUsers().ConfigureAwait(false);

            while (page.Items?.Any() == true)
            {
                foreach (var playlist in page.Items)
                {
                    if (string.IsNullOrEmpty(playlist?.Name) || string.IsNullOrEmpty(playlist?.Uri))
                        continue;

                    playlistMap[playlist.Name] = playlist.Uri;
                }

                if (string.IsNullOrEmpty(page.Next))
                    break;

                page = await _spotifyClient.NextPage(page).ConfigureAwait(false);
            }

            return playlistMap;
        }

        public async Task AddItems(string playlistId, PlaylistAddItemsRequest request)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot add items to playlist.");
                return;
            }

            try
            {
                await _spotifyClient.Playlists.AddItems(playlistId, request).ConfigureAwait(false);
                _logger.LogInformation($"Track added to playlist: {playlistId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to add track to playlist: {ex.Message}");
            }
        }

        public async Task AddTrackToLibraryAsync(string trackId, string trackFriendlyName)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot save track to library.");
                return;
            }

            try
            {
                await _spotifyClient.Library.SaveTracks(
                    new LibrarySaveTracksRequest(new[] { trackId })
                ).ConfigureAwait(false);

                _logger.LogInformation($"Track {trackFriendlyName} added to Liked Songs.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save track to library: {ex.Message}");
            }
        }

        public async Task<Dictionary<string, string>> ListAvailableDevices(System.Threading.CancellationToken cancel = default)
        {
            var deviceMap = new Dictionary<string, string>();

            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot list available devices.");
                return deviceMap;
            }

            var response = await _spotifyClient.Player.GetAvailableDevices(cancel).ConfigureAwait(false);
            if (response?.Devices?.Any() == true)
            {
                foreach (var device in response.Devices!)
                {
                    deviceMap[device.Name] = device.Id;
                }
            }
            else
            {
                _logger.LogWarning("No devices available.");
            }

            return deviceMap;
        }

        public async Task TransferPlayback(string deviceId)
        {
            if (!await EnsureValidSpotifyClient().ConfigureAwait(false) || _spotifyClient == null)
            {
                _logger.LogError("Spotify client not valid. Cannot transfer playback.");
                return;
            }

            await _spotifyClient.Player.TransferPlayback(new PlayerTransferPlaybackRequest(new List<string> { deviceId })).ConfigureAwait(false);
            _logger.LogInformation($"Playback transferred to device: {deviceId}");
        }

        private async Task<bool> EnsureValidSpotifyClient()
        {
            var newToken = await GetValidAccessTokenAsync().ConfigureAwait(false);

            if (newToken == null)
            {
                _logger.LogError("Unable to refresh access token. Spotify client cannot be used.");
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
}