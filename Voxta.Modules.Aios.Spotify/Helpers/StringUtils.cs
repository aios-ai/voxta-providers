using System.Text.RegularExpressions;

namespace Voxta.Modules.Aios.Spotify.Helpers;

public static class StringUtils
{
    public static string CleanString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        input = Regex.Replace(input, @"[^\w\s]", "");
        input = Regex.Replace(input, @"\s+", " ").Trim();

        return input;
    }

    public static string CleanFriendlyNameRegex(string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName)) return string.Empty;
        var trimmed = Regex.Replace(friendlyName, @"\s+(by|from)\s+.*$", "", RegexOptions.IgnoreCase).Trim();
        trimmed = Regex.Replace(trimmed, @"^(Playlist|Album|Artist|Track|Song|Episode|Show|Audiobook):\s*", "", RegexOptions.IgnoreCase).Trim();

        return trimmed;
    }

    public static string FormatMillisecondsToMinutesSeconds(int milliseconds)
    {
        var t = TimeSpan.FromMilliseconds(milliseconds);
        return string.Format("{0:D2}:{1:D2}", (int)t.TotalMinutes, t.Seconds);
    }

    public static string ExtractIdFromUri(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return string.Empty;

        var parts = uri.Split(':');
        return parts.Length > 0 ? parts[^1] : string.Empty;
    }

    // Map of canonical name -> Spotify playlist ID (user-specific for algorithmic ones)
    public static readonly Dictionary<string, string> SpecialPlaylistMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // "Made For You" artist/genre mixes (user-specific) -> needs to be tested
        { "stations", "PASTE_ID_HERE" },
    };

    public static string NormaliseSpecialName(string input)
    {
        input = input.Trim().ToLowerInvariant();

        if (input.Contains("radar"))
            return "release radar";

        if (input.Contains("discover") || input.Contains("weekly"))
            return "discover weekly";

        if (input.Contains("daily") || input.Contains("mix") || input.Contains("mixtape"))
        {
            var match = Regex.Match(input, @"\d+");
            if (match.Success)
                return $"daily mix {match.Value}";
            else
            {
                var rnd = new Random();
                return $"daily mix {rnd.Next(1, 7)}";
            }
        }

        return input;
    }
}