namespace Voxta.Modules.Aios.Spotify.Configuration;

public static class SpotifyAuthPath
{
    public static string GetUserTokenPath(string configuredTokenPath, Guid userId)
    {
        var tokenPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredTokenPath));
        if (!tokenPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("TokenPath must end with .json");

        return tokenPath[..^5] + $".{userId}.json";
    }
}
