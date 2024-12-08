using Microsoft.Extensions.Logging;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Providers.Host;
using System.Text.RegularExpressions;
using System.Text.Json;
using SpotifyAPI.Web;
using System.Net;

namespace Voxta.SampleProviderApp.Providers;

public class UserFunctionProviderSpotify(
    IRemoteChatSession session,
    ILogger<UserFunctionProviderSpotify> logger
)
    : ProviderBase(session, logger)
{
    public class SpotifyConfig
    {
        public string? clientId { get; set; }
        public string? clientSecret { get; set; }
        public string? redirectUri { get; set; }
    }

    public class SpotifyAuthToken
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private SpotifyClient? _spotifyClient;
    private SpotifyAuthToken? _spotifyAuthToken;
    private readonly string _tokenFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Providers\\configs\\UserFunctionProviderSpotifyAuth.json");
    private CurrentlyPlayingContext? _lastKnownState = null;
    private CurrentlyPlayingContext? _playbackState;
    private CancellationTokenSource? _cancellationTokenSource;
    private Dictionary<string, string> _deviceMap = new Dictionary<string, string>();
    private Dictionary<string, string> _playlistMap = new Dictionary<string, string>();
    private SpotifyConfig? _spotifyConfig = new SpotifyConfig();

    protected override async Task OnStartAsync()
    {
        await base.OnStartAsync();

        _cancellationTokenSource = new CancellationTokenSource();

        LoadConfiguration();
        if (_spotifyConfig == null)
        {
            logger.LogError("Configuration not loaded properly.");
            return;
        }

        await InitializeSpotifyClient();
        if (_spotifyClient == null)
        {
            Logger.LogError("Spotify client could not be initialized.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _ = MonitorSpotifyPlayback(_cancellationTokenSource.Token);

        HandleMessage<ServerActionMessage>(async message =>
        {
            if (message.Role != ChatMessageRole.User) return;

            switch (message.Value)
            {
                case "toggle_playback":
                    bool isPlaying = false;
                    if (_playbackState != null)
                    {
                        isPlaying = _playbackState.IsPlaying;
                    }

                    Logger.LogInformation($"Toggling music playback. Current state: {isPlaying}, toggling to: {(!isPlaying ? "play" : "pause")}");
                    await ControlSpotifyPlayback(!isPlaying);
                    SendMessage($"/note As requested {{{{ char }}}} toggled playback to: {(!isPlaying ? "play" : "pause")}");
                    break;
                case "spotify_connect":
                    SendMessagePrefix("No active spotify device found. Start playback in one of your clients. ");
                    break;
                case "play_random_music":
                    string? randomTrackUri = null;
                    var topTracks = await GetUsersTopTracks();
                    //var topTracksJson = JsonSerializer.Serialize(topTracks, new JsonSerializerOptions { WriteIndented = true });
                    //Logger.LogInformation($"Top Tracks: {topTracksJson}");
                    if (topTracks?.Items != null && topTracks.Items.Count > 0)
                    {
                        var random = new Random();
                        var randomIndex = random.Next(topTracks.Items.Count);

                        randomTrackUri = topTracks.Items[randomIndex].Uri;
                        await PlaySpecificUri(randomTrackUri);

                        SendMessage($"/note As requested {{{{ char }}}} play started some music");
                    }
                    break;
                case "play_music":
                    if (!message.TryGetArgument("name", out var playNameString))
                        playNameString = "noop";
                    if (!message.TryGetArgument("type", out var playTypeString))
                        playTypeString = "track";

                    if (playNameString == "noop")
                    {
                        {
                            SendMessage("/note request not identified.");
                        }
                    }
                    else
                    {
                        var (playUri, playFriendlyName) = await GetSpotifyUri(playNameString, playTypeString);
                        if (playUri != null)
                        {
                            await PlaySpecificUri(playUri);
                            SendMessage($"/note Playing: {playFriendlyName}");
                        }
                        else
                        {
                            SendMessage("/note  No matching results found to play.");
                        }
                    }
                    break;
                case "queue_Track":
                    if (!message.TryGetArgument("name", out var queueNameString))
                        queueNameString = "noop";
                    if (!message.TryGetArgument("type", out var queueTypeString))
                        queueTypeString = "track";

                    if (queueNameString == "noop")
                    {
                        {
                            SendMessage("/note request not identified.");
                        }
                    }
                    else
                    {
                        var (queueUri, queueFriendlyName) = await GetSpotifyUri(queueNameString, queueTypeString);
                        if (queueUri != null)
                        {
                            await QueueTrack(queueUri);
                            SendMessage($"/note Added to queue: {queueFriendlyName}");
                        }
                        else
                        {
                            SendMessage("/note No matching results found to queue.");
                        }
                    }
                    break;
                case "volume":
                    if (!message.TryGetArgument("level", out var levelString) || !int.TryParse(levelString, out var volumeLevel))
                        volumeLevel = 50;
                    await ChangeVolume(volumeLevel);
                    SendMessage($"/note As requested {{{{ char }}}} changed the volume to {volumeLevel}");
                    break;
                case "seek_playback":
                    if (!message.TryGetArgument("seconds", out var positionInSString) || !int.TryParse(positionInSString, out var positionInS))
                        positionInS = 50;
                    var positionInMs = positionInS * 1000;
                    await SeekPlayback(positionInMs);
                    SendMessage($"/note As requested {{{{ char }}}} skipped to {positionInS} seconds");
                    break;
                case "skip_next":
                    await SkipToPreviousOrNextTrack("next");
                    SendMessage($"/note As requested {{{{ char }}}} skipped to the next track");
                    break;
                case "skip_previous":
                    await SkipToPreviousOrNextTrack("previous");
                    SendMessage($"/note As requested {{{{ char }}}} skipped to the previous track");
                    break;
                case "repeat_mode":
                    if (!message.TryGetArgument("mode", out var repeatMode))
                        repeatMode = "repeat-track";
                    await SetRepeatMode(repeatMode);
                    SendMessage($"/note As requested {{{{ char }}}} set the repeat mode to: {repeatMode}");
                    break;
                case "shuffle_mode":
                    if (!message.TryGetArgument("mode", out var shuffleMode))
                        shuffleMode = "off";
                    var shuffleModeBool = shuffleMode == "off" ? false : true;
                    await SetShuffle(shuffleModeBool);
                    SendMessage($"/note As requested {{{{ char }}}} set the shuffle mode to: {shuffleModeBool}");
                    break;
                case "get_playlists":
                    _playlistMap = await ListAvailablePlaylists();
                    if (_playlistMap.Any())
                    {
                        var playlistList = string.Join(", ", _playlistMap.Keys);
                        SendMessage($"/note Available playlists: {playlistList}");
                    }
                    else
                    {
                        SendMessage($"/note No playlists available.");
                    }
                    break;
                case "add_to_playlist":
                    if (!message.TryGetArgument("playlist", out var playlistFriendlyName) || string.IsNullOrEmpty(playlistFriendlyName))
                    {
                        SendMessage("/event No playlist specified.");
                        break;
                    }
                    _playlistMap = await ListAvailablePlaylists();

                    playlistFriendlyName = CleanString(playlistFriendlyName);

                    if (!_playlistMap.TryGetValue(playlistFriendlyName, out var playlistId))
                    {
                        SendMessage($"/note Playlist not found: {playlistFriendlyName}");
                        break;
                    }

                    var (trackUri, trackFriendlyName) = GetCurrentTrackInfo();
                    if (string.IsNullOrEmpty(trackUri))
                    {
                        SendMessage("/note No track is currently playing.");
                        break;
                    }

                    var request = new PlaylistAddItemsRequest(new List<string> { trackUri });
                    await AddItems(playlistId, request);

                    SendMessage($"/note Track '{trackFriendlyName}' added to playlist '{playlistFriendlyName}'.");
                    break;
                case "list_devices":
                    var deviceMap = await ListAvailableDevices();
                    if (deviceMap.Any())
                    {
                        var deviceList = string.Join(", ", deviceMap.Keys);
                        SendMessage($"/note Available devices: {deviceList}");
                    }
                    else
                    {
                        SendMessage($"/note No devices available.");
                    }
                    break;
                case "transfer_to_device":
                    if (!message.TryGetArgument("device", out var partialName) || string.IsNullOrEmpty(partialName))
                    {
                        SendMessage("/note No device specified.");
                        break;
                    }

                    _deviceMap = await ListAvailableDevices();

                    partialName = CleanString(partialName);

                    var matchedDevice = _deviceMap.FirstOrDefault(d =>
                        d.Key.Contains(partialName, StringComparison.OrdinalIgnoreCase));

                    if (matchedDevice.Key == null)
                    {
                        var possibleMatches = _deviceMap.Keys
                            .Where(k => k.Contains(partialName, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (possibleMatches.Any())
                        {
                            SendMessage($"/note Did you mean: {string.Join(", ", possibleMatches)}?");
                        }
                        else
                        {
                            SendMessage($"/note No device found matching: {partialName}");
                        }
                        break;
                    }
                    await TransferPlayback(matchedDevice.Value);
                    SendMessage($"/note Playback transferred to: {matchedDevice.Key}");
                    break;
            }
        });
    }

    protected override async Task OnStopAsync()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        await base.OnStopAsync();
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Send Message
    //////////////////////////////////////////////////////////////////////////////////

    private void SendMessage(string message)
    {
        Send(new ClientSendMessage
        {
            SessionId = SessionId,
            DoUserActionInference = false,
            Text = message
        });
    }

    private void SendMessagePrefix(string message)
    {
        Send(new ClientSendMessage
        {
            SessionId = SessionId,
            DoUserActionInference = false,
            CharacterResponsePrefix = message
        });
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Update Client Context Actions
    //////////////////////////////////////////////////////////////////////////////////
    private void UpdateClientContext(string[] flags, string[] contexts)
    {
        // Register our action
        Send(new ClientUpdateContextMessage
        {
            SessionId = SessionId,
            ContextKey = "Music",
            Contexts = contexts.Select(context => new ContextDefinition { Text = context }).ToArray(),
            SetFlags = flags,
            Actions =
            [
                // Example action
                new()
                {
                    // The name used by the LLM to select the action. Make sure to select a clear name.
                    Name = "toggle_playback",
                    // Layers allow you to run your actions separately from the scene
                    Layer = "SpotifyControl",
                    // A short description of the action to be included in the functions list, typically used for character action inference
                    ShortDescription = "play or pause music",
                    // The condition for executing this function
                    Description = "When {{ user }} asks to toggle music playback (play, pause, stop, resume, etc.).",
                    // This match will ensure user action inference is only going to be triggered if this regex matches the message.
                    // For example, if you use "please" in all functions, this can avoid running user action inference at all unless
                    // the user said "please".
                    //MatchFilter = [\b(?:play|pause|stop|continue|toggle|playback|music)\b.*\bmusic\b],
                    // Only available when the specific flag is set
                    FlagsFilter = "spotify_connected",
                    // Only run in response to the user messages 
                    Timing = FunctionTiming.AfterUserMessage,
                    // Do not generate a response, we will instead handle the action ourselves
                    CancelReply = true,
                    // Only allow this for characters with the assistant field enabled
                    AssistantFilter = true,
                },
                new()
                {
                    Name = "spotify_connect",
                    Layer = "SpotifyControl",
                    ShortDescription = "anything regarding spotify",
                    Description = "When {{ user }} asks to interact with spotify in any way, like playing music, search for an artist, etc.",
                    //MatchFilter = [\b(?:play|pause|stop|continue|toggle|playback|music)\b.*\bmusic\b],
                    FlagsFilter = "spotify_disconnected",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                },
                new()
                {
                    Name = "play_random_music",
                    Layer = "SpotifyControl",
                    ShortDescription = "play random music",
                    Description = "When {{ user }} asks to play music without mentioning the artist or song.",
                    //MatchFilter = [\b(?:play|pause|stop|continue|toggle|playback|music)\b.*\bmusic\b],
                    FlagsFilter = "spotify_connected",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                },
                new()
                {
                    Name = "play_music",
                    Layer = "SpotifyControl",
                    ShortDescription = "play requested music",
                    Description = "When {{ user }} asks to play a specific track, album or artist.",
                    //MatchFilter = [\b(?:play|pause|stop|continue|toggle|playback|music)\b.*\bmusic\b],
                    FlagsFilter = "spotify_connected",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                    Arguments =
                    [
                        new FunctionArgumentDefinition
                        {
                            Name = "name",
                            Description = "Select the track, album or artist name {{ user }} asked for.",
                            Required = true,
                            Type = FunctionArgumentType.String,
                        },
                        new FunctionArgumentDefinition
                        {
                            Name = "type",
                            Description = "Select what type of object the {{ user }} asked for. Select noop if you are unsure",
                            Required = true,
                            Type = FunctionArgumentType.String,
                        }
                    ],
                },
                new()
                {
                    Name = "queue_Track",
                    Layer = "SpotifyControl",
                    ShortDescription = "search on spotify",
                    Description = "When {{ user }} asks to search for albums, artists, episodes, playlists, shows or tracks",
                    //MatchFilter = [\b(?:play|pause|stop|continue|toggle|playback|music)\b.*\bmusic\b],
                    FlagsFilter = "spotify_connected",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                    Arguments =
                    [
                        new FunctionArgumentDefinition
                        {
                            Name = "name",
                            Description = "Select the track, album or artist name {{ user }} asked for.",
                            Required = true,
                            Type = FunctionArgumentType.String,
                        },
                        new FunctionArgumentDefinition
                        {
                            Name = "type",
                            Description = "Select what type of object the {{ user }} asked for. Select noop if you are unsure",
                            Required = true,
                            Type = FunctionArgumentType.String,
                        }
                    ],
                },
                new()
                {
                    Name = "volume",
                    Layer = "SpotifyControl",
                    ShortDescription = "change volume",
                    Description = "When {{ user }} asks to set the volume to a specific level.",
                    //MatchFilter = [\b(?:play|pause|stop|continue|toggle|playback|music)\b.*\bmusic\b],
                    FlagsFilter = "playing",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                    Arguments =
                    [
                        new FunctionArgumentDefinition
                        {
                            Name = "level",
                            Description = "select a level from 1 to 100 based on what {{ user }} requested. If {{ user }} requests just louder or quieter, select the current value and add or subtract 10.",
                            Required = true,
                            Type = FunctionArgumentType.Integer,
                        }
                    ],
                },
                new()
                {
                    Name = "seek_playback",
                    Layer = "SpotifyControl",
                    ShortDescription = "seek through the track",
                    Description = "When {{ user }} asks go to (seek) a specific position within the current track.",
                    //MatchFilter = [\b(?:play|pause|stop|continue|toggle|playback|music)\b.*\bmusic\b],
                    FlagsFilter = "playing",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                    Arguments =
                    [
                        new FunctionArgumentDefinition
                        {
                            Name = "seconds",
                            Description = "select a position in seconds based on what {{ user }} requested.",
                            Required = true,
                            Type = FunctionArgumentType.Integer,
                        }
                    ],
                },
                new()
                {
                    Name = "skip_next",
                    Layer = "SpotifyControl",
                    ShortDescription = "skip to the next track",
                    Description = "When {{ user }} asks to skip to next track/song/title.",
                    //MatchFilter = ["\b(?:next|skip|song)\b"],
                    FlagsFilter = "playing",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true
                },
                new()
                {
                    Name = "skip_previous",
                    Layer = "SpotifyControl",
                    ShortDescription = "skip to the previous track",
                    Description = "When {{ user }} asks to skip to the previous track/song/title.",
                    //MatchFilter = ["\b(?:previous|skip|song)\b"],
                    FlagsFilter = "playing",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true
                },
                new()
                {
                    //
                    // Todo: Invalid repeat mode: track
                    //
                    Name = "repeat_mode",
                    Layer = "SpotifyControl",
                    ShortDescription = "change the repeat mode",
                    Description = "When {{ user }} asks to change the repeat mode to one of the following repeat-track, repeat-context or off",
                    //MatchFilter = ["\b(?:repeat|track|context|off|disable|enable)\b"],
                    FlagsFilter = "playing",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                    Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "mode",
                                Description = "it must be one of those: [repeat-track, repeat-context, off]",
                                Required = true,
                                Type = FunctionArgumentType.String,
                            }
                        ],
                },
                new()
                {
                    Name = "shuffle_mode",
                    Layer = "SpotifyControl",
                    ShortDescription = "change the shuffle mode",
                    Description = "When {{ user }} asks to change the shuffle mode on or off",
                    //MatchFilter = ["\b(?:repeat|track|context|off|disable|enable)\b"],
                    FlagsFilter = "playing",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                    Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "mode",
                                Description = "Select on or off based on {{ user }}s requested",
                                Required = true,
                                Type = FunctionArgumentType.String,
                            }
                        ],
                },
                new()
                {
                    Name = "get_playlists",
                    Layer = "SpotifyControl",
                    ShortDescription = "list all available playlists",
                    Description = "When {{ user }} asks to list all available playlists",
                    //MatchFilter = ["\b(?:repeat|track|context|off|disable|enable)\b"],
                    FlagsFilter = "spotify_connected",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                },
                new()
                {
                    Name = "add_to_playlist",
                    Layer = "SpotifyControl",
                    ShortDescription = "add to playlist",
                    Description = "When {{ user }} asks to add the current song to a specific device",
                    //MatchFilter = ["\b(?:repeat|track|context|off|disable|enable)\b"],
                    FlagsFilter = "spotify_connected",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                    Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "playlist",
                                Description = "Select the playlist name based {{ user }} requested",
                                Required = true,
                                Type = FunctionArgumentType.String,
                            }
                        ],
                },
                new()
                {
                    Name = "list_devices",
                    Layer = "SpotifyControl",
                    ShortDescription = "list all available devices",
                    Description = "When {{ user }} asks to list all available devices",
                    //MatchFilter = ["\b(?:repeat|track|context|off|disable|enable)\b"],
                    FlagsFilter = "spotify_connected",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                },
                new()
                {
                    Name = "transfer_to_device",
                    Layer = "SpotifyControl",
                    ShortDescription = "transfer to device",
                    Description = "When {{ user }} asks to transfer playback to a specific device",
                    //MatchFilter = ["\b(?:repeat|track|context|off|disable|enable)\b"],
                    FlagsFilter = "spotify_connected",
                    Timing = FunctionTiming.AfterUserMessage,
                    CancelReply = true,
                    AssistantFilter = true,
                    Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "device",
                                Description = "Select the device name based {{ user }} requested",
                                Required = true,
                                Type = FunctionArgumentType.String,
                            }
                        ],
                },
            ]
        });
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Load config json
    //////////////////////////////////////////////////////////////////////////////////

    private void LoadConfiguration()
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Providers\\configs\\UserFunctionProviderSpotifyConfig.json");

        if (!File.Exists(configPath))
        {
            logger.LogError("Configuration file not found at {ConfigPath}", configPath);
            return;
        }

        try
        {
            // Read the file and deserialize into SpotifyConfig object
            var json = File.ReadAllText(configPath);
            _spotifyConfig = JsonSerializer.Deserialize<SpotifyConfig>(json);
            if (_spotifyConfig == null ||
                string.IsNullOrEmpty(_spotifyConfig.clientId) ||
                string.IsNullOrEmpty(_spotifyConfig.clientSecret) ||
                string.IsNullOrEmpty(_spotifyConfig.redirectUri))
            {
                logger.LogError("Invalid Spotify configuration.");
                _spotifyConfig = null;
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Error loading configuration: {Message}", ex.Message);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Initialize Spotify Client
    //////////////////////////////////////////////////////////////////////////////////

    private async Task InitializeSpotifyClient()
    {
        var accessToken = await GetValidAccessTokenAsync();
        if (accessToken != null)
        {
            _spotifyClient = new SpotifyClient(accessToken);
        }
        else
        {
            Logger.LogError("Failed to initialize Spotify client. Access token could not be retrieved.");
        }
    }

    private async Task<string?> GetValidAccessTokenAsync()
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
                    Logger.LogError($"Failed to refresh token: {ex.Message}");
                    token = null; // Fall back to full re-authentication
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
                // Full re-authentication
                var auth = new OAuthClient();
                var loginRequest = new LoginRequest(
                    new Uri(_spotifyConfig.redirectUri),
                    _spotifyConfig.clientId,
                    LoginRequest.ResponseType.Code
                )
                {
                    Scope = [Scopes.UserReadPlaybackState, Scopes.UserModifyPlaybackState, Scopes.UserTopRead, Scopes.PlaylistReadPrivate, Scopes.PlaylistModifyPrivate, Scopes.PlaylistReadCollaborative]
                };

                var authUri = loginRequest.ToUri();
                var code = await GetAuthCodeAsync(authUri, _spotifyConfig.redirectUri);

                var tokenRequest = new AuthorizationCodeTokenRequest(
                    _spotifyConfig.clientId,
                    _spotifyConfig.clientSecret,
                    code,
                    new Uri(_spotifyConfig.redirectUri)
                );
                var response = await auth.RequestToken(tokenRequest);

                token = new SpotifyAuthToken
                {
                    AccessToken = response.AccessToken,
                    RefreshToken = response.RefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn)
                };

                await SaveTokenAsync(token);
            }
        }

        return token?.AccessToken;
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Spotify connectivity
    //////////////////////////////////////////////////////////////////////////////////

    // Request auth token
    private async Task<string> GetAuthCodeAsync(Uri authUri, string redirectUri)
    {
        Logger.LogInformation($"Please authorize your app by visiting: {authUri}");

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri.EndsWith("/") ? redirectUri : redirectUri + "/");
        listener.Start();

        Logger.LogInformation($"Waiting for Spotify authentication...");

        var context = await listener.GetContextAsync();
        var query = context.Request.QueryString;
        string? code = query["code"];

        var response = context.Response;
        //string responseString = "<html><body>Authentication successful! You can close this tab.</body></html>";
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
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();

        listener.Stop();

        return code ?? throw new InvalidOperationException("Authorization code not received.");
    }

    // Token validation
    private bool IsTokenExpired(SpotifyAuthToken token)
    {
        return DateTime.UtcNow >= token.ExpiresAt.AddMinutes(-59);
    }

    // Save the Token
    private async Task SaveTokenAsync(SpotifyAuthToken token)
    {
        //_spotifyAuthToken = token;
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(token, options);
        await File.WriteAllTextAsync(_tokenFilePath, json);
    }

    // Refresh the Token
    private async Task<SpotifyAuthToken> RefreshTokenAsync(string refreshToken)
    {
        var auth = new OAuthClient();
        if (_spotifyConfig == null || _spotifyConfig.clientId == null || _spotifyConfig.clientSecret == null)
        {
            throw new InvalidOperationException("Client ID or Client Secret is not set in the configuration.");
        }

        var refreshRequest = new AuthorizationCodeRefreshRequest(_spotifyConfig.clientId, _spotifyConfig.clientSecret, refreshToken);

        var response = await auth.RequestToken(refreshRequest);

        return new SpotifyAuthToken
        {
            AccessToken = response.AccessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn)
        };
    }

    // Load token
    private async Task<SpotifyAuthToken?> LoadTokenAsync()
    {
        if (!File.Exists(_tokenFilePath))
            return null;

        var json = await File.ReadAllTextAsync(_tokenFilePath);
        _spotifyAuthToken = JsonSerializer.Deserialize<SpotifyAuthToken>(json);
        return JsonSerializer.Deserialize<SpotifyAuthToken>(json);
    }

    // Check if token is still valid
    private async Task<bool> EnsureValidSpotifyClient()
    {
        var newToken = await GetValidAccessTokenAsync();

        if (newToken == null)
        {
            Logger.LogError("Unable to refresh access token. Spotify client cannot be used.");
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

    //////////////////////////////////////////////////////////////////////////////////
    // Continuous polling
    //////////////////////////////////////////////////////////////////////////////////
    public async Task MonitorSpotifyPlayback(CancellationToken cancellationToken)
    {
        bool connected = false;
        bool isPlaying = false;
        string? trackContext = null;
        string? volumeContext = null;

        UpdateClientContext(["spotify_disconnected"], []);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await GetCurrentPlaybackState();
                bool hasChanges = false;
                List<string> flags = new();
                List<string> contexts = new();

                if (_playbackState != null)
                {
                    if (_lastKnownState == null || HasConnectionStateChanged(_playbackState, _lastKnownState))
                    {
                        hasChanges = true;
                        flags.Add(_playbackState.Device?.IsActive == true ? "spotify_connected" : "!spotify_connected");
                        flags.Add(_playbackState.Device?.IsActive == true ? "!spotify_disconnected" : "spotify_disconnected");
                        connected = _playbackState.Device?.IsActive ?? false;
                    }

                    if (connected)
                    {
                        if (_lastKnownState == null || Has_playbackStateChanged(_playbackState, _lastKnownState))
                        {
                            hasChanges = true;
                            flags.Add(_playbackState.IsPlaying ? "playing" : "!playing");
                            isPlaying = _playbackState.IsPlaying;
                        }

                        if (_lastKnownState == null || HasTrackChanged(_playbackState, _lastKnownState))
                        {
                            hasChanges = true;
                            if (_playbackState?.Item is FullTrack track)
                            {
                                //
                                // Todo: get duration for more reliable seeking
                                //
                                //var trackJson = JsonSerializer.Serialize(track, new JsonSerializerOptions { WriteIndented = true });
                                //Logger.LogInformation($"trackJson: {trackJson}");
                                var trackName = track.Name ?? "Unknown Track";
                                var artistName = string.Join(", ", track.Artists.Select(a => a.Name)) ?? "Unknown Artist";
                                var albumName = track.Album?.Name;

                                string message = albumName != null
                                    ? $"{trackName} by {artistName} from the album {albumName}"
                                    : $"{trackName} by {artistName}";

                                trackContext = $"Currently playing: {message}";
                            }
                        }

                        if (_playbackState == null)
                        {
                            throw new InvalidOperationException("_playbackState null");
                        }

                        if (_lastKnownState == null || HasVolumeChanged(_playbackState, _lastKnownState) && isPlaying)
                        {
                            hasChanges = true;
                            volumeContext = $"(Volume: {_playbackState?.Device?.VolumePercent})";
                        }

                        if (hasChanges)
                        {
                            if (isPlaying)
                            {
                                contexts.Add($"{trackContext} {volumeContext}");
                            }

                            _lastKnownState = _playbackState;
                            UpdateClientContext(flags.ToArray(), contexts.ToArray());
                        }
                    }
                    else
                    {
                        if (hasChanges)
                        {
                            flags.Add("spotify_disconnected");
                            _lastKnownState = _playbackState;
                            UpdateClientContext(flags.ToArray(), contexts.ToArray());
                        }
                    }
                }
                else
                {
                    if (_playbackState == null)
                    {
                        if (_lastKnownState == null)
                        {
                            await Task.Delay(1000, cancellationToken);
                            continue;
                        }

                        flags.Add("!spotify_connected");
                        flags.Add("!playing");
                        flags.Add("spotify_disconnected");
                        connected = false;
                        isPlaying = false;

                        _lastKnownState = _playbackState;
                        UpdateClientContext(flags.ToArray(), []);
                    }
                }
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            // Expected exception when cancellation is requested
            //Logger.LogInformation("MonitorSpotifyPlayback loop stopped.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in MonitorSpotifyPlayback.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Handle Updates
    //////////////////////////////////////////////////////////////////////////////////

    private bool HasConnectionStateChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.Device?.IsActive != oldState?.Device?.IsActive;
    }

    private bool Has_playbackStateChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.IsPlaying != oldState?.IsPlaying;
    }

    private bool HasTrackChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.Item is FullTrack newTrack && oldState?.Item is FullTrack oldTrack && newTrack.Name != oldTrack.Name;
    }

    private bool HasVolumeChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.Device?.VolumePercent != oldState?.Device?.VolumePercent;
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Search mapping and parsing
    //////////////////////////////////////////////////////////////////////////////////
    private async Task<(string? Uri, string? FriendlyName)> GetSpotifyUri(string nameString, string typeString)
    {
        nameString = CleanString(nameString);
        typeString = CleanString(typeString);

        Logger.LogInformation($"Searching for: {nameString}");
        Logger.LogInformation($"Type: {typeString}");

        var searchType = MapToSearchType(typeString);
        var searchResponse = await SearchSpotify(nameString, searchType);

        if (searchResponse == null)
        {
            Logger.LogError("Search failed. No response received.");
            return (null, null);
        }

        return ExtractUriAndFriendlyName(searchResponse);
    }

    private SearchRequest.Types MapToSearchType(string typeString)
    {
        return typeString.ToLower() switch
        {
            "album" or "albums" => SearchRequest.Types.Album,
            "artist" or "artists" => SearchRequest.Types.Artist,
            "episode" or "episodes" => SearchRequest.Types.Episode,
            "playlist" or "playlists" => SearchRequest.Types.Playlist,
            "show" or "shows" => SearchRequest.Types.Show,
            "track" or "tracks" => SearchRequest.Types.Track,
            _ => SearchRequest.Types.All,
        };
    }
    private (string? Uri, string? FriendlyName) ExtractUriAndFriendlyName(SearchResponse searchResponse)
    {
        if (searchResponse.Tracks?.Items?.FirstOrDefault() is var track && track != null)
            return (track.Uri, $"Track: {track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))} (Album: {track.Album.Name})");

        if (searchResponse.Albums?.Items?.FirstOrDefault() is var album && album != null)
            return (album.Uri, $"Album: {album.Name} by {string.Join(", ", album.Artists.Select(a => a.Name))}");

        if (searchResponse.Artists?.Items?.FirstOrDefault() is var artist && artist != null)
            return (artist.Uri, $"Artist: {artist.Name}");

        if (searchResponse.Episodes?.Items?.FirstOrDefault() is var episode && episode != null)
            return (episode.Uri, $"Episode: {episode.Name}");

        if (searchResponse.Playlists?.Items?.FirstOrDefault() is var playlist && playlist != null)
            return (playlist.Uri, $"Playlist: {playlist.Name}");

        if (searchResponse.Shows?.Items?.FirstOrDefault() is var show && show != null)
            return (show.Uri, $"Show: {show.Name}");

        return (null, null);
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Spotify API Endpoints
    //////////////////////////////////////////////////////////////////////////////////

    // Get information about the user’s current playback state, including track or episode, progress, and active device. 
    private async Task GetCurrentPlaybackState()
    {
        try
        {
            if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
            {
                Logger.LogError("Spotify client not valid. Playback state cannot be retrieved.");
                return;
            }

            _playbackState = await _spotifyClient.Player.GetCurrentPlayback();
            //var playbackJson = JsonSerializer.Serialize(_playbackState, new JsonSerializerOptions { WriteIndented = true });
            //Logger.LogInformation($"playbackJson: {playbackJson}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error retrieving playback state: {ex.Message}");
            return;
        }
    }

    // Ger users top tracks
    private async Task<Paging<FullTrack>?> GetUsersTopTracks()
    {
        try
        {
            if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
            {
                Logger.LogError("Spotify client not valid. Playback state cannot be retrieved.");
                return null;
            }

            var topTracks = await _spotifyClient.Personalization.GetTopTracks();
            return topTracks;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error retrieving top tracks: {ex.Message}");
            return null;
        }
    }

    // Toggle playback state
    private async Task ControlSpotifyPlayback(bool playback)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Playback cannot be controlled.");
            return;
        }

        if (playback)
        {
            await _spotifyClient.Player.ResumePlayback();
            Logger.LogInformation("Playback resumed.");
        }
        else
        {
            await _spotifyClient.Player.PausePlayback();
            Logger.LogInformation("Playback paused.");
        }
    }

    // Play a specific uri (artist, album, track, playlist, episode, show)
    private async Task PlaySpecificUri(string uri)
    {
        try
        {
            if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
            {
                Logger.LogError("Spotify client not valid. Cannot play specific URI.");
                return;
            }

            await _spotifyClient.Player.ResumePlayback(new PlayerResumePlaybackRequest
            {
                Uris = new List<string> { uri }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error starting playback: {ex.Message}");
            return;
        }
    }

    // Queue a specific uri (artist, album, track, playlist, episode, show)
    private async Task QueueTrack(string uri)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot queue track.");
            return;
        }

        await _spotifyClient.Player.AddToQueue(new PlayerAddToQueueRequest(uri));
    }

    // Search for a specific type or use "all" based on the action argument
    private async Task<SearchResponse?> SearchSpotify(string nameString, SearchRequest.Types searchType)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot search for artist.");
            return null;
        }

        var searchResult = await _spotifyClient.Search.Item(new SearchRequest(searchType, nameString));
        return searchResult;
    }

    //
    // Todo: Investigate different search functions & request identification logics
    //

    /*
    var searchResponseJson = JsonSerializer.Serialize(searchResponse, new JsonSerializerOptions { WriteIndented = true });
    //Logger.LogInformation($"Top Tracks: {searchResponseJson}");

    var albums = searchResponse.Albums?.Items?.FirstOrDefault();
    var artists = searchResponse.Artists?.Items?.FirstOrDefault();
    var episodes = searchResponse.Episodes?.Items?.FirstOrDefault();
    var playlists = searchResponse.Playlists?.Items?.FirstOrDefault();
    var shows = searchResponse.Shows?.Items?.FirstOrDefault();
    var tracks = searchResponse.Tracks?.Items?.FirstOrDefault();
    Logger.LogInformation($"albums: {JsonSerializer.Serialize(albums, new JsonSerializerOptions { WriteIndented = true })}");
    Logger.LogInformation($"artists: {JsonSerializer.Serialize(artists, new JsonSerializerOptions { WriteIndented = true })}");
    Logger.LogInformation($"episodes: {JsonSerializer.Serialize(episodes, new JsonSerializerOptions { WriteIndented = true })}");
    Logger.LogInformation($"playlists: {JsonSerializer.Serialize(playlists, new JsonSerializerOptions { WriteIndented = true })}");
    Logger.LogInformation($"shows: {JsonSerializer.Serialize(shows, new JsonSerializerOptions { WriteIndented = true })}");
    Logger.LogInformation($"tracks: {JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true })}");
    */

    /*private async Task SearchSpotifyBroadly(string query)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot perform search.");
            return;
        }

        var searchResult = await _spotifyClient.Search.Item(new SearchRequest(SearchRequest.Types.Track | SearchRequest.Types.Album | SearchRequest.Types.Artist, query));

        // Example disambiguation logic
        if (searchResult.Tracks?.Items?.Any() == true)
        {
            Logger.LogInformation("Tracks found:");
            foreach (var track in searchResult.Tracks.Items)
            {
                Logger.LogInformation($"- {track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))} - {track.Uri}");
            }
        }

        if (searchResult.Albums?.Items?.Any() == true)
        {
            Logger.LogInformation("Albums found:");
            foreach (var album in searchResult.Albums.Items)
            {
                Logger.LogInformation($"- {album.Name} by {string.Join(", ", album.Artists.Select(a => a.Name))}");
            }
        }

        if (searchResult.Artists?.Items?.Any() == true)
        {
            Logger.LogInformation("Artists found:");
            foreach (var artist in searchResult.Artists.Items)
            {
                Logger.LogInformation($"- {artist.Name}, URI: {artist.Uri}");
            }
        }
    }*/

    /*private async Task SearchSpotifyTrack(string trackName)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot search for artist.");
            return;
        }

        var searchResult = await _spotifyClient.Search.Item(new SearchRequest(SearchRequest.Types.Track, trackName));
        var track = searchResult.Tracks?.Items?.FirstOrDefault();
        if (track != null)
        {
            Logger.LogInformation($"Found Track: {track.Name}, URI: {track.Uri}");
        }
        else
        {
            Logger.LogWarning($"No track found for name: {trackName}");
        }
    }*/

    /* private async Task SearchSpotifyArtist(string artistName)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot search for artist.");
            return;
        }

        var searchResult = await _spotifyClient.Search.Item(new SearchRequest(SearchRequest.Types.Artist, artistName));
        var artist = searchResult.Artists?.Items?.FirstOrDefault();
        if (artist != null)
        {
            Logger.LogInformation($"Found artist: {artist.Name}, URI: {artist.Uri}");
        }
        else
        {
            Logger.LogWarning($"No artist found for name: {artistName}");
        }
    }*/

    // Volume control
    private async Task ChangeVolume(int volumePercent)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot change volume.");
            return;
        }

        await _spotifyClient.Player.SetVolume(new PlayerVolumeRequest(volumePercent));
        Logger.LogInformation($"Volume set to {volumePercent}.");
    }

    // Skip track back or fourth based on argument
    private async Task SkipToPreviousOrNextTrack(string skipToPrevious)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot skip track.");
            return;
        }

        if (skipToPrevious == "previous")
        {
            await _spotifyClient.Player.SkipPrevious();
            Logger.LogInformation("Skipped to previous track.");
        }
        else
        {
            await _spotifyClient.Player.SkipNext();
            Logger.LogInformation("Skipped to next track.");
        }
    }

    // Seek track position
    private async Task SeekPlayback(int positionMs)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot seek playback.");
            return;
        }

        await _spotifyClient.Player.SeekTo(new PlayerSeekToRequest(positionMs));
        Logger.LogInformation($"Playback position set to {positionMs} ms.");
    }

    // Set playlist shuffle mode on or off
    private async Task SetShuffle(bool shuffleState)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot set shuffle mode.");
            return;
        }

        await _spotifyClient.Player.SetShuffle(new PlayerShuffleRequest(shuffleState));
        Logger.LogInformation($"Shuffle mode set to {(shuffleState ? "on" : "off")}.");
    }

    // Set repeat mode to repeat-track, repeat-context or off
    private async Task SetRepeatMode(string repeatMode)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot set repeat mode.");
            return;
        }
        PlayerSetRepeatRequest.State repeatState;

        switch (repeatMode.ToLower())
        {
            case "repeat-track":
                repeatState = PlayerSetRepeatRequest.State.Track;
                break;
            case "repeat-context":
                repeatState = PlayerSetRepeatRequest.State.Context;
                break;
            case "off":
                repeatState = PlayerSetRepeatRequest.State.Off;
                break;
            default:
                Logger.LogError($"Invalid repeat mode: {repeatMode}");
                return;
        }

        var request = new PlayerSetRepeatRequest(repeatState);
        var result = await _spotifyClient.Player.SetRepeat(request);
        if (result)
        {
            Logger.LogInformation($"Repeat mode set to {repeatMode}.");
        }
        else
        {
            Logger.LogError($"Failed to set repeat mode to {repeatMode}.");
        }
    }

    // List users playlists
    private async Task<Dictionary<string, string>> ListAvailablePlaylists(CancellationToken cancel = default)
    {
        var playlistMap = new Dictionary<string, string>();

        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot list playlists.");
            return playlistMap;
        }

        var response = await _spotifyClient.Playlists.CurrentUsers(cancel);
        if (response.Items?.Any() == true)
        {
            //Logger.LogInformation("Available playlists:");
            foreach (var playlist in response.Items)
            {
                if (playlist?.Name == null || playlist?.Id == null)
                {
                    continue;
                }

                //Logger.LogInformation($"- {playlist.Name}");
                playlistMap[playlist.Name] = playlist.Id;
            }
        }
        else
        {
            Logger.LogWarning("No playlists available.");
        }

        return playlistMap;
    }

    // Add specific uri to a playlist
    private async Task AddItems(string playlistId, PlaylistAddItemsRequest request)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot add items to playlist.");
            return;
        }

        try
        {
            await _spotifyClient.Playlists.AddItems(playlistId, request);
            Logger.LogInformation($"Track added to playlist: {playlistId}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to add track to playlist: {ex.Message}");
        }
    }

    // List all available spotify clients
    private async Task<Dictionary<string, string>> ListAvailableDevices(CancellationToken cancel = default)
    {
        var deviceMap = new Dictionary<string, string>();

        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot list available devices.");
            return deviceMap;
        }

        var response = await _spotifyClient.Player.GetAvailableDevices(cancel);
        if (response.Devices.Any())
        {
            //Logger.LogInformation("Available devices:");
            foreach (var device in response.Devices)
            {
                //Logger.LogInformation($"- {device.Name} ({device.Type})");
                deviceMap[device.Name] = device.Id;
            }
        }
        else
        {
            Logger.LogWarning("No devices available.");
        }

        return deviceMap;
    }

    // Transfer playback to one specific spotify client
    private async Task TransferPlayback(string deviceId)
    {
        if (!await EnsureValidSpotifyClient() || _spotifyClient == null)
        {
            Logger.LogError("Spotify client not valid. Cannot transfer playback.");
            return;
        }

        await _spotifyClient.Player.TransferPlayback(new PlayerTransferPlaybackRequest(new List<string> { deviceId }));
        Logger.LogInformation($"Playback transferred to device: {deviceId}");
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////

    // Parse current playing track information
    private (string? Uri, string FriendlyName) GetCurrentTrackInfo()
    {
        if (_playbackState?.Item is FullTrack track)
        {
            var trackUri = track.Uri;
            var friendlyName = $"{track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))}";
            return (trackUri, friendlyName);
        }

        return (null, "Unknown Track");
    }

    // String cleaning
    private static string CleanString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove unwanted characters, preserving alphanumerics and spaces
        input = Regex.Replace(input, @"[^\w\s]", "");

        // Normalize whitespace to a single space
        input = Regex.Replace(input, @"\s+", " ").Trim();

        return input;
    }
}