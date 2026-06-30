using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;
using Voxta.Modules.Aios.Spotify.ChatAugmentations;
using System.Text.Json;

namespace Voxta.Modules.Aios.Spotify.Configuration;

public class ModuleConfigurationProvider : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public static string[] FieldsRequiringReload => [ClientId.Name, ClientSecret.Name, Actions.Name];
    private static readonly string DefaultTokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voxta",
        "Aios.Spotify",
        "Voxta.Modules.Aios.Spotify.Auth.json");

    public static readonly FormTextField ClientId = new()
    {
        Name = "ClientId",
        Label = "Client ID",
        Required = true,
        //language=markdown
        Text = "Get your Client ID from your Spotify Developer Dashboard at [developer.spotify.com](https://developer.spotify.com/dashboard/).",
    };

    public static readonly FormPasswordField ClientSecret = new()
    {
        Name = "ClientSecret",
        Label = "Client Secret",
        Required = true,
        //language=markdown
        Text = "Get your Client Secret from your Spotify Developer Dashboard at [developer.spotify.com](https://developer.spotify.com/dashboard/).",
    };

    public static readonly FormTextField RedirectUri = new()
    {
        Name = "RedirectUri",
        Label = "Redirect URI",
        Required = true,
        //language=markdown
        Text = "Set your Redirect URI in your Spotify Developer Dashboard at [developer.spotify.com](https://developer.spotify.com/dashboard/).",
        DefaultValue = "http://127.0.0.1:5384/api/extensions/spotify/oauth2/callback"
    };

    public static readonly FormTextField MatchFilterWakeWord = new()
    {
        Name = "MatchFilterWakeWord",
        Label = "Wake Word",
        Required = false,
        Text = "Optional wake word required before Spotify commands. Leave empty to match Spotify commands directly.",
        DefaultValue = ""
    };

    public static readonly FormIntSliderField SpeechDuckingVolumePercent = new()
    {
        Name = "SpeechDuckingVolumePercent",
        Label = "Speech Ducking Volume",
        Text = "Spotify volume while speech or transcription is active. Set to 100 to disable volume ducking.",
        Min = 0,
        Max = 100,
        SoftMin = 0,
        SoftMax = 100,
        Step = 1,
        DefaultValue = 60
    };

    public static readonly FormMultilineField SpecialPlaylists = new()
    {
        Name = "SpecialPlaylists",
        Label = "Special Spotify Playlists",
        Required = false,
        Text = "You can map your algorithmic Spotify playlists here. Add one entry per line in the format: Name=PlaylistId (e.g. Release Radar=123abc).",
        Rows = 8,
        DefaultValue =
            @"Release Radar=
Discover Weekly=
Daily Mix 1=
Daily Mix 2=
Daily Mix 3=
Daily Mix 4=
Daily Mix 5=
Daily Mix 6="
    };

    public static readonly SpotifyActionSettings[] DefaultActions =
    [
        new()
        {
            Name = "toggle_playback",
            Layer = "SpotifyControl",
            ShortDescription = "play or pause music",
            Description = "When {{ user }} asks to toggle music playback (play, pause, stop, resume, etc.).",
            MatchFilter = @"\b(?:play|pause|stop|resume|continue|toggle)\b(?:\s+(?:music|playback|spotify|song|track))?|\b(?:music|playback|spotify)\b.*\b(?:play|pause|stop|resume|continue)\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "spotify_connect",
            Layer = "SpotifyControl",
            ShortDescription = "anything regarding spotify",
            Description = "When {{ user }} asks to interact with spotify in any way, like playing music, search for an artist, etc.",
            MatchFilter = @"\b(?:spotify|music|playback|songs?|tracks?|albums?|artists?|playlists?|shows?|episodes?|genre|volume)\b|\b(?:play|pause|stop|resume|continue|loud(?:er)?|quiet(?:er)?|increase|decrease|reduce)\b",
            FlagsFilter = "spotify_disconnected",
            CancelReply = "true",
        },
        new()
        {
            Name = "play_random_music",
            Layer = "SpotifyControl",
            ShortDescription = "play random music",
            Description = "When {{ user }} asks to play music without mentioning the artist or song.",
            MatchFilter = @"\b(?:surprise|random|select|choose|pick)\b(?:\s+(?:music|song|track|playlist))?|\b(?:play|put on)\b.*\b(?:anything|something|random)\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "play_special_playlist",
            Layer = "SpotifyControl",
            ShortDescription = "Play one of the user's special Spotify playlists.",
            Description = "When {{ user }} asks to play a special playlist generated by Spotify for them. Available playlists: {special_playlists}.",
            MatchFilter = "{special_playlists_pattern}",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "play_music",
            Layer = "SpotifyControl",
            ShortDescription = "play requested music",
            Description = "When {{ user }} asks to play a track, album, artist, playlist, show or episode. Select all details you can find from the request.",
            MatchFilter = @"\b(?:play|put on|start)\b.*\b(?:music|song|track|album|artist|playlist|show|episode|genre)\b|\b(?:songs?|tracks?|albums?|artists?|playlists?|shows?|episodes?|genre)\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "queue_track",
            Layer = "SpotifyControl",
            ShortDescription = "Queue a track for later",
            Description = "When {{ user }} wants to add a album, artist, episode, playlist, show or track to the queue. Select all track details you can find from the request.",
            MatchFilter = @"\b(?:queue|enqueue|next up)\b|\b(?:add|put)\b.*\b(?:queue|next)\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "volume",
            Layer = "SpotifyControl",
            ShortDescription = "change volume",
            Description = "When {{ user }} asks to set the volume to a specific level, or increase/decrease it. Use 'set' for absolute volume, 'increase' or 'decrease' for relative changes.",
            MatchFilter = @"\b(?:volume|loud(?:er)?|quiet(?:er)?|increase|decrease|reduce|raise|lower)\b|\bturn\s+(?:it\s+)?(?:up|down)\b",
            FlagsFilter = "playing",
            CancelReply = "true",
        },
        new()
        {
            Name = "seek_playback",
            Layer = "SpotifyControl",
            ShortDescription = "seek through the track",
            Description = "When {{ user }} asks go to (seek) a specific position within the current track. Use 'forward' or 'backward' for relative seeks, 'to_time' for absolute time (e.g., 1 minute 30 seconds), 'to_percent' for percentage (e.g., 50%), or 'middle' for the midpoint.",
            MatchFilter = @"\b(?:seek|jump|scrub|forward|backward|rewind)\b|\bgo\s+to\b|\bskip\s+(?:ahead|back|forward|backward)\b",
            FlagsFilter = "playing",
            CancelReply = "true",
        },
        new()
        {
            Name = "skip_next",
            Layer = "SpotifyControl",
            ShortDescription = "skip to the next track",
            Description = "When {{ user }} asks to skip to next track/song/title.",
            MatchFilter = @"\b(?:next|skip)\b(?:\s+(?:song|track|title))?|\bskip\s+(?:forward|ahead)\b",
            FlagsFilter = "playing",
            CancelReply = "true",
        },
        new()
        {
            Name = "skip_previous",
            Layer = "SpotifyControl",
            ShortDescription = "skip to the previous track",
            Description = "When {{ user }} asks to skip to the previous track/song/title.",
            MatchFilter = @"\b(?:previous|prev|back)\b(?:\s+(?:song|track|title))?|\bskip\s+(?:back|backward)\b",
            FlagsFilter = "playing",
            CancelReply = "true",
        },
        new()
        {
            Name = "repeat_mode",
            Layer = "SpotifyControl",
            ShortDescription = "change the repeat mode",
            Description = "When {{ user }} asks to change the repeat mode to one of the following track, context or off",
            MatchFilter = @"\brepeat\b|\brepeat\s+(?:track|song|playlist|album|context|off)\b|\bturn\s+(?:on|off)\s+repeat\b",
            FlagsFilter = "playing",
            CancelReply = "true",
        },
        new()
        {
            Name = "shuffle_mode",
            Layer = "SpotifyControl",
            ShortDescription = "change the shuffle mode",
            Description = "When {{ user }} asks to change the shuffle mode on or off",
            MatchFilter = @"\bshuffle\b|\bturn\s+(?:on|off)\s+shuffle\b",
            FlagsFilter = "playing",
            CancelReply = "true",
        },
        new()
        {
            Name = "add_to_favorites",
            Layer = "SpotifyControl",
            ShortDescription = "Add the currently playing track to Favorites (Liked Songs)",
            Description = "When {{ user }} asks to like, favorite, love, or save the currently playing track to their library",
            MatchFilter = @"\b(?:like|love|save|favorite|favourite)\b(?:\s+(?:this|track|song))?|\badd\s+(?:this\s+)?(?:song|track)?\s*to\s+(?:favorites?|favourites?|library|liked\s+songs)\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "get_playlists",
            Layer = "SpotifyControl",
            ShortDescription = "list all available playlists",
            Description = "When {{ user }} asks to list all available playlists",
            MatchFilter = @"\b(?:list|show|get)\b.*\b(?:playlists?|available playlists?)\b|\bavailable\s+playlists?\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "add_to_playlist",
            Layer = "SpotifyControl",
            ShortDescription = "add to playlist",
            Description = "When {{ user }} asks to add the current song to a specific playlist",
            MatchFilter = @"\badd\b.*\b(?:song|track|this)\b.*\bplaylist\b|\bsave\b.*\bplaylist\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "list_devices",
            Layer = "SpotifyControl",
            ShortDescription = "list all available devices",
            Description = "When {{ user }} asks to list all available devices",
            MatchFilter = @"\b(?:list|show|get)\b.*\b(?:devices?|available devices?)\b|\bavailable\s+devices?\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "transfer_to_device",
            Layer = "SpotifyControl",
            ShortDescription = "transfer to device",
            Description = "When {{ user }} asks to transfer playback to a specific device",
            MatchFilter = @"\b(?:transfer|move|switch)\b.*\b(?:playback|music|spotify|device)\b|\bplay\s+on\b.*\bdevice\b",
            FlagsFilter = "spotify_connected",
            CancelReply = "true",
        },
    ];

    public static readonly FormArrayOfObjectsField Actions = new()
    {
        Name = "Actions",
        Label = "Actions",
        Text = "Configure Spotify actions exposed to action inference. Match Filter accepts one keyword/regex pattern per line; the optional wake word is applied automatically. Use {special_playlists} for the configured special playlist names.",
        AllowAddRemove = false,
        AllowRename = false,
        AllowDisable = true,
        UniqueFields = [nameof(SpotifyActionSettings.Name)],
        FieldTemplate =
        [
            new FormTextField
            {
                Name = nameof(SpotifyActionSettings.Name),
                Label = "Name",
                Required = true,
            },
            new FormTextField
            {
                Name = nameof(SpotifyActionSettings.ShortDescription),
                Label = "Short Description",
                Required = true,
            },
            new FormMultilineField
            {
                Name = nameof(SpotifyActionSettings.Description),
                Label = "Description",
                Required = true,
            },
            new FormMultilineField
            {
                Name = nameof(SpotifyActionSettings.MatchFilter),
                Label = "Match Filter",
                Text = "One keyword/regex pattern per line. Use {special_playlists} for the configured special playlist names.",
                Required = true,
            },
            new FormTextField
            {
                Name = nameof(SpotifyActionSettings.FlagsFilter),
                Label = "Flags Filter",
                Required = false,
            },
            new FormBooleanField
            {
                Name = nameof(SpotifyActionSettings.CancelReply),
                Label = "Cancel Reply",
                DefaultValue = true,
            },
        ],
        DefaultValue = JsonSerializer.Serialize(DefaultActions),
    };

    public static readonly FormTextField TokenPath = new()
    {
        Name = "TokenPath",
        Label = "Token Path",
        Required = true,
        Text = "The path to store the Spotify authentication token.",
        DefaultValue = DefaultTokenPath,
        Advanced = true,
    };

    public Task<FormField[]> GetModuleConfigurationFieldsAsync(
        IAuthenticationContext auth,
        ISettingsSource settings,
        CancellationToken cancellationToken
    )
    {
        var fields = FormBuilder.Build(
            ClientId,
            ClientSecret,
            RedirectUri,
            MatchFilterWakeWord,
            SpeechDuckingVolumePercent,
            SpecialPlaylists,
            Actions,
            TokenPath
        );
        return Task.FromResult(fields);
    }
}
