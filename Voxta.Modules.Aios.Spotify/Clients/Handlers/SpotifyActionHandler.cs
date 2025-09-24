using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Modules.Aios.Spotify.ChatAugmentations;
using Voxta.Modules.Aios.Spotify.Clients.Services;
using Voxta.Modules.Aios.Spotify.Helpers;

namespace Voxta.Modules.Aios.Spotify.Clients.Handlers;

public class SpotifyActionHandler(
    ISpotifyManager spotifyManager,
    SpotifySearchService searchService,
    IChatSessionChatAugmentationApi session,
    SpotifyChatAugmentationSettings settings,
    ILogger<SpotifyActionHandler> logger,
    Func<CurrentlyPlayingContext?> getPlaybackState,
    bool enableCharacterReplies = true)
{
    private Dictionary<string, string> _deviceMap = new();
    private Dictionary<string, string> _playlistMap = new();

    public async Task HandleAction(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (message.Role != Model.Shared.ChatMessageRole.User) return;

        switch (message.Value)
        {
            case "toggle_playback":
                await HandleTogglePlayback(cancellationToken);
                break;
            case "spotify_connect":
                await SendWithPrefix($"No active Spotify client detected. Please start playback in your browser, desktop or mobile Spotify app to connect.", cancellationToken);
                break;
            case "play_random_music":
                await HandlePlayRandomMusic(cancellationToken);
                break;
            case "play_special_playlist":
                await HandlePlaySpecialPlaylist(message, cancellationToken);
                break;
            case "play_music":
                await HandlePlayMusic(message, cancellationToken);
                break;
            case "queue_track":
                await HandleQueueTrack(message, cancellationToken);
                break;
            case "volume":
                await HandleVolume(message, cancellationToken);
                break;
            case "seek_playback":
                await HandleSeekPlayback(message, cancellationToken);
                break;
            case "skip_next":
                await spotifyManager.SkipToPreviousOrNextTrack("next", cancellationToken);
                await SendWithPrefix($"As requested {{{{ char }}}} skipped to the next track", cancellationToken);
                break;
            case "skip_previous":
                await spotifyManager.SkipToPreviousOrNextTrack("previous", cancellationToken);
                await SendWithPrefix($"As requested {{{{ char }}}} skipped to the previous track", cancellationToken);
                break;
            case "repeat_mode":
                await HandleRepeatMode(message, cancellationToken);
                break;
            case "shuffle_mode":
                await HandleShuffleMode(message, cancellationToken);
                break;
            case "add_to_favorites":
                await HandleAddToFavorites(cancellationToken);
                break;
            case "get_playlists":
                await HandleGetPlaylists(cancellationToken);
                break;
            case "add_to_playlist":
                await HandleAddToPlaylist(message, cancellationToken);
                break;
            case "list_devices":
                await HandleListDevices(cancellationToken);
                break;
            case "transfer_to_device":
                await HandleTransferToDevice(message, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Action '{message.Value}' is not supported.");
        }
    }

    private async Task HandleTogglePlayback(CancellationToken cancellationToken)
    {
        var playbackState = getPlaybackState();
        var isPlaying = playbackState?.IsPlaying ?? false;
        logger.LogInformation($"Toggling music playback. Current state: {isPlaying}, toggling to: {(!isPlaying ? "play" : "pause")}");
        await spotifyManager.ControlSpotifyPlayback(!isPlaying, cancellationToken);
        await SendWithPrefix($"As requested {{{{ char }}}} toggled playback to: {(!isPlaying ? "play" : "pause")}", cancellationToken);
    }

    private async Task HandlePlayRandomMusic(CancellationToken cancellationToken)
    {
        var topTracks = await spotifyManager.GetUsersTopTracks(cancellationToken);
        if (topTracks?.Items is { Count: > 0 })
        {
            var random = new Random();
            var randomIndex = random.Next(topTracks.Items.Count);
            var selectedTrack = topTracks.Items[randomIndex];
            var randomTrackUri = selectedTrack.Uri;

            await spotifyManager.PlaySpecificUri(randomTrackUri, cancellationToken, "track");

            var trackName = selectedTrack.Name;
            var artistName = string.Join(", ", selectedTrack.Artists.Select(a => a.Name));

            await SendWithPrefix($" {trackName} by {artistName} has been selected by spotify based on {{{{ user }}}}'s top tracks.", cancellationToken);
        }
        else
        {
            await SendWithPrefix($"No top tracks found to play randomly.", cancellationToken);
        }
    }
    
    private async Task HandlePlaySpecialPlaylist(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("name", out var playlistName) || string.IsNullOrWhiteSpace(playlistName))
        {
            await SendWithPrefix("Special playlist name not provided.", cancellationToken);
            return;
        }

        var key = StringUtils.NormaliseKey(playlistName);

        if (settings.SpecialPlaylists.TryGetValue(key, out var playlistId))
        {
            var uri = $"spotify:playlist:{playlistId}";
            await spotifyManager.PlaySpecificUri(uri, cancellationToken, "playlist");
            await SendWithPrefix($"Playing your playlist: {playlistName}", cancellationToken);
            return;
        }
        
        /*var bestMatch = StringUtils.FindBestMatch(playlistName, settings.SpecialPlaylists.Keys);
        if (bestMatch != null && settings.SpecialPlaylists.TryGetValue(bestMatch, out playlistId) &&
            !string.IsNullOrWhiteSpace(playlistId))
        {
            var uri = $"spotify:playlist:{playlistId}";
            await spotifyManager.PlaySpecificUri(uri, cancellationToken, "playlist");
            await SendWithPrefix($"Playing your playlist: {bestMatch}", cancellationToken);
            return;
        }*/
        
        await SendWithPrefix($"No stored ID for '{playlistName}'. Please add it in the configuration.", cancellationToken);
    }

    private async Task HandlePlayMusic(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("name", out var playNameString))
            playNameString = "noop";

        if (playNameString == "noop")
        {
            await SendWithPrefix("Request not identified.", cancellationToken);
            return;
        }

        message.TryGetArgument("type", out var playTypeString);

        var validTypes = new[] { "track", "album", "artist", "playlist", "show", "episode", "genre" };

        if (!string.IsNullOrEmpty(playTypeString) && !validTypes.Contains(playTypeString))
        {
            logger.LogWarning($"Invalid type '{playTypeString}' received. Falling back to default type.");
            playTypeString = "track";
        }

        var originalType = playTypeString;

        if (playTypeString == "genre")
            playTypeString = "playlist";

        var (playUri, playFriendlyName, playType) =
            await searchService.GetSpotifyUri(playNameString, playTypeString, originalType);

        if (!string.IsNullOrEmpty(playUri))
        {
            await spotifyManager.PlaySpecificUri(playUri, cancellationToken, playType);
            await SendWithPrefix($"Playing {playType}: {playFriendlyName}", cancellationToken);
        }
        else
        {
            await SendWithPrefix("No matching results found to play.", cancellationToken);
        }
    }
    private async Task HandleQueueTrack(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("name", out var queueTypeString))
            queueTypeString = "noop";

        if (queueTypeString == "noop")
        {
            await SendWithPrefix($"Request not identified.", cancellationToken);
            return;
        }

        var (queueUri, queueFriendlyName, type) = await searchService.GetSpotifyUri(queueTypeString, requestedType: "track");

        if (queueUri != null && type == "track")
        {
            await spotifyManager.QueueTrack(queueUri, cancellationToken);
            await SendWithPrefix($"Added to queue: {queueFriendlyName}", cancellationToken);
        }
        else if (type != null && type != "track")
        {
            await SendWithPrefix($"Cannot queue {type}s. Try playing it instead.", cancellationToken);
        }
        else
        {
            await SendWithPrefix($"No matching results found to queue.", cancellationToken);
        }
    }


    private async Task HandleVolume(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("type", out var typeString))
        {
            await SendWithPrefix($"Volume change type not specified.", cancellationToken);
            return;
        }

        var playbackState = getPlaybackState();
        if (playbackState?.Device == null)
        {
            await SendWithPrefix($"No active Spotify device found to change volume.", cancellationToken);
            return;
        }

        var currentVolume = playbackState.Device.VolumePercent ?? 50;
        int newVolume;
        var step = 10;

        if (message.TryGetArgument("value", out var valueString) && int.TryParse(valueString, out var value))
        {
            step = value;
        }

        switch (typeString.ToLower())
        {
            case "set":
                newVolume = Math.Clamp(step, 0, 100);
                break;
            case "increase":
                newVolume = Math.Clamp(currentVolume + step, 0, 100);
                break;
            case "decrease":
                newVolume = Math.Clamp(currentVolume - step, 0, 100);
                break;
            default:
                await SendWithPrefix($"Invalid volume change type. Please use 'set', 'increase', or 'decrease'.", cancellationToken);
                return;
        }

        if (await spotifyManager.ChangeVolume(newVolume, cancellationToken))
        {
            await SendWithPrefix($"Volume changed to {newVolume}%.", cancellationToken);
        }
        else
        {
            await SendWithPrefix("Failed to change Spotify volume.", cancellationToken);
        }
    }

    private async Task HandleSeekPlayback(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("target", out var targetString))
        {
            await SendWithPrefix($"Seek target not specified.", cancellationToken);
            return;
        }

        var playbackState = getPlaybackState();
        if (playbackState == null || playbackState.Item is not FullTrack currentTrack)
        {
            await SendWithPrefix($"No track is currently playing to seek within.", cancellationToken);
            return;
        }

        long currentPositionMs = playbackState.ProgressMs;
        long trackDurationMs = currentTrack.DurationMs;
        var newPositionMs = currentPositionMs;
        int seconds;

        switch (targetString.ToLower())
        {
            case "forward":
                if (message.TryGetArgument("value", out var forwardSeconds) && int.TryParse(forwardSeconds, out seconds))
                {
                    newPositionMs = currentPositionMs + (seconds * 1000);
                }
                else
                {
                    await SendWithPrefix($"Seeking forward by 10 seconds.", cancellationToken);
                }
                break;
            case "backward":
                if (message.TryGetArgument("value", out var backwardSeconds) && int.TryParse(backwardSeconds, out seconds))
                {
                    newPositionMs = currentPositionMs - (seconds * 1000);
                }
                else
                {
                    newPositionMs = currentPositionMs - (10 * 1000);
                    await SendWithPrefix($"Seeking backward by 10 seconds.", cancellationToken);
                }
                break;
            case "to_time":
                if (message.TryGetArgument("value", out var timeInSeconds) && int.TryParse(timeInSeconds, out seconds))
                {
                    newPositionMs = seconds * 1000;
                }
                else
                {
                    await SendWithPrefix($"Please specify a time in seconds to seek to.", cancellationToken);
                    return;
                }
                break;
            case "to_percent":
                if (message.TryGetArgument("value", out var percentString) && int.TryParse(percentString, out var percent))
                {
                    newPositionMs = (long)(trackDurationMs * (percent / 100.0));
                }
                else
                {
                    await SendWithPrefix($"Please specify a percentage to seek to.", cancellationToken);
                    return;
                }
                break;
            case "middle":
                newPositionMs = trackDurationMs / 2;
                break;
            default:
                await SendWithPrefix($"Invalid seek target. Please use 'forward', 'backward', 'to_time', 'to_percent', or 'middle'.", cancellationToken);
                return;
        }

        newPositionMs = Math.Max(0, Math.Min(newPositionMs, trackDurationMs));

        await spotifyManager.SeekPlayback((int)newPositionMs, cancellationToken);
        await SendWithPrefix($"Playback position updated to {StringUtils.FormatMillisecondsToMinutesSeconds((int)newPositionMs)}.", cancellationToken);
    }

    private async Task HandleRepeatMode(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("mode", out var repeatMode))
            repeatMode = "repeat-track";
        repeatMode = StringUtils.CleanString(repeatMode);
        await spotifyManager.SetRepeatMode(repeatMode, cancellationToken);
        await SendWithPrefix($"As requested {{{{ char }}}} set the repeat mode to: {repeatMode}", cancellationToken);
    }

    private async Task HandleShuffleMode(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("mode", out var shuffleMode))
            shuffleMode = "off";
        shuffleMode = StringUtils.CleanString(shuffleMode);
        var shuffleModeBool = shuffleMode != "off";
        await spotifyManager.SetShuffle(shuffleModeBool, cancellationToken);
        await SendWithPrefix($"As requested {{{{ char }}}} set the shuffle mode to: {shuffleModeBool}", cancellationToken);
    }

    private async Task HandleAddToFavorites(CancellationToken cancellationToken)
    {
        var (trackUri, trackFriendlyName) = GetCurrentTrackInfo();
        if (string.IsNullOrEmpty(trackUri))
        {
            await SendWithPrefix("No track is currently playing.", cancellationToken);
            return;
        }

        var trackId = StringUtils.ExtractIdFromUri(trackUri);
        if (string.IsNullOrEmpty(trackId))
        {
            await SendWithPrefix("Could not extract track ID from current track.", cancellationToken);
            return;
        }

        try
        {
            await spotifyManager.AddTrackToLibraryAsync(trackId, trackFriendlyName, cancellationToken);

            await SendWithPrefix($"Track '{trackFriendlyName}' added to your Favorites.", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to add track to favorites: {ex.Message}");
            await SendWithPrefix("Failed to add track to Favorites.", cancellationToken);
        }
    }

    private async Task HandleGetPlaylists(CancellationToken cancellationToken)
    {
        _playlistMap = await spotifyManager.ListAvailablePlaylists(cancellationToken);
        if (_playlistMap.Any())
        {
            var playlistList = string.Join(", ", _playlistMap.Keys);
            await SendWithPrefix($"Available playlists: {playlistList}", cancellationToken);
        }
        else
        {
            await SendWithPrefix($"No playlists available.", cancellationToken);
        }
    }

    private async Task HandleAddToPlaylist(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("playlist", out var playlistFriendlyName) || string.IsNullOrEmpty(playlistFriendlyName))
        {
            await SendWithPrefix($"No playlist specified.", cancellationToken);
            return;
        }

        _playlistMap = await spotifyManager.ListAvailablePlaylists(cancellationToken);

        playlistFriendlyName = StringUtils.CleanString(playlistFriendlyName);

        var matchedPlaylist = _playlistMap.FirstOrDefault(p =>
            p.Key.Equals(playlistFriendlyName, StringComparison.OrdinalIgnoreCase));

        if (matchedPlaylist.Key == null)
        {
            await SendWithPrefix($"Playlist not found: {playlistFriendlyName}", cancellationToken);
            return;
        }

        var playlistId = StringUtils.ExtractIdFromUri(matchedPlaylist.Value);

        var (trackUri, trackFriendlyName) = GetCurrentTrackInfo();
        if (string.IsNullOrEmpty(trackUri))
        {
            await SendWithPrefix($"No track is currently playing.", cancellationToken);
            return;
        }

        var request = new PlaylistAddItemsRequest(new List<string> { trackUri });
        await spotifyManager.AddItems(playlistId, request, cancellationToken);

        await SendWithPrefix($"Track '{trackFriendlyName}' added to playlist '{playlistFriendlyName}'.", cancellationToken);
    }

    private async Task HandleListDevices(CancellationToken cancellationToken)
    {
        var deviceMap = await spotifyManager.ListAvailableDevices(cancellationToken);
        if (deviceMap.Any())
        {
            var deviceList = string.Join(", ", deviceMap.Keys);
            await SendWithPrefix($"Available devices: {deviceList}", cancellationToken);
        }
        else
        {
            await SendWithPrefix($"No devices available.", cancellationToken);
        }
    }

    private async Task HandleTransferToDevice(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("device", out var partialName) || string.IsNullOrEmpty(partialName))
        {
            await SendWithPrefix($"No device specified.", cancellationToken);
            return;
        }

        _deviceMap = await spotifyManager.ListAvailableDevices(cancellationToken);

        partialName = StringUtils.CleanString(partialName);

        var matchedDevice = _deviceMap.FirstOrDefault(d =>
            d.Key.Contains(partialName, StringComparison.OrdinalIgnoreCase));

        if (matchedDevice.Key == null)
        {
            var possibleMatches = _deviceMap.Keys
                .Where(k => k.Contains(partialName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (possibleMatches.Any())
            {
                await SendWithPrefix($"Did you mean: {string.Join(", ", possibleMatches)}?", cancellationToken);
            }
            else
            {
                await SendWithPrefix($"No device found matching: {partialName}", cancellationToken);
            }
            return;
        }
        await spotifyManager.TransferPlayback(matchedDevice.Value, cancellationToken);
        await SendWithPrefix($"Playback transferred to: {matchedDevice.Key}", cancellationToken);
    }
    private async Task SendWithPrefix(string message, CancellationToken cancellationToken)
    {
        await session.SendNoteAsync(message, cancellationToken);
        if (enableCharacterReplies)
            await session.TriggerReplyAsync(cancellationToken);
    }
    private (string? Uri, string FriendlyName) GetCurrentTrackInfo()
    {
        var playbackState = getPlaybackState();
        if (playbackState?.Item is FullTrack track)
        {
            var trackUri = track.Uri;
            var friendlyName = $"{track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))}";
            return (trackUri, friendlyName);
        }

        return (null, "Unknown Track");
    }
}
