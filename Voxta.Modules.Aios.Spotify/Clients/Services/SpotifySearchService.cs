using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using Voxta.Modules.Aios.Spotify.Helpers;

namespace Voxta.Modules.Aios.Spotify.Clients.Services;

public class SpotifySearchService(ISpotifyManager spotifyManager, ILogger<SpotifySearchService> logger)
{
    private string? _currentUserId;
    private string? _userMarket;
    private readonly Queue<string> _recentlyPlayedUris = new();

    public async Task InitializeAsync()
    {
        _currentUserId = await spotifyManager.GetSpotifyUserIdAsync();
        _userMarket = await spotifyManager.GetUserMarketAsync();
    }

    public async Task<(string? Uri, string? FriendlyName, string? Type)> GetSpotifyUri(string nameString, string? requestedType = null, string? originalType = null)
    {
        nameString = StringUtils.CleanString(nameString);
        logger.LogInformation("Searching for: {NameString}", nameString);

        //var audiobookMarkets = new HashSet<string> { "US", "GB", "CA", "IE", "NZ", "AU" }; # Needed for audiobooks

        var searchTasks = new List<Task<SearchResponse?>> {
        spotifyManager.SearchSpotify(nameString, SearchRequest.Types.Track, _userMarket),
        spotifyManager.SearchSpotify(nameString, SearchRequest.Types.Album, _userMarket),
        spotifyManager.SearchSpotify(nameString, SearchRequest.Types.Artist, _userMarket),
        spotifyManager.SearchSpotify(nameString, SearchRequest.Types.Playlist, _userMarket),
        spotifyManager.SearchSpotify(nameString, SearchRequest.Types.Show, _userMarket),
        spotifyManager.SearchSpotify(nameString, SearchRequest.Types.Episode, _userMarket),
        //_spotifyManager.SearchSpotify(nameString, SearchRequest.Types.Audiobooks, _userMarket) # not working yet
        };

        var searchResponses = await Task.WhenAll(searchTasks);
        var candidates = new List<(string Uri, string FriendlyName, string Type, int Popularity, int Priority, bool IsOfficial)>();

        var extractors = new Dictionary<int, Action<SearchResponse?>>
        {
            [0] = response =>
            {
                foreach (var track in response?.Tracks?.Items ?? Enumerable.Empty<FullTrack>())
                {
                    if (track?.Uri == null) continue;

                    if (!string.IsNullOrEmpty(_userMarket) &&
                        track.AvailableMarkets != null &&
                        !track.AvailableMarkets.Contains(_userMarket))
                    {
                        logger.LogInformation("Skipping Track '{TrackName}' — not available in {UserMarket}", track.Name, _userMarket);
                        continue;
                    }

                    var artistNames = track.Artists?.Where(a => a != null).Select(a => a.Name ?? "Unknown Artist") ?? [];
                    var albumName = track.Album?.Name ?? "Unknown Album";
                    candidates.Add((track.Uri, $"Track: {track.Name ?? "Unknown Track"} by {string.Join(", ", artistNames)} (Album: {albumName})", "track", track.Popularity, 2, false));
                }
            },
            [1] = response =>
            {
                foreach (var album in response?.Albums?.Items ?? Enumerable.Empty<SimpleAlbum>())
                {
                    if (album?.Uri == null) continue;

                    if (!string.IsNullOrEmpty(_userMarket) &&
                        album.AvailableMarkets != null &&
                        !album.AvailableMarkets.Contains(_userMarket))
                    {
                        logger.LogInformation("Skipping Album '{AlbumName}' — not available in {UserMarket}", album.Name, _userMarket);
                        continue;
                    }

                    var artistNames = album.Artists?.Where(a => a != null).Select(a => a.Name ?? "Unknown Artist") ?? [];
                    var recencyBoost = 0;
                    if (DateTime.TryParse(album.ReleaseDate, out var releaseDate))
                    {
                        recencyBoost = (int)Math.Round(CalculateRecencyBoost(releaseDate));
                    }

                    candidates.Add((album.Uri, $"Album: {album.Name ?? "Unknown Album"} by {string.Join(", ", artistNames)}", "album", recencyBoost, 1, false));
                }
            },
            [2] = response =>
            {
                foreach (var artist in response?.Artists?.Items ?? Enumerable.Empty<FullArtist>())
                {
                    if (artist?.Uri == null) continue;
                    candidates.Add((artist.Uri, $"Artist: {artist.Name ?? "Unknown Artist"}", "artist", artist.Popularity, 0, false));
                }
            },
            [3] = response =>
            {
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

                    candidates.Add((playlist.Uri, $"Playlist: {playlist.Name ?? "Unknown Playlist"} by {ownerName}", "playlist", popularityBoost, 1, isOfficialSpotify));
                }
            },
            [4] = response =>
            {
                foreach (var show in response?.Shows?.Items ?? Enumerable.Empty<SimpleShow>())
                {
                    if (show?.Uri == null) continue;

                    if (!string.IsNullOrEmpty(_userMarket) &&
                        show.AvailableMarkets != null &&
                        !show.AvailableMarkets.Contains(_userMarket))
                    {
                        logger.LogInformation("Skipping Show '{ShowName}' — not available in {UserMarket}", show.Name, _userMarket);
                        continue;
                    }

                    candidates.Add((show.Uri, $"Show: {show.Name ?? "Unknown Show"} by {show.Publisher ?? "Unknown Publisher"}", "show", 0, 1, false));
                }
            },
            [5] = response =>
            {
                foreach (var episode in response?.Episodes?.Items ?? Enumerable.Empty<SimpleEpisode>())
                {
                    if (episode?.Uri == null) continue;
                    candidates.Add((episode.Uri, $"Episode: {episode.Name ?? "Unknown Episode"}", "episode", 0, 2, false));
                }
            },
            /*[6] = response =>
            {
                foreach (var audiobook in response?.Audiobooks?.Items ?? Enumerable.Empty<FullAudiobook>())
                {
                    if (audiobook?.Uri == null) continue;

                    if (!string.IsNullOrEmpty(_userMarket) &&
                        audiobook.AvailableMarkets != null &&
                        !audiobook.AvailableMarkets.Contains(_userMarket))
                    {
                        _logger.LogInformation($"Skipping Audiobook '{audiobook.Name}' — not available in {_userMarket}");
                        continue;
                    }

                    var authorNames = audiobook.Authors?.Where(a => a != null).Select(a => a.Name ?? "Unknown Author") ?? Enumerable.Empty<string>();
                    candidates.Add((audiobook.Uri, $"Audiobook: {audiobook.Name ?? "Unknown Audiobook"} by {string.Join(", ", authorNames)}", "audiobook", audiobook.Popularity, 1));
                }
            }*/
        };

