using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using Voxta.Modules.Aios.Spotify.Helpers;

namespace Voxta.Modules.Aios.Spotify.Clients.Services;

public class SpotifySearchService(ISpotifyManager spotifyManager, ILogger<SpotifySearchService> logger)
{
    private string? _currentUserId;
    private readonly Queue<string> _recentlyPlayedUris = new();

    public async Task InitializeAsync()
    {
        _currentUserId = await spotifyManager.GetSpotifyUserIdAsync();
    }

    public async Task<(string? Uri, string? FriendlyName, string? Type)> GetSpotifyUri(string nameString, string? requestedType = null, string? originalType = null)
    {
        nameString = StringUtils.CleanString(nameString);
        logger.LogInformation("Searching for: {NameString}", nameString);

        var searches = BuildSearches(requestedType);
        var searchTasks = searches
            .Select(search => spotifyManager.SearchSpotify(nameString, search.SearchType, "from_token"))
            .ToList();

        SearchResponse?[] searchResponses;
        try
        {
            searchResponses = await Task.WhenAll(searchTasks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Spotify search failed.");
            return (null, null, null);
        }
        var candidates = new List<SearchCandidate>();

        var extractors = new Dictionary<string, Action<SearchResponse?>>
        {
            ["track"] = response =>
            {
                var rank = 0;
                foreach (var track in response?.Tracks?.Items ?? Enumerable.Empty<FullTrack>())
                {
                    if (track?.Uri == null) continue;

                    var artistNames = track.Artists?.Where(a => a != null).Select(a => a.Name ?? "Unknown Artist") ?? [];
                    var albumName = track.Album?.Name ?? "Unknown Album";
                    candidates.Add(new SearchCandidate(track.Uri, $"Track: {track.Name ?? "Unknown Track"} by {string.Join(", ", artistNames)} (Album: {albumName})", "track", 0, 2, false, rank++));
                }
            },
            ["album"] = response =>
            {
                var rank = 0;
                foreach (var album in response?.Albums?.Items ?? Enumerable.Empty<SimpleAlbum>())
                {
                    if (album?.Uri == null) continue;

                    var artistNames = album.Artists?.Where(a => a != null).Select(a => a.Name ?? "Unknown Artist") ?? [];
                    var recencyBoost = 0;
                    if (DateTime.TryParse(album.ReleaseDate, out var releaseDate))
                    {
                        recencyBoost = (int)Math.Round(CalculateRecencyBoost(releaseDate));
                    }

                    candidates.Add(new SearchCandidate(album.Uri, $"Album: {album.Name ?? "Unknown Album"} by {string.Join(", ", artistNames)}", "album", recencyBoost, 1, false, rank++));
                }
            },
            ["artist"] = response =>
            {
                var rank = 0;
                foreach (var artist in response?.Artists?.Items ?? Enumerable.Empty<FullArtist>())
                {
                    if (artist?.Uri == null) continue;
                    candidates.Add(new SearchCandidate(artist.Uri, $"Artist: {artist.Name ?? "Unknown Artist"}", "artist", 0, 0, false, rank++));
                }
            },
            ["playlist"] = response =>
            {
                var rank = 0;
                foreach (var playlist in response?.Playlists?.Items ?? Enumerable.Empty<FullPlaylist>())
                {
                    if (playlist?.Uri == null) continue;
                    var ownerId = playlist.Owner?.Id ?? "";
                    var isOfficialSpotify = string.Equals(ownerId, "spotify", StringComparison.OrdinalIgnoreCase);
                    var ownerName = playlist.Owner?.DisplayName ?? "Unknown Owner";
                    var isUserOwned = string.Equals(ownerId, _currentUserId, StringComparison.OrdinalIgnoreCase);

                    var popularityBoost = 0;

                    if (isOfficialSpotify)
                    {
                        popularityBoost = 1000;
                    }
                    else if (isUserOwned)
                    {
                        popularityBoost = originalType == "genre" ? -50 : 50;
                    }

                    candidates.Add(new SearchCandidate(playlist.Uri, $"Playlist: {playlist.Name ?? "Unknown Playlist"} by {ownerName}", "playlist", popularityBoost, 1, isOfficialSpotify, rank++));
                }
            },
            ["show"] = response =>
            {
                var rank = 0;
                foreach (var show in response?.Shows?.Items ?? Enumerable.Empty<SimpleShow>())
                {
                    if (show?.Uri == null) continue;

                    candidates.Add(new SearchCandidate(show.Uri, $"Show: {show.Name ?? "Unknown Show"}", "show", 0, 1, false, rank++));
                }
            },
            ["episode"] = response =>
            {
                var rank = 0;
                foreach (var episode in response?.Episodes?.Items ?? Enumerable.Empty<SimpleEpisode>())
                {
                    if (episode?.Uri == null) continue;
                    candidates.Add(new SearchCandidate(episode.Uri, $"Episode: {episode.Name ?? "Unknown Episode"}", "episode", 0, 2, false, rank++));
                }
            },
            /*["audiobook"] = response =>
            {
                var rank = 0;
                foreach (var audiobook in response?.Audiobooks?.Items ?? Enumerable.Empty<FullAudiobook>())
                {
                    if (audiobook?.Uri == null) continue;

                    var authorNames = audiobook.Authors?.Where(a => a != null).Select(a => a.Name ?? "Unknown Author") ?? Enumerable.Empty<string>();
                    candidates.Add(new SearchCandidate(audiobook.Uri, $"Audiobook: {audiobook.Name ?? "Unknown Audiobook"} by {string.Join(", ", authorNames)}", "audiobook", 0, 1, false, rank++));
                }
            }*/
        };

        for (var i = 0; i < searchResponses.Length; i++)
        {
            if (extractors.TryGetValue(searches[i].Type, out var extractor))
                extractor(searchResponses[i]);
        }

        if (!candidates.Any())
        {
            logger.LogWarning("Search found no results.");
            return (null, null, null);
        }

        logger.LogInformation("DEBUG: All candidates and their priorities: {Join}", string.Join("; ", candidates.Select(c => $"{c.FriendlyName} (Type: {c.Type}, Priority: {c.Priority}, Boost: {c.Boost}, SpotifyRank: {c.SpotifyRank})")));

        var orderedCandidates = candidates
            .OrderByDescending(c => c.Type == "playlist" && c.IsOfficial)
            .ThenByDescending(c => CalculateWordMatchScore(nameString, c.FriendlyName))
            .ThenByDescending(c => c.Priority)
            .ThenByDescending(c => c.Boost)
            .ThenBy(c => c.SpotifyRank)
            .ToList();

        if (!orderedCandidates.Any())
        {
            logger.LogWarning("Found no suitable candidate.");
            return (null, null, null);
        }

        var topCandidate = orderedCandidates.First();
        var topScore = CalculateWordMatchScore(nameString, topCandidate.FriendlyName);
        var topPriority = topCandidate.Priority;
        var topBoost = topCandidate.Boost;
        var topSpotifyRank = topCandidate.SpotifyRank;

        var tiedCandidates = orderedCandidates
            .Where(c => CalculateWordMatchScore(nameString, c.FriendlyName) == topScore
                     && c.Priority == topPriority
                     && c.Boost == topBoost
                     && c.SpotifyRank == topSpotifyRank)
            .ToList();

        var filteredTies = tiedCandidates
            .Where(c => !_recentlyPlayedUris.Contains(c.Uri))
            .ToList();

        if (filteredTies.Any())
            tiedCandidates = filteredTies;

        var bestCandidate = tiedCandidates[new Random().Next(tiedCandidates.Count)];

        logger.LogInformation("DEBUG: Best candidate selected: {BestCandidateFriendlyName} (Type: {BestCandidateType}, Priority: {BestCandidatePriority}, Boost: {BestCandidateBoost}, SpotifyRank: {BestCandidateSpotifyRank}, Score: {TopScore})", bestCandidate.FriendlyName, bestCandidate.Type, bestCandidate.Priority, bestCandidate.Boost, bestCandidate.SpotifyRank, topScore);

        AddToHistory(bestCandidate.Uri);
        return (bestCandidate.Uri, bestCandidate.FriendlyName, bestCandidate.Type);

    }

    private static List<(string Type, SearchRequest.Types SearchType)> BuildSearches(string? requestedType)
    {
        var allSearches = new List<(string Type, SearchRequest.Types SearchType)>
        {
            ("track", SearchRequest.Types.Track),
            ("album", SearchRequest.Types.Album),
            ("artist", SearchRequest.Types.Artist),
            ("playlist", SearchRequest.Types.Playlist),
            ("show", SearchRequest.Types.Show),
            ("episode", SearchRequest.Types.Episode)
        };

        if (string.IsNullOrEmpty(requestedType))
            return allSearches;

        return allSearches
            .Where(search => search.Type.Equals(requestedType, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private int CalculateWordMatchScore(string searchString, string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(searchString) || string.IsNullOrWhiteSpace(friendlyName))
            return 0;

        var searchWords = searchString.ToLower().Split([' '], StringSplitOptions.RemoveEmptyEntries);
        var friendlyNameWords = friendlyName.ToLower().Split([' '], StringSplitOptions.RemoveEmptyEntries);

        var score = 0;
        var consecutiveMatches = 0;

        for (var i = 0; i < searchWords.Length; i++)
        {
            var found = false;
            for (var j = 0; j < friendlyNameWords.Length; j++)
            {
                if (searchWords[i] == friendlyNameWords[j])
                {
                    score += 10;
                    if (i > 0 && j > 0 && searchWords[i - 1] == friendlyNameWords[j - 1])
                    {
                        consecutiveMatches++;
                        score += consecutiveMatches * 5;
                    }
                    else
                    {
                        consecutiveMatches = 0;
                    }
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                consecutiveMatches = 0;
            }
        }

        if (friendlyName.ToLower().Contains(searchString.ToLower()))
        {
            score += 50;
        }
        // Add +5 score for exact matches (needs testing)
        var nameOnly = StringUtils.CleanFriendlyNameRegex(friendlyName);
        if (nameOnly.Equals(searchString, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        logger.LogInformation("DEBUG: Match score between '{SearchString}' and '{FriendlyName}' is {Score}", searchString, friendlyName, score);
        return score;
    }

    private static double CalculateRecencyBoost(DateTime releaseDate)
    {
        var monthsOld = (DateTime.UtcNow.Year - releaseDate.Year) * 12
                        + DateTime.UtcNow.Month - releaseDate.Month;

        if (monthsOld < 0) monthsOld = 0;

        const double maxBoost = 100.0;
        const double halfLifeMonths = 12.0;

        var decayFactor = Math.Pow(0.5, monthsOld / halfLifeMonths);

        return maxBoost * decayFactor;
    }

    private void AddToHistory(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return;

        if (_recentlyPlayedUris.Count >= 100)
            _recentlyPlayedUris.Dequeue();

        _recentlyPlayedUris.Enqueue(uri);
    }

    private sealed record SearchCandidate(string Uri, string FriendlyName, string Type, int Boost, int Priority, bool IsOfficial, int SpotifyRank);
}
