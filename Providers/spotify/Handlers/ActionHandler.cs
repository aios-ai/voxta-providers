
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.SampleProviderApp.Providers.Spotify.Helpers;
using Voxta.SampleProviderApp.Providers.Spotify.Services;

namespace Voxta.SampleProviderApp.Providers.Spotify.Handlers;

public class ActionHandler
{
    private readonly SpotifyManager _spotifyManager;
    private readonly SpotifySearchService _searchService;
    private readonly Action<string> _sendMessage;
    private readonly ILogger<ActionHandler> _logger;
    private Dictionary<string, string> _deviceMap = new();
    private Dictionary<string, string> _playlistMap = new();
    private readonly Func<CurrentlyPlayingContext?> _getPlaybackState;
    private readonly bool _enableCharacterReplies;

    public ActionHandler(
        SpotifyManager spotifyManager,
        SpotifySearchService searchService,
        Action<string> sendMessage,
        ILogger<ActionHandler> logger,
        Func<CurrentlyPlayingContext?> getPlaybackState,
        bool enableCharacterReplies = true)
    {
        _spotifyManager = spotifyManager;
        _searchService = searchService;
        _sendMessage = sendMessage;
        _logger = logger;
        _getPlaybackState = getPlaybackState;
        _enableCharacterReplies = enableCharacterReplies;
    }

