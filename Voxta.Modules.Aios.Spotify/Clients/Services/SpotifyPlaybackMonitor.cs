using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using Voxta.Abstractions.Chats.Objects.Chats;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Model.Shared;

namespace Voxta.Modules.Aios.Spotify.Clients.Services;

public class SpotifyPlaybackMonitor(
    ISpotifyManager spotifyManager,
    IChatSessionChatAugmentationApi session,
    ILogger<SpotifyPlaybackMonitor> logger)
{
    private CurrentlyPlayingContext? _lastKnownState;
    private string? _lastAction;
    public CurrentlyPlayingContext? PlaybackState { get; private set; }

    public async Task MonitorSpotifyPlayback(CancellationToken cancellationToken)
    {
        await session.SetFlags(SetFlagRequest.ParseFlags(["spotify_disconnected"]), cancellationToken);
        await PublishContextsAsync(null, cancellationToken);

        try
        {
            var isFirstRun = _lastKnownState == null;
            while (!cancellationToken.IsCancellationRequested)
            {
                PlaybackState = await spotifyManager.GetCurrentPlaybackState(cancellationToken);

                var flags = new List<string>();

                var isConnected = PlaybackState?.Device?.IsActive == true;
                var wasConnected = _lastKnownState?.Device?.IsActive == true;
                var isPlaying = PlaybackState?.IsPlaying == true;
                var wasPlaying = _lastKnownState?.IsPlaying == true;
                var hasTrack = PlaybackState?.Item is FullTrack;

                var connectionChanged = isFirstRun || wasConnected != isConnected;
                var playbackChanged = isFirstRun || wasPlaying != isPlaying;
                
                if (connectionChanged)
                {
                    if (spotifyManager.IsAuthorizationRequired)
                    {
                        logger.LogInformation("Spotify authorization is required.");
                        flags.Add("!spotify_connected");
                        flags.Add("spotify_disconnected");
                    }
                    else if (isConnected)
                    {
                        logger.LogInformation("Spotify is now connected and active.");
                        flags.Add("spotify_connected");
                        flags.Add("!spotify_disconnected");
                    }
                    else
                    {
                        logger.LogInformation("No active Spotify player found");
                        flags.Add("!spotify_connected");
                        flags.Add("spotify_disconnected");
                    }
                }

                if (playbackChanged)
                {
                    if (isConnected)
                    {
                        logger.LogInformation(isPlaying ? "Playback started" : "Playback stopped");
                        flags.Add(isPlaying ? "playing" : "!playing");
                    }
                    else if(wasPlaying)
                    {
                        logger.LogInformation("Playback stopped");
                        flags.Add("!playing");
                    }
                }

                if (isFirstRun && !isConnected)
                {
                    flags.Add("!playing");
                }

                var hasChanges = false;
                if (hasTrack && _lastKnownState?.Item is FullTrack lastTrack)
                {
                    var currentTrack = (FullTrack)PlaybackState!.Item;
                    if (currentTrack.Id != lastTrack.Id) hasChanges = true;
                }
                else if (hasTrack && !(_lastKnownState?.Item is FullTrack)) hasChanges = true;

                if (_lastKnownState != null && HasVolumeChanged(PlaybackState!, _lastKnownState!)) hasChanges = true;

                if (flags.Any())
                {
                    await session.SetFlags(SetFlagRequest.ParseFlags(flags.Distinct().ToArray()), cancellationToken);
                }

                if (connectionChanged || playbackChanged || hasChanges)
                {
                    await PublishContextsAsync(PlaybackState, cancellationToken);
                    _lastKnownState = PlaybackState;
                }

                isFirstRun = false;
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Spotify playback monitoring stopped by request.");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Spotify playback monitoring canceled unexpectedly.");
        }
    }

    public async Task SetLastActionAsync(string action, CancellationToken cancellationToken)
    {
        _lastAction = action;
        await PublishContextsAsync(PlaybackState, cancellationToken);
    }

    private async Task PublishContextsAsync(CurrentlyPlayingContext? playbackState, CancellationToken cancellationToken)
    {
        var contexts = BuildContextDefinitions(playbackState);
        await session.SetContexts(VoxtaModule.ServiceName, contexts, cancellationToken);
    }

    private ContextDefinition[] BuildContextDefinitions(CurrentlyPlayingContext? playbackState)
    {
        var contexts = new List<ContextDefinition>();
        var isConnected = playbackState?.Device?.IsActive == true;

        AddContext(
            contexts,
            "connection",
            "Spotify Connection Status",
            spotifyManager.IsAuthorizationRequired
                ? spotifyManager.LastUserVisibleError ?? "Spotify authorization is required."
                : isConnected
                    ? "Spotify is connected and has an active player."
                    : "Spotify is disconnected. No active Spotify player was found.");

        if (playbackState?.Device != null)
        {
            var device = playbackState.Device;
            AddContext(
                contexts,
                "device",
                "Spotify Active Device",
                $"Active Spotify device: {device.Name ?? "Unknown device"} ({device.Type ?? "unknown type"}).");

            if (device.VolumePercent.HasValue)
            {
                AddContext(
                    contexts,
                    "volume",
                    "Spotify Volume",
                    $"Spotify volume: {device.VolumePercent.Value}%.");
            }
        }

        if (isConnected)
        {
            AddContext(
                contexts,
                "playback",
                "Spotify Playback Status",
                playbackState?.IsPlaying == true ? "Spotify playback is currently playing." : "Spotify playback is currently paused.");
        }

        if (playbackState?.Item is FullTrack track)
        {
            var trackName = track.Name ?? "Unknown Track";
            var artistName = string.Join(", ", track.Artists.Select(a => a.Name));
            if (string.IsNullOrWhiteSpace(artistName))
                artistName = "Unknown Artist";

            AddContext(
                contexts,
                "currently_playing",
                "Spotify Currently Playing",
                $"Currently playing on Spotify: {trackName} by {artistName}.");

            if (!string.IsNullOrWhiteSpace(track.Album?.Name))
            {
                var albumText = $"Current Spotify album: {track.Album.Name}.";
                if (!string.IsNullOrWhiteSpace(track.Album.ReleaseDate))
                    albumText += $" Released in {track.Album.ReleaseDate.Split('-')[0]}.";

                AddContext(contexts, "album", "Spotify Album", albumText);
            }
        }

        if (!string.IsNullOrWhiteSpace(_lastAction))
        {
            AddContext(contexts, "last_action", "Spotify Last Action", $"Last Spotify action: {_lastAction}");
        }

        return contexts.ToArray();
    }

    private static void AddContext(List<ContextDefinition> contexts, string key, string name, string text)
    {
        contexts.Add(new ContextDefinition
        {
            Id = CreateStableContextId(key),
            Name = name,
            Text = text
        });
    }

    private static Guid CreateStableContextId(string key)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{VoxtaModule.ServiceName}.{key}"));
        return new Guid(bytes);
    }

    private bool HasVolumeChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.Device?.VolumePercent != oldState?.Device?.VolumePercent;
    }

}
