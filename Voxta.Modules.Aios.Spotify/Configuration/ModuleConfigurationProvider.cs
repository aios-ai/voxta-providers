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

    public static readonly FormBooleanField EnableCharacterReplies = new()
    {
        Name = "EnableCharacterReplies",
        Label = "Enable Character Replies",
        Text = "Enable character replies to actions performed by the module.",
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
            MatchFilterWakeWord,
            SpeechDuckingVolumePercent,
            EnableCharacterReplies,
            SpecialPlaylists,
            TokenPath
        );
        return Task.FromResult(fields);
    }
}
