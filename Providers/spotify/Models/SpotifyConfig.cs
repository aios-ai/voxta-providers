namespace Voxta.SampleProviderApp.Providers.Spotify.Models
{
    public class SpotifyConfig
    {
        public string? clientId { get; set; }
        public string? clientSecret { get; set; }
        public string? redirectUri { get; set; }
        public bool enableMatchFilter { get; set; } = false;
        public string? matchFilterWakeWord { get; set; }
        public bool enableCharacterReplies { get; set; } = false;
        public string? tokenPath { get; set; }
    }
}