    public async Task HandleAction(ServerActionMessage message)
    {
        if (message.Role != Model.Shared.ChatMessageRole.User) return;

        switch (message.Value)
        {
            case "toggle_playback":
                await HandleTogglePlayback().ConfigureAwait(false);
                break;
            case "spotify_connect":
                SendWithPrefix($"No active Spotify client detected. Please start playback in your browser, desktop or mobile Spotify app to connect.");
                break;
            case "play_random_music":
                await HandlePlayRandomMusic().ConfigureAwait(false);
                break;
            case "play_special_playlist":
                await HandlePlaySpecialPlaylist(message).ConfigureAwait(false);
                break;
            case "play_music":
                await HandlePlayMusic(message).ConfigureAwait(false);
                break;
            case "queue_track":
                await HandleQueueTrack(message).ConfigureAwait(false);
                break;
            case "volume":
                await HandleVolume(message).ConfigureAwait(false);
                break;
            case "seek_playback":
                await HandleSeekPlayback(message).ConfigureAwait(false);
                break;
            case "skip_next":
                await _spotifyManager.SkipToPreviousOrNextTrack("next").ConfigureAwait(false);
                SendWithPrefix($"As requested {{{{ char }}}} skipped to the next track");
                break;
            case "skip_previous":
                await _spotifyManager.SkipToPreviousOrNextTrack("previous").ConfigureAwait(false);
                SendWithPrefix($"As requested {{{{ char }}}} skipped to the previous track");
                break;
            case "repeat_mode":
                await HandleRepeatMode(message).ConfigureAwait(false);
                break;
            case "shuffle_mode":
                await HandleShuffleMode(message).ConfigureAwait(false);
                break;
            case "add_to_favorites":
                await HandleAddToFavorites(message).ConfigureAwait(false);
                break;
            case "get_playlists":
                await HandleGetPlaylists().ConfigureAwait(false);
                break;
            case "add_to_playlist":
                await HandleAddToPlaylist(message).ConfigureAwait(false);
                break;
            case "list_devices":
                await HandleListDevices().ConfigureAwait(false);
                break;
            case "transfer_to_device":
                await HandleTransferToDevice(message).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleTogglePlayback()
    {
        var playbackState = _getPlaybackState();
        bool isPlaying = playbackState?.IsPlaying ?? false;
        _logger.LogInformation($"Toggling music playback. Current state: {isPlaying}, toggling to: {(!isPlaying ? "play" : "pause")}");
        await _spotifyManager.ControlSpotifyPlayback(!isPlaying).ConfigureAwait(false);
        SendWithPrefix($"As requested {{{{ char }}}} toggled playback to: {(!isPlaying ? "play" : "pause")}");
    }

    private async Task HandlePlayRandomMusic()
    {
        var topTracks = await _spotifyManager.GetUsersTopTracks().ConfigureAwait(false);
        if (topTracks?.Items != null && topTracks.Items.Count > 0)
        {
            var random = new Random();
            var randomIndex = random.Next(topTracks.Items.Count);
            var selectedTrack = topTracks.Items[randomIndex];
            var randomTrackUri = selectedTrack.Uri;

            if (randomTrackUri != null)
            {
                await _spotifyManager.PlaySpecificUri(randomTrackUri, "track").ConfigureAwait(false);

                var trackName = selectedTrack.Name ?? "Unknown Track";
                var artistName = string.Join(", ", selectedTrack.Artists.Select(a => a.Name)) ?? "Unknown Artist";

                SendWithPrefix($" {trackName} by {artistName} has been selected by spotify based on {{{{ user }}}}'s top tracks.");
            }
        }
        else
        {
            SendWithPrefix($"No top tracks found to play randomly.");
        }
    }

    private async Task HandlePlaySpecialPlaylist(ServerActionMessage message)
    {
        if (!message.TryGetArgument("name", out var playlistName) || string.IsNullOrWhiteSpace(playlistName))
        {
            SendWithPrefix("Special playlist name not provided.");
            return;
        }

        var canonicalName = StringUtils.NormaliseSpecialName(playlistName);

        if (StringUtils.SpecialPlaylistMap.TryGetValue(canonicalName, out var playlistId) && !string.IsNullOrWhiteSpace(playlistId))
        {
            var uri = $"spotify:playlist:{playlistId}";
            await _spotifyManager.PlaySpecificUri(uri, "playlist").ConfigureAwait(false);
            SendWithPrefix($"Playing your playlist: {canonicalName}");
            return;
        }

        SendWithPrefix($"No stored ID for '{canonicalName}'. Please paste the playlist ID so I can save it.");
    }

    private async Task HandlePlayMusic(ServerActionMessage message)
    {
        if (!message.TryGetArgument("name", out var playNameString))
            playNameString = "noop";

        if (playNameString == "noop")
        {
            SendWithPrefix("Request not identified.");
            return;
        }

        message.TryGetArgument("type", out var playTypeString);

        var validTypes = new[] { "track", "album", "artist", "playlist", "show", "episode", "genre" };

        if (!string.IsNullOrEmpty(playTypeString) && !validTypes.Contains(playTypeString))
        {
            _logger.LogWarning($"Invalid type '{playTypeString}' received. Falling back to default type.");
            playTypeString = "track";
        }

        var originalType = playTypeString;

        if (playTypeString == "genre")
            playTypeString = "playlist";

        var (playUri, playFriendlyName, playType) =
            await _searchService.GetSpotifyUri(playNameString, playTypeString, originalType).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(playUri))
        {
            await _spotifyManager.PlaySpecificUri(playUri, playType).ConfigureAwait(false);
            SendWithPrefix($"Playing {playType}: {playFriendlyName}");
        }
        else
        {
            SendWithPrefix("No matching results found to play.");
        }
    }
    private async Task HandleQueueTrack(ServerActionMessage message)
    {
        if (!message.TryGetArgument("name", out var queueTypeString))
            queueTypeString = "noop";

        if (queueTypeString == "noop")
        {
            SendWithPrefix($"Request not identified.");
            return;
        }

        var (queueUri, queueFriendlyName, type) = await _searchService.GetSpotifyUri(queueTypeString, requestedType: "track").ConfigureAwait(false);

        if (queueUri != null && type == "track")
        {
            await _spotifyManager.QueueTrack(queueUri).ConfigureAwait(false);
            SendWithPrefix($"Added to queue: {queueFriendlyName}");
        }
        else if (type != null && type != "track")
        {
            SendWithPrefix($"Cannot queue {type}s. Try playing it instead.");
        }
        else
        {
            SendWithPrefix($"No matching results found to queue.");
        }
    }


    private async Task HandleVolume(ServerActionMessage message)
    {
        if (!message.TryGetArgument("type", out var typeString))
        {
            SendWithPrefix($"Volume change type not specified.");
            return;
        }

        var playbackState = _getPlaybackState();
        if (playbackState == null || playbackState.Device == null)
        {
            SendWithPrefix($"No active Spotify device found to change volume.");
            return;
        }

        int currentVolume = playbackState.Device.VolumePercent ?? 50;
        int newVolume = currentVolume;
        int step = 10;

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
                SendWithPrefix($"Invalid volume change type. Please use 'set', 'increase', or 'decrease'.");
                return;
        }

        if (await _spotifyManager.ChangeVolume(newVolume).ConfigureAwait(false))
        {
            SendWithPrefix($"Volume changed to {newVolume}%.");
        }
        else
        {
            SendWithPrefix("Failed to change Spotify volume.");
        }
    }

