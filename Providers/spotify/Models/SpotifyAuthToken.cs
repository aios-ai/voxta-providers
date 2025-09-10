using System;

namespace Voxta.SampleProviderApp.Providers.Spotify.Models
{
    public class SpotifyAuthToken
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}