        for (var i = 0; i < searchResponses.Length; i++)
        {
            if (extractors.TryGetValue(i, out var extractor))
                extractor(searchResponses[i]);
        }

        if (!string.IsNullOrEmpty(requestedType))
            candidates = candidates.Where(c => c.Type.Equals(requestedType, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!candidates.Any())
        {
            logger.LogWarning("Search found no results.");
            return (null, null, null);
        }

        if (requestedType != null)
        {
            candidates = candidates.Where(c => c.Type == requestedType).ToList();
        }

        logger.LogInformation("DEBUG: All candidates and their priorities: {Join}", string.Join("; ", candidates.Select(c => $"{c.FriendlyName} (Type: {c.Type}, Priority: {c.Priority}, Popularity: {c.Popularity})")));

        var orderedCandidates = candidates
            .OrderByDescending(c => c.Type == "playlist" && c.IsOfficial)
            .ThenByDescending(c => CalculateWordMatchScore(nameString, c.FriendlyName))
            .ThenByDescending(c => c.Priority)
            .ThenByDescending(c => c.Popularity)
            .ToList();

        if (!orderedCandidates.Any())
        {
            logger.LogWarning("Found no suitable candidate.");
            return (null, null, null);
        }

        var topCandidate = orderedCandidates.First();
        var topScore = CalculateWordMatchScore(nameString, topCandidate.FriendlyName);
        var topPriority = topCandidate.Priority;
        var topPopularity = topCandidate.Popularity;

        var tiedCandidates = orderedCandidates
            .Where(c => CalculateWordMatchScore(nameString, c.FriendlyName) == topScore
                     && c.Priority == topPriority
                     && c.Popularity == topPopularity)
            .ToList();

        var filteredTies = tiedCandidates
            .Where(c => !_recentlyPlayedUris.Contains(c.Uri))
            .ToList();

        if (filteredTies.Any())
            tiedCandidates = filteredTies;

        var bestCandidate = tiedCandidates[new Random().Next(tiedCandidates.Count)];

        logger.LogInformation("DEBUG: Best candidate selected: {BestCandidateFriendlyName} (Type: {BestCandidateType}, Priority: {BestCandidatePriority}, Popularity: {BestCandidatePopularity}, Score: {TopScore})", bestCandidate.FriendlyName, bestCandidate.Type, bestCandidate.Priority, bestCandidate.Popularity, topScore);

        return (bestCandidate.Uri, bestCandidate.FriendlyName, bestCandidate.Type);

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
}