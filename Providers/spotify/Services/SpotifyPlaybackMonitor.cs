using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Voxta.SampleProviderApp.Providers.Spotify.Helpers;

namespace Voxta.SampleProviderApp.Providers.Spotify.Services;

public class SpotifyPlaybackMonitor
{
    private readonly SpotifyManager _spotifyManager;
    private readonly ClientContextUpdater _contextUpdater;
    private readonly ILogger<SpotifyPlaybackMonitor> _logger;
    private readonly Action<string> _sendMessage;
    private CurrentlyPlayingContext? _lastKnownState;
    public CurrentlyPlayingContext? PlaybackState { get; private set; }
    private readonly bool _enableCharacterReplies;

    public SpotifyPlaybackMonitor(SpotifyManager spotifyManager, ClientContextUpdater contextUpdater, ILogger<SpotifyPlaybackMonitor> logger, Action<string> sendMessage, bool enableCharacterReplies = false)
    {
        _spotifyManager = spotifyManager;
        _contextUpdater = contextUpdater;
        _logger = logger;
        _sendMessage = sendMessage;
        _enableCharacterReplies = enableCharacterReplies;
    }

    public async Task MonitorSpotifyPlayback(CancellationToken cancellationToken)
    {
        _contextUpdater.UpdateClientContext(new[] { "spotify_disconnected" }, Array.Empty<string>());

        try
        {
            bool isFirstRun = _lastKnownState == null;
            while (!cancellationToken.IsCancellationRequested)
            {
                PlaybackState = await _spotifyManager.GetCurrentPlaybackState().ConfigureAwait(false);

                bool hasChanges = false;
                List<string> flags = new();
                List<string> contexts = new();

                bool isConnected = PlaybackState?.Device?.IsActive == true;
                bool wasConnected = _lastKnownState?.Device?.IsActive == true;
                bool isPlaying = PlaybackState?.IsPlaying == true;
                bool wasPlaying = _lastKnownState?.IsPlaying == true;
                bool hasTrack = PlaybackState?.Item is FullTrack;

                bool connectionChanged = isFirstRun || wasConnected != isConnected;
                if (connectionChanged)
                {
                    if (isConnected)
                    {
                        _logger.LogInformation("Spotify is now connected and active.");
                        SendWithPrefix("Spotify is now connected and active.");
                    }
                    else
                    {
                        _logger.LogInformation("No active Spotify player found");
                        SendWithPrefix("No active Spotify player found");
                    }
                }

                bool playbackChanged = isConnected && (isFirstRun || wasPlaying != isPlaying);
                if (playbackChanged)
                {
                    _logger.LogInformation(isPlaying
                        ? "Playback started"
                        : "Playback stopped");
                }

                if (hasTrack && (_lastKnownState?.Item is FullTrack lastTrack))
                {
                    var currentTrack = (FullTrack)PlaybackState!.Item;
                    if (currentTrack.Id != lastTrack.Id)
                    {
                        hasChanges = true;
                    }
                }
                else if (hasTrack && !(_lastKnownState?.Item is FullTrack))
                {
                    hasChanges = true;
                }

                if (hasTrack && HasPositionChanged(PlaybackState!, _lastKnownState!))
                {
                    hasChanges = true;
                }

                if (HasVolumeChanged(PlaybackState!, _lastKnownState!))
                {
                    hasChanges = true;
                }

                if (isConnected)
                {
                    flags.Add("spotify_connected");
                    flags.Add("!spotify_disconnected");
                    flags.Add(isPlaying ? "playing" : "!playing");

                    if (hasTrack)
                    {
                        var track = (FullTrack)PlaybackState!.Item;
                        var trackName = track.Name ?? "Unknown Track";
                        var artistName = string.Join(", ", track.Artists.Select(a => a.Name)) ?? "Unknown Artist";
                        var albumName = track.Album?.Name;
                        string? releaseYear = null;
                        if (!string.IsNullOrWhiteSpace(track.Album?.ReleaseDate))
                        {
                            releaseYear = track.Album.ReleaseDate.Split('-')[0];
                        }
                        var playedTime = StringUtils.FormatMillisecondsToMinutesSeconds(PlaybackState.ProgressMs);
                        var totalTime = StringUtils.FormatMillisecondsToMinutesSeconds(track.DurationMs);

                        var trackContext = albumName != null
                            ? $"{trackName} by {artistName} from the album {albumName}"
                            : $"{trackName} by {artistName}";

                        if (!string.IsNullOrEmpty(releaseYear))
                            trackContext += $" (Released in {releaseYear})";

                        trackContext += $" ({playedTime}/{totalTime})";

                        var volumeContext = $"(Volume: {PlaybackState.Device?.VolumePercent})";

                        if (isPlaying)
                            contexts.Add($"{trackContext} {volumeContext}");
                    }
                }
                else
                {
                    flags.Add("!spotify_connected");
                    flags.Add("!playing");
                    flags.Add("spotify_disconnected");
                }

                if (connectionChanged || playbackChanged || hasChanges)
                {
                    _lastKnownState = PlaybackState;
                    _contextUpdater.UpdateClientContext(flags.ToArray(), contexts.ToArray());
                }

                isFirstRun = false;
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Spotify playback monitoring stopped by request.");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Spotify playback monitoring canceled unexpectedly.");
        }
    }

    private bool HasConnectionStateChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.Device?.IsActive != oldState?.Device?.IsActive;
    }

    private bool HasPlaybackStateChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.IsPlaying != oldState?.IsPlaying;
    }

    private bool HasTrackChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.Item is FullTrack newTrack && oldState?.Item is FullTrack oldTrack && newTrack.Id != oldTrack.Id;
    }

    private bool HasVolumeChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.Device?.VolumePercent != oldState?.Device?.VolumePercent;
    }

    private bool HasPositionChanged(CurrentlyPlayingContext newState, CurrentlyPlayingContext oldState)
    {
        return newState?.Item is FullTrack newTrack && oldState?.Item is FullTrack oldTrack &&
               newTrack.Id == oldTrack.Id &&
               Math.Abs(newState.ProgressMs - oldState.ProgressMs) > 1000;
    }

    private void SendWithPrefix(string message)
    {
        string prefix = _enableCharacterReplies ? "/event" : "/note";
        _sendMessage($"{prefix} {message}");
    }
}
