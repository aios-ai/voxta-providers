namespace Voxta.Modules.Aios.Spotify.Clients.Models;

public class SpotifyAuthToken
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime ExpiresAt { get; init; }
}