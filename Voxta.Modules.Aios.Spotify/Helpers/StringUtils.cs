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
    
    public static string NormaliseKey(string input)
    {
        return CleanString(input).ToLowerInvariant();
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
            return "daily mix";
        }

        return input;
    }
    
    /*public static string? FindBestMatch(string input, IEnumerable<string> candidates)
    {
        string? best = null;
        int bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            int distance = LevenshteinDistance(input, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        
        return bestDistance <= 3 ? best : null;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s))
            return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t))
            return s.Length;

        int n = s.Length;
        int m = t.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }

        return d[n, m];
    }*/
}