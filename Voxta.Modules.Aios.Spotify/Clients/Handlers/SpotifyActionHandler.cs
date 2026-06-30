using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Modules.Aios.Spotify.ChatAugmentations;
using Voxta.Modules.Aios.Spotify.Clients.Services;
using Voxta.Modules.Aios.Spotify.Helpers;

namespace Voxta.Modules.Aios.Spotify.Clients.Handlers;

public class SpotifyActionHandler(
    ISpotifyManager spotifyManager,
    SpotifySearchService searchService,
    IChatSessionChatAugmentationApi session,
    SpotifyChatAugmentationsSettings settings,
    ILogger<SpotifyActionHandler> logger,
    Func<CurrentlyPlayingContext?> getPlaybackState,
    Func<string, CancellationToken, Task> setLastAction)
{
    private Dictionary<string, string> _deviceMap = new();
    private Dictionary<string, string> _playlistMap = new();
    private readonly AsyncLocal<bool> _cancelReply = new();
    private int? _lastKnownVolume;
    
    public Task LowerVolumeAsync(CancellationToken cancellationToken) => FadeVolumeAsync(settings.SpeechDuckingVolumePercent, cancellationToken);
    public async Task RestoreVolumeAsync(CancellationToken cancellationToken)
    {
        if (_lastKnownVolume.HasValue)
        {
            await spotifyManager.ChangeVolume(_lastKnownVolume.Value, cancellationToken);
            _lastKnownVolume = null;
        }
    }
    
    public async Task HandleAction(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (message.Role != Model.Shared.ChatMessageRole.User) return;

        _cancelReply.Value = IsCancelReplyEnabled(message.Value);
        try
        {
            switch (message.Value)
            {
                case "toggle_playback":
                    await HandleTogglePlayback(cancellationToken);
                    break;
                case "spotify_connect":
                    await SendSpotifyFailureOrDefault($"No active Spotify client detected. Please start playback in your browser, desktop or mobile Spotify app to connect.", cancellationToken);
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
                    if (await spotifyManager.SkipToPreviousOrNextTrack("next", cancellationToken))
                    {
                        await SetLastActionAndSendSecret("Skipped to the next track.", cancellationToken);
                    }
                    else
                        await SendSpotifyFailureOrDefault($"Failed to skip to the next Spotify track.", cancellationToken);
                    break;
                case "skip_previous":
                    if (await spotifyManager.SkipToPreviousOrNextTrack("previous", cancellationToken))
                    {
                        await SetLastActionAndSendSecret("Skipped to the previous track.", cancellationToken);
                    }
                    else
                        await SendSpotifyFailureOrDefault($"Failed to skip to the previous Spotify track.", cancellationToken);
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
                    logger.LogWarning("Unsupported Spotify action '{Action}' received.", message.Value);
                    await SendWithPrefix($"That Spotify action is not supported.", cancellationToken);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Spotify action '{Action}' failed unexpectedly.", message.Value);
            await SendSpotifyFailureOrDefault("Spotify could not complete that request. Please check Spotify and try again.", cancellationToken);
        }
    }

    private bool IsCancelReplyEnabled(string actionName)
    {
        return settings.Actions
            .FirstOrDefault(action => string.Equals(action.Name, actionName, StringComparison.OrdinalIgnoreCase))
            ?.CancelReply is { } value
            ? SpotifyChatAugmentationsService.ParseActionBoolean(value, true)
            : true;
    }

    private async Task HandleTogglePlayback(CancellationToken cancellationToken)
    {
        var playbackState = getPlaybackState();
        var isPlaying = playbackState?.IsPlaying ?? false;
        logger.LogInformation($"Toggling music playback. Current state: {isPlaying}, toggling to: {(!isPlaying ? "play" : "pause")}");
        if (await spotifyManager.ControlSpotifyPlayback(!isPlaying, cancellationToken))
        {
            await SetLastActionAndSendSecret($"Toggled playback to {(!isPlaying ? "play" : "pause")}.", cancellationToken);
        }
        else
            await SendSpotifyFailureOrDefault("Failed to toggle Spotify playback.", cancellationToken);
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

            if (!await spotifyManager.PlaySpecificUri(randomTrackUri, cancellationToken, "track"))
            {
                await SendSpotifyFailureOrDefault("Failed to play a random Spotify track.", cancellationToken);
                return;
            }

            var trackName = selectedTrack.Name;
            var artistName = string.Join(", ", selectedTrack.Artists.Select(a => a.Name));

            await setLastAction($"Started random track: {trackName} by {artistName}.", cancellationToken);
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
            if (await spotifyManager.PlaySpecificUri(uri, cancellationToken, "playlist"))
            {
                await setLastAction($"Started playlist: {playlistName}.", cancellationToken);
                await SendWithPrefix($"Playing your playlist: {playlistName}", cancellationToken);
            }
            else
                await SendSpotifyFailureOrDefault($"Failed to play your playlist: {playlistName}", cancellationToken);
            return;
        }
                
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
            if (await spotifyManager.PlaySpecificUri(playUri, cancellationToken, playType))
            {
                await setLastAction($"Started {playType}: {playFriendlyName}.", cancellationToken);
                await SendWithPrefix($"Playing {playType}: {playFriendlyName}", cancellationToken);
            }
            else
                await SendSpotifyFailureOrDefault($"Failed to play {playType}: {playFriendlyName}", cancellationToken);
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
            if (await spotifyManager.QueueTrack(queueUri, cancellationToken))
            {
                await setLastAction($"Queued track: {queueFriendlyName}.", cancellationToken);
                await SendWithPrefix($"Added to queue: {queueFriendlyName}", cancellationToken);
            }
            else
                await SendSpotifyFailureOrDefault($"Failed to add to queue: {queueFriendlyName}", cancellationToken);
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
    
    private async Task FadeVolumeAsync(int targetVolume, CancellationToken cancellationToken)
    {
        targetVolume = Math.Clamp(targetVolume, 0, 100);
        if (targetVolume >= 100) return;

        var playbackState = getPlaybackState();
        if (playbackState?.Device?.Id == null) return;
        
        if ((playbackState.Device.VolumePercent ?? 100) <= targetVolume)
            return;
        
        _lastKnownVolume = playbackState.Device.VolumePercent;
        
        await spotifyManager.ChangeVolume(targetVolume, cancellationToken);
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
            await SetLastActionAndSendSecret($"Changed volume to {newVolume}%.", cancellationToken);
        }
        else
        {
            await SendSpotifyFailureOrDefault("Failed to change Spotify volume.", cancellationToken);
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

        if (await spotifyManager.SeekPlayback((int)newPositionMs, cancellationToken))
        {
            await SetLastActionAndSendSecret($"Set playback position to {StringUtils.FormatMillisecondsToMinutesSeconds((int)newPositionMs)}.", cancellationToken);
        }
        else
            await SendSpotifyFailureOrDefault("Failed to seek Spotify playback.", cancellationToken);
    }

    private async Task HandleRepeatMode(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("mode", out var repeatMode))
            repeatMode = "repeat-track";
        repeatMode = StringUtils.CleanString(repeatMode);
        if (await spotifyManager.SetRepeatMode(repeatMode, cancellationToken))
        {
            await setLastAction($"Set repeat mode to {repeatMode}.", cancellationToken);
            await SendWithPrefix($"Repeat mode set to {repeatMode}.", cancellationToken);
        }
        else
            await SendSpotifyFailureOrDefault("Failed to change Spotify repeat mode.", cancellationToken);
    }

    private async Task HandleShuffleMode(ServerActionMessage message, CancellationToken cancellationToken)
    {
        if (!message.TryGetArgument("mode", out var shuffleMode))
            shuffleMode = "off";
        shuffleMode = StringUtils.CleanString(shuffleMode);
        var shuffleModeBool = shuffleMode != "off";
        if (await spotifyManager.SetShuffle(shuffleModeBool, cancellationToken))
        {
            await setLastAction($"Set shuffle mode to {(shuffleModeBool ? "on" : "off")}.", cancellationToken);
            await SendWithPrefix($"Shuffle mode set to {(shuffleModeBool ? "on" : "off")}.", cancellationToken);
        }
        else
            await SendSpotifyFailureOrDefault("Failed to change Spotify shuffle mode.", cancellationToken);
    }

    private async Task HandleAddToFavorites(CancellationToken cancellationToken)
    {
        var (trackUri, trackFriendlyName) = GetCurrentTrackInfo();
        if (string.IsNullOrEmpty(trackUri))
        {
            await SendWithPrefix("No track is currently playing.", cancellationToken);
            return;
        }

        try
        {
            if (await spotifyManager.AddTrackToLibraryAsync(trackUri, trackFriendlyName, cancellationToken))
            {
                await setLastAction($"Added {trackFriendlyName} to Favorites.", cancellationToken);
                await SendWithPrefix($"Track '{trackFriendlyName}' added to your Favorites.", cancellationToken);
            }
            else
                await SendSpotifyFailureOrDefault("Failed to add track to Favorites.", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to add track to favorites: {ex.Message}");
            await SendSpotifyFailureOrDefault("Failed to add track to Favorites.", cancellationToken);
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
            await SendSpotifyFailureOrDefault($"No playlists available.", cancellationToken);
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
        if (await spotifyManager.AddItems(playlistId, request, cancellationToken))
        {
            await setLastAction($"Added {trackFriendlyName} to playlist {playlistFriendlyName}.", cancellationToken);
            await SendWithPrefix($"Track '{trackFriendlyName}' added to playlist '{playlistFriendlyName}'.", cancellationToken);
        }
        else
            await SendSpotifyFailureOrDefault($"Failed to add track to playlist '{playlistFriendlyName}'.", cancellationToken);
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
            await SendSpotifyFailureOrDefault($"No devices available.", cancellationToken);
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
        if (await spotifyManager.TransferPlayback(matchedDevice.Value, cancellationToken))
        {
            await SetLastActionAndSendSecret($"Transferred playback to {matchedDevice.Key}.", cancellationToken);
        }
        else
            await SendSpotifyFailureOrDefault($"Failed to transfer playback to: {matchedDevice.Key}", cancellationToken);
    }

    private async Task SetLastActionAndSendSecret(string action, CancellationToken cancellationToken)
    {
        await setLastAction(action, cancellationToken);
        if (_cancelReply.Value)
        {
            await session.SendNoteAsync(action, cancellationToken);
            return;
        }

        await session.SendSecretAsync(action, cancellationToken);
    }
    
    private Task SendSpotifyFailureOrDefault(string fallbackMessage, CancellationToken cancellationToken)
    {
        return SendWithPrefix(spotifyManager.LastUserVisibleError ?? fallbackMessage, cancellationToken);
    }
    
    private async Task SendWithPrefix(string message, CancellationToken cancellationToken)
    {
        if (_cancelReply.Value)
        {
            await session.SendNoteAsync(message, cancellationToken);
            return;
        }

        await session.SendSecretAsync(message, cancellationToken);
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