    private async Task HandleSeekPlayback(ServerActionMessage message)
    {
        if (!message.TryGetArgument("target", out var targetString))
        {
            SendWithPrefix($"Seek target not specified.");
            return;
        }

        var playbackState = _getPlaybackState();
        if (playbackState == null || playbackState.Item is not FullTrack currentTrack)
        {
            SendWithPrefix($"No track is currently playing to seek within.");
            return;
        }

        long currentPositionMs = playbackState.ProgressMs;
        long trackDurationMs = currentTrack.DurationMs;
        long newPositionMs = currentPositionMs;
        var seconds = 0;

        switch (targetString.ToLower())
        {
            case "forward":
                if (message.TryGetArgument("value", out var forwardSeconds) && int.TryParse(forwardSeconds, out seconds))
                {
                    newPositionMs = currentPositionMs + (seconds * 1000);
                }
                else
                {
                    SendWithPrefix($"Seeking forward by 10 seconds.");
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
                    SendWithPrefix($"Seeking backward by 10 seconds.");
                }
                break;
            case "to_time":
                if (message.TryGetArgument("value", out var timeInSeconds) && int.TryParse(timeInSeconds, out seconds))
                {
                    newPositionMs = seconds * 1000;
                }
                else
                {
                    SendWithPrefix($"Please specify a time in seconds to seek to.");
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
                    SendWithPrefix($"Please specify a percentage to seek to.");
                    return;
                }
                break;
            case "middle":
                newPositionMs = trackDurationMs / 2;
                break;
            default:
                SendWithPrefix($"Invalid seek target. Please use 'forward', 'backward', 'to_time', 'to_percent', or 'middle'.");
                return;
        }

        newPositionMs = Math.Max(0, Math.Min(newPositionMs, trackDurationMs));

        await _spotifyManager.SeekPlayback((int)newPositionMs).ConfigureAwait(false);
        SendWithPrefix($"Playback position updated to {StringUtils.FormatMillisecondsToMinutesSeconds((int)newPositionMs)}.");
    }

    private async Task HandleRepeatMode(ServerActionMessage message)
    {
        if (!message.TryGetArgument("mode", out var repeatMode))
            repeatMode = "repeat-track";
        repeatMode = StringUtils.CleanString(repeatMode);
        await _spotifyManager.SetRepeatMode(repeatMode).ConfigureAwait(false);
        SendWithPrefix($"As requested {{{{ char }}}} set the repeat mode to: {repeatMode}");
    }

    private async Task HandleShuffleMode(ServerActionMessage message)
    {
        if (!message.TryGetArgument("mode", out var shuffleMode))
            shuffleMode = "off";
        shuffleMode = StringUtils.CleanString(shuffleMode);
        var shuffleModeBool = shuffleMode == "off" ? false : true;
        await _spotifyManager.SetShuffle(shuffleModeBool).ConfigureAwait(false);
        SendWithPrefix($"As requested {{{{ char }}}} set the shuffle mode to: {shuffleModeBool}");
    }

