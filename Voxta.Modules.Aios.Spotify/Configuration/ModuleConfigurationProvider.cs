using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;

namespace Voxta.Modules.Aios.Spotify.Configuration;

public class ModuleConfigurationProvider : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public static string[] FieldsRequiringReload => [ClientId.Name, ClientSecret.Name];

    public static readonly FormTextField ClientId = new()
    {
        Name = "ClientId",
        Label = "Client ID",
        Required = true,
        Text = "Get your Client ID from your Spotify Developer Dashboard at <a href=\"https://developer.spotify.com/dashboard/\" target=\"_blank\" rel=\"external\">developer.spotify.com</a>.",
    };
    
    public static readonly FormPasswordField ClientSecret = new()
    {
        Name = "ClientSecret",
        Label = "Client Secret",
        Required = true,
        Text = "Get your Client Secret from your Spotify Developer Dashboard at <a href=\"https://developer.spotify.com/dashboard/\" target=\"_blank\" rel=\"external\">developer.spotify.com</a>.",
    };
    
    public static readonly FormTextField RedirectUri = new()
    {
        Name = "RedirectUri",
        Label = "Redirect URI",
        Required = true,
        Text = "Set your Redirect URI in your Spotify Developer Dashboard at <a href=\"https://developer.spotify.com/dashboard/\" target=\"_blank\" rel=\"external\">developer.spotify.com</a>. It must match exactly.",
        DefaultValue = "http://127.0.0.1:5384/api/extensions/spotify/oauth2/callback"
    };
    
    public static readonly FormBooleanField EnableMatchFilter = new()
    {
        Name = "EnableMatchFilter",
        Label = "Enable Match Filter",
        Text = "Enable match filter to only activate the augmentation when a specific wake word is detected.",
        DefaultValue = true
    };
    
    public static readonly FormTextField MatchFilterWakeWord = new()
    {
        Name = "MatchFilterWakeWord",
        Label = "Match Filter Wake Word",
        Required = false,
        Text = "The wake word to activate the augmentation when match filter is enabled.",
        DefaultValue = ""
    };
    
    public static readonly FormBooleanField EnableCharacterReplies = new()
    {
        Name = "EnableCharacterReplies",
        Label = "Enable Character Replies",
        Text = "Enable character replies to allow the augmentation to respond as the character.",
        DefaultValue = false
    };
    
    public static readonly FormTextField ReleaseRadarPlaylistId = new()
    {
        Name = "ReleaseRadarPlaylistId",
        Label = "Release Radar Playlist ID",
        Required = false,
        Text = "Paste your Spotify Release Radar playlist ID here.",
        DefaultValue = ""
    };

    public static readonly FormTextField DiscoverWeeklyPlaylistId = new()
    {
        Name = "DiscoverWeeklyPlaylistId",
        Label = "Discover Weekly Playlist ID",
        Required = false,
        Text = "Paste your Spotify Discover Weekly playlist ID here.",
        DefaultValue = ""
    };

    public static readonly FormTextField DailyMix1PlaylistId = new()
    {
        Name = "DailyMix1PlaylistId",
        Label = "Daily Mix 1 Playlist ID",
        Required = false,
        Text = "Paste your Spotify Daily Mix 1 playlist ID here.",
        DefaultValue = ""
    };
    
    public static readonly FormTextField DailyMix2PlaylistId = new()
    {
        Name = "DailyMix2PlaylistId",
        Label = "Daily Mix 2 Playlist ID",
        Required = false,
        Text = "Paste your Spotify Daily Mix 2 playlist ID here.",
        DefaultValue = ""
    };
    
    public static readonly FormTextField DailyMix3PlaylistId = new()
    {
        Name = "DailyMix3PlaylistId",
        Label = "Daily Mix 3 Playlist ID",
        Required = false,
        Text = "Paste your Spotify Daily Mix 3 playlist ID here.",
        DefaultValue = ""
    };
    
    public static readonly FormTextField DailyMix4PlaylistId = new()
    {
        Name = "DailyMix4PlaylistId",
        Label = "Daily Mix 4 Playlist ID",
        Required = false,
        Text = "Paste your Spotify Daily Mix 4 playlist ID here.",
        DefaultValue = ""
    };
    
    public static readonly FormTextField DailyMix5PlaylistId = new()
    {
        Name = "DailyMix5PlaylistId",
        Label = "Daily Mix 5 Playlist ID",
        Required = false,
        Text = "Paste your Spotify Daily Mix 5 playlist ID here.",
        DefaultValue = ""
    };
    
    public static readonly FormTextField DailyMix6PlaylistId = new()
    {
        Name = "DailyMix6PlaylistId",
        Label = "Daily Mix 6 Playlist ID",
        Required = false,
        Text = "Paste your Spotify Daily Mix 6 playlist ID here.",
        DefaultValue = ""
    };
    
    public static readonly FormTextField TokenPath = new()
    {
        Name = "TokenPath",
        Label = "Token Path",
        Required = true,
        Text = "The path to store the Spotify authentication token.",
        DefaultValue = @"%LOCALAPPDATA%\Voxta\Aios.Spotify\Voxta.Modules.Aios.Spotify.Auth.json",
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
            EnableMatchFilter,
            MatchFilterWakeWord,
            EnableCharacterReplies,
            ReleaseRadarPlaylistId,
            DiscoverWeeklyPlaylistId,
            DailyMix1PlaylistId,
            DailyMix2PlaylistId,
            DailyMix3PlaylistId,
            DailyMix4PlaylistId,
            DailyMix5PlaylistId,
            DailyMix6PlaylistId,
            TokenPath
        );
        return Task.FromResult(fields);
    }
}
