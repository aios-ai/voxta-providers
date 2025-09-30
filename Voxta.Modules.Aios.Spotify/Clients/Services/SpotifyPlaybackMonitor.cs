using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using Voxta.Abstractions.Chats.Objects.Chats;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Model.Shared;
using Voxta.Modules.Aios.Spotify.Helpers;

namespace Voxta.Modules.Aios.Spotify.Clients.Services;

public class SpotifyPlaybackMonitor(
    ISpotifyManager spotifyManager,
    IChatSessionChatAugmentationApi session,
    ILogger<SpotifyPlaybackMonitor> logger,
    bool enableCharacterReplies = false)
{
    private CurrentlyPlayingContext? _lastKnownState;
    public CurrentlyPlayingContext? PlaybackState { get; private set; }

    public async Task MonitorSpotifyPlayback(CancellationToken cancellationToken)
    {
        await session.SetFlags(SetFlagRequest.ParseFlags(["spotify_disconnected"]), cancellationToken);
        await session.SetContexts(VoxtaModule.ServiceName, [], cancellationToken);

        try
        {
            var isFirstRun = _lastKnownState == null;
            while (!cancellationToken.IsCancellationRequested)
            {
                PlaybackState = await spotifyManager.GetCurrentPlaybackState(cancellationToken);

                var hasChanges = false;
                List<string> flags = new();
                List<string> contexts = new();

                var isConnected = PlaybackState?.Device?.IsActive == true;
                var wasConnected = _lastKnownState?.Device?.IsActive == true;
                var isPlaying = PlaybackState?.IsPlaying == true;
                var wasPlaying = _lastKnownState?.IsPlaying == true;
                var hasTrack = PlaybackState?.Item is FullTrack;

                var connectionChanged = isFirstRun || wasConnected != isConnected;
                if (connectionChanged)
                {
                    if (isConnected)
                    {
                        logger.LogInformation("Spotify is now connected and active.");
                        await SendWithPrefixAsync("Spotify is now connected and active.", cancellationToken);
                    }
                    else
                    {
                        logger.LogInformation("No active Spotify player found");
                        await SendWithPrefixAsync("No active Spotify player found", cancellationToken);
                    }
                }

                var playbackChanged = isConnected && (isFirstRun || wasPlaying != isPlaying);
                if (playbackChanged)
                {
                    logger.LogInformation(isPlaying
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
                    await session.SetFlags(SetFlagRequest.ParseFlags(flags.ToArray()), cancellationToken);
                    var contextDefinitions = contexts
                        .Select(c => new ContextDefinition
                        {
                            Text = c
                        })
                        .ToArray();
                    await session.SetContexts(VoxtaModule.ServiceName, contextDefinitions, cancellationToken);
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

    private async Task SendWithPrefixAsync(string message, CancellationToken cancellationToken)
    {
        await session.SendNoteAsync(message, cancellationToken);
        if (enableCharacterReplies)
            await session.TriggerReplyAsync(cancellationToken);
    }
}