    private async Task HandleAddToFavorites(ServerActionMessage message)
    {
        var (trackUri, trackFriendlyName) = GetCurrentTrackInfo();
        if (string.IsNullOrEmpty(trackUri))
        {
            SendWithPrefix("No track is currently playing.");
            return;
        }

        var trackId = StringUtils.ExtractIdFromUri(trackUri);
        if (string.IsNullOrEmpty(trackId))
        {
            SendWithPrefix("Could not extract track ID from current track.");
            return;
        }

        try
        {
            await _spotifyManager.AddTrackToLibraryAsync(trackId, trackFriendlyName).ConfigureAwait(false);

            SendWithPrefix($"Track '{trackFriendlyName}' added to your Favorites.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to add track to favorites: {ex.Message}");
            SendWithPrefix("Failed to add track to Favorites.");
        }
    }

    private async Task HandleGetPlaylists()
    {
        _playlistMap = await _spotifyManager.ListAvailablePlaylists().ConfigureAwait(false);
        if (_playlistMap.Any())
        {
            var playlistList = string.Join(", ", _playlistMap.Keys);
            SendWithPrefix($"Available playlists: {playlistList}");
        }
        else
        {
            SendWithPrefix($"No playlists available.");
        }
    }

    private async Task HandleAddToPlaylist(ServerActionMessage message)
    {
        if (!message.TryGetArgument("playlist", out var playlistFriendlyName) || string.IsNullOrEmpty(playlistFriendlyName))
        {
            SendWithPrefix($"No playlist specified.");
            return;
        }

        _playlistMap = await _spotifyManager.ListAvailablePlaylists().ConfigureAwait(false);

        playlistFriendlyName = StringUtils.CleanString(playlistFriendlyName);

        var matchedPlaylist = _playlistMap.FirstOrDefault(p =>
            p.Key.Equals(playlistFriendlyName, StringComparison.OrdinalIgnoreCase));

        if (matchedPlaylist.Key == null)
        {
            SendWithPrefix($"Playlist not found: {playlistFriendlyName}");
            return;
        }

        var playlistId = StringUtils.ExtractIdFromUri(matchedPlaylist.Value);

        var (trackUri, trackFriendlyName) = GetCurrentTrackInfo();
        if (string.IsNullOrEmpty(trackUri))
        {
            SendWithPrefix($"No track is currently playing.");
            return;
        }

        var request = new PlaylistAddItemsRequest(new List<string> { trackUri });
        await _spotifyManager.AddItems(playlistId, request).ConfigureAwait(false);

        SendWithPrefix($"Track '{trackFriendlyName}' added to playlist '{playlistFriendlyName}'.");
    }

    private async Task HandleListDevices()
    {
        var deviceMap = await _spotifyManager.ListAvailableDevices().ConfigureAwait(false);
        if (deviceMap.Any())
        {
            var deviceList = string.Join(", ", deviceMap.Keys);
            SendWithPrefix($"Available devices: {deviceList}");
        }
        else
        {
            SendWithPrefix($"No devices available.");
        }
    }

    private async Task HandleTransferToDevice(ServerActionMessage message)
    {
        if (!message.TryGetArgument("device", out var partialName) || string.IsNullOrEmpty(partialName))
        {
            SendWithPrefix($"No device specified.");
            return;
        }

        _deviceMap = await _spotifyManager.ListAvailableDevices().ConfigureAwait(false);

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
                SendWithPrefix($"Did you mean: {string.Join(", ", possibleMatches)}?");
            }
            else
            {
                SendWithPrefix($"No device found matching: {partialName}");
            }
            return;
        }
        await _spotifyManager.TransferPlayback(matchedDevice.Value).ConfigureAwait(false);
        SendWithPrefix($"Playback transferred to: {matchedDevice.Key}");
    }
    private void SendWithPrefix(string message)
    {
        string prefix = _enableCharacterReplies ? "/event" : "/note";
        _sendMessage($"{prefix} {message}");
    }
    private (string? Uri, string FriendlyName) GetCurrentTrackInfo()
    {
        var playbackState = _getPlaybackState();
        if (playbackState?.Item is FullTrack track)
        {
            var trackUri = track.Uri;
            var friendlyName = $"{track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))}";
            return (trackUri, friendlyName);
        }

        return (null, "Unknown Track");
    }
}
