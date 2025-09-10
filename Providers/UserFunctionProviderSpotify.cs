using Microsoft.Extensions.Logging;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Providers.Host;
using System.Text.Json;
using SpotifyAPI.Web;
using Voxta.SampleProviderApp.Providers.Spotify.Services;
using Voxta.SampleProviderApp.Providers.Spotify.Handlers;
using Voxta.SampleProviderApp.Providers.Spotify.Models;

namespace Voxta.SampleProviderApp.Providers;

public class UserFunctionProviderSpotify(
    IRemoteChatSession session,
    ILogger<UserFunctionProviderSpotify> logger,
    ILogger<SpotifySearchService> searchServiceLogger,
    ILogger<SpotifyPlaybackMonitor> playbackMonitorLogger,
    ILogger<ActionHandler> actionHandlerLogger
)
    : ProviderBase(session, logger)
{
    private SpotifyManager? _spotifyManager;
    private SpotifyConfig? _spotifyConfig = new();
    private Voxta.SampleProviderApp.Providers.Spotify.Services.ClientContextUpdater? _contextUpdater;
    private CancellationTokenSource? _cancellationTokenSource;
    private SpotifyPlaybackMonitor? _playbackMonitor;
    private ActionHandler? _actionHandler;
    private SpotifySearchService? _searchService;

    protected override async Task OnStartAsync()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        await base.OnStartAsync().ConfigureAwait(false);

        LoadConfiguration();
        if (_spotifyConfig == null)
        {
            logger.LogError("Configuration not loaded properly.");
            return;
        }

        _contextUpdater = new Voxta.SampleProviderApp.Providers.Spotify.Services.ClientContextUpdater(SessionId, Send, _spotifyConfig!.enableMatchFilter, _spotifyConfig!.matchFilterWakeWord);

        var tokenFilePath = Path.Combine(Directory.GetCurrentDirectory(), _spotifyConfig!.tokenPath!);
        _spotifyManager = new SpotifyManager(_spotifyConfig!, tokenFilePath, logger);

        await _spotifyManager.InitializeSpotifyClient().ConfigureAwait(false);

        if (!_spotifyManager.HasClient)
        {
            logger.LogError("Spotify client could not be initialized.");
            return;
        }

        _searchService = new SpotifySearchService(_spotifyManager!, searchServiceLogger);
        _playbackMonitor = new SpotifyPlaybackMonitor(_spotifyManager!, _contextUpdater!, playbackMonitorLogger, SendMessage, _spotifyConfig!.enableCharacterReplies);
        _actionHandler = new ActionHandler(_spotifyManager!, _searchService, SendMessage, actionHandlerLogger, () => _playbackMonitor!.PlaybackState, _spotifyConfig!.enableCharacterReplies);

        _ = _playbackMonitor.MonitorSpotifyPlayback(_cancellationTokenSource.Token);

        await _searchService.InitializeAsync();

        HandleMessage<ServerActionMessage>(async message =>
        {
            if (_actionHandler != null)
            {
                await _actionHandler.HandleAction(message).ConfigureAwait(false);
            }
        });
    }

    protected override async Task OnStopAsync()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        await base.OnStopAsync().ConfigureAwait(false);
    }

    private void SendMessage(string message)
    {
        Send(new ClientSendMessage
        {
            SessionId = SessionId,
            DoUserActionInference = false,
            Text = message
        });
    }

    private void LoadConfiguration()
    {
        var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Providers\\spotify\\config\\UserFunctionProviderSpotifyConfig.json");

        if (!File.Exists(configPath))
        {
            logger.LogError("Configuration file not found at {ConfigPath}", configPath);
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            _spotifyConfig = JsonSerializer.Deserialize<SpotifyConfig>(json);
            if (_spotifyConfig == null ||
                string.IsNullOrEmpty(_spotifyConfig.clientId) ||
                string.IsNullOrEmpty(_spotifyConfig.clientSecret) ||
                string.IsNullOrEmpty(_spotifyConfig.redirectUri) ||
                string.IsNullOrEmpty(_spotifyConfig.tokenPath))
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
}