using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public class TileFetcher
{
    private const string OpenStreetMapUserAgent =
        "Voxta-Aios-OpenWeather/1.0 (+https://github.com/aios-ai/voxta-providers)";

    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> InFlight = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly TimeSpan MinimumOsmCacheDuration = TimeSpan.FromDays(180);
    private static readonly TimeSpan LegacyDynamicCacheMaxAge = TimeSpan.FromDays(1);
    public static readonly TimeSpan WeatherCacheDuration = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly string _osmCacheDir;
    private readonly string _weatherCacheDir;
    private readonly string _compositeCacheDir;

    public TileFetcher(HttpClient client, string cacheDir)
    {
        _httpClient = client;
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(OpenStreetMapUserAgent);

        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDir);

        _osmCacheDir = Path.Combine(cacheDir, "osm");
        _weatherCacheDir = Path.Combine(cacheDir, "openweather");
        _compositeCacheDir = Path.Combine(cacheDir, "composites");

        Directory.CreateDirectory(_osmCacheDir);
        Directory.CreateDirectory(_weatherCacheDir);
        Directory.CreateDirectory(_compositeCacheDir);

        CleanupExpiredDynamicCaches();
    }

    public async Task<Image<Rgba32>> GetOsmTileAsync(int z, int x, int y, CancellationToken ct)
    {
        var cacheKey = $"osm:{z}:{x}:{y}";
        var cachePath = Path.Combine(_osmCacheDir, $"{z}_{x}_{y}.png");
        var url = $"https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        var bytes = await GetCachedBytesAsync(cacheKey, cachePath, url, "OpenStreetMap", MinimumOsmCacheDuration, ct);

        return Image.Load<Rgba32>(bytes);
    }

    public async Task<Image<Rgba32>> GetWeatherTileAsync(
        string apiKey, string layer, int z, int x, int y, CancellationToken ct)
    {
        var safeLayer = SanitizeCacheKeyPart(layer);
        var cacheKey = $"openweather:{safeLayer}:{z}:{x}:{y}";
        var cachePath = Path.Combine(_weatherCacheDir, $"{safeLayer}_{z}_{x}_{y}.png");
        var url = $"https://tile.openweathermap.org/map/{layer}/{z}/{x}/{y}.png?appid={apiKey}";
        var bytes = await GetCachedBytesAsync(cacheKey, cachePath, url, "OpenWeather", WeatherCacheDuration, ct);

        return Image.Load<Rgba32>(bytes);
    }

    public async Task<byte[]?> TryGetCompositeMapAsync(string cacheKey, CancellationToken ct)
    {
        var cachePath = GetCompositeCachePath(cacheKey);
        var metadata = await ReadMetadataAsync(GetMetadataPath(cachePath), ct);

        if (!File.Exists(cachePath) || !IsFresh(metadata))
        {
            TryDeleteCachePair(cachePath);
            return null;
        }

        return await File.ReadAllBytesAsync(cachePath, ct);
    }

    public async Task SaveCompositeMapAsync(string cacheKey, byte[] bytes, CancellationToken ct)
    {
        var cachePath = GetCompositeCachePath(cacheKey);
        await WriteAllBytesAtomicAsync(cachePath, bytes, ct);
        await WriteMetadataAsync(GetMetadataPath(cachePath), new CacheMetadata
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(WeatherCacheDuration)
        }, ct);
    }

    private async Task<byte[]> GetCachedBytesAsync(
        string cacheKey,
        string cachePath,
        string url,
        string provider,
        TimeSpan fallbackCacheDuration,
        CancellationToken ct)
    {
        var metadataPath = GetMetadataPath(cachePath);
        var metadata = await ReadMetadataAsync(metadataPath, ct);

        if (File.Exists(cachePath) && IsFresh(metadata))
            return await File.ReadAllBytesAsync(cachePath, ct);

        var lazy = InFlight.GetOrAdd(cacheKey, _ => new Lazy<Task<byte[]>>(
            () => DownloadAndCacheAsync(cachePath, metadataPath, metadata, url, provider, fallbackCacheDuration, ct)));

        try
        {
            return await lazy.Value;
        }
        finally
        {
            InFlight.TryRemove(cacheKey, out _);
        }
    }

    private async Task<byte[]> DownloadAndCacheAsync(
        string cachePath,
        string metadataPath,
        CacheMetadata? metadata,
        string url,
        string provider,
        TimeSpan fallbackCacheDuration,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrWhiteSpace(metadata?.ETag))
            request.Headers.TryAddWithoutValidation("If-None-Match", metadata.ETag);

        if (metadata?.LastModifiedUtc is { } lastModified)
            request.Headers.IfModifiedSince = lastModified;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotModified && File.Exists(cachePath))
        {
            await WriteMetadataAsync(metadataPath, BuildMetadata(response, fallbackCacheDuration, metadata), ct);
            return await File.ReadAllBytesAsync(cachePath, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (File.Exists(cachePath) && response.StatusCode != HttpStatusCode.Forbidden)
                return await File.ReadAllBytesAsync(cachePath, ct);

            throw new TileFetchException(provider, url, response.StatusCode, body);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        await WriteAllBytesAtomicAsync(cachePath, bytes, ct);
        await WriteMetadataAsync(metadataPath, BuildMetadata(response, fallbackCacheDuration, metadata), ct);

        return bytes;
    }

    private static CacheMetadata BuildMetadata(
        HttpResponseMessage response,
        TimeSpan fallbackCacheDuration,
        CacheMetadata? previous)
    {
        var now = DateTimeOffset.UtcNow;
        var cacheControl = response.Headers.CacheControl;
        var expires = response.Content.Headers.Expires;

        var headerExpiresUtc = cacheControl?.MaxAge is { } maxAge
            ? now.Add(maxAge)
            : expires ?? now.Add(fallbackCacheDuration);
        var minimumExpiresUtc = now.Add(fallbackCacheDuration);
        var expiresUtc = headerExpiresUtc > minimumExpiresUtc
            ? headerExpiresUtc
            : minimumExpiresUtc;

        return new CacheMetadata
        {
            CreatedUtc = previous?.CreatedUtc ?? now,
            ExpiresUtc = expiresUtc,
            ETag = response.Headers.ETag?.ToString() ?? previous?.ETag,
            LastModifiedUtc = response.Content.Headers.LastModified ?? previous?.LastModifiedUtc
        };
    }

    private void CleanupExpiredDynamicCaches()
    {
        CleanupExpiredCacheDirectory(_weatherCacheDir);
        CleanupExpiredCacheDirectory(_compositeCacheDir);
    }

    private static void CleanupExpiredCacheDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var cachePath in Directory.EnumerateFiles(directory, "*.png"))
        {
            var metadataPath = GetMetadataPath(cachePath);
            var metadata = ReadMetadata(metadataPath);
            var fileInfo = new FileInfo(cachePath);

            var shouldDelete = metadata?.ExpiresUtc is { } expiresUtc
                ? expiresUtc <= DateTimeOffset.UtcNow
                : fileInfo.LastWriteTimeUtc <= DateTime.UtcNow.Subtract(LegacyDynamicCacheMaxAge);

            if (shouldDelete)
                TryDeleteCachePair(cachePath);
        }
    }

    private static bool IsFresh(CacheMetadata? metadata)
    {
        return metadata?.ExpiresUtc is { } expires && expires > DateTimeOffset.UtcNow;
    }

    private static async Task<CacheMetadata?> ReadMetadataAsync(string metadataPath, CancellationToken ct)
    {
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            await using var stream = File.OpenRead(metadataPath);
            return await JsonSerializer.DeserializeAsync<CacheMetadata>(stream, JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    private static CacheMetadata? ReadMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            using var stream = File.OpenRead(metadataPath);
            return JsonSerializer.Deserialize<CacheMetadata>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteMetadataAsync(string metadataPath, CacheMetadata metadata, CancellationToken ct)
    {
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, ct);
    }

    private static async Task WriteAllBytesAtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tempPath, bytes, ct);
        File.Move(tempPath, path, overwrite: true);
    }

    private string GetCompositeCachePath(string cacheKey)
    {
        return Path.Combine(_compositeCacheDir, $"{SanitizeCacheKeyPart(cacheKey)}.png");
    }

    private static string GetMetadataPath(string cachePath)
    {
        return $"{cachePath}.json";
    }

    private static void TryDeleteCachePair(string cachePath)
    {
        TryDeleteFile(cachePath);
        TryDeleteFile(GetMetadataPath(cachePath));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cache cleanup is opportunistic; stale files can be retried later.
        }
    }

    private static string SanitizeCacheKeyPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) || ch is ':' or '/' or '\\' or '?' or '&' or '='
            ? '_'
            : ch));
    }

    private sealed class CacheMetadata
    {
        public DateTimeOffset CreatedUtc { get; init; }
        public DateTimeOffset? ExpiresUtc { get; init; }
        public string? ETag { get; init; }
        public DateTimeOffset? LastModifiedUtc { get; init; }
    }
}

public sealed class TileFetchException(
    string provider,
    string url,
    HttpStatusCode statusCode,
    string responseBody
) : Exception($"{provider} tile request failed with HTTP {(int)statusCode} {statusCode}: {responseBody}")
{
    public string Provider { get; } = provider;
    public string Url { get; } = url;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;
}
