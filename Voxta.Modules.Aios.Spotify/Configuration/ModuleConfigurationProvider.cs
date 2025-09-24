using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;

namespace Voxta.Modules.Aios.Spotify.Configuration;

public class ModuleConfigurationProvider : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public ModuleConfigurationProvider()
    {
    }

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
            SpecialPlaylists,
            TokenPath
        );
        return Task.FromResult(fields);
    }
}
