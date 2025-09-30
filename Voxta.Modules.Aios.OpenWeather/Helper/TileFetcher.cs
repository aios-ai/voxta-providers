using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public class TileFetcher
{
    private readonly HttpClient httpClient;
    private readonly string cacheDir;

    public TileFetcher(HttpClient client, string appName = "Aios.OpenWeather")
    {
        httpClient = client;
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MyWeatherApp/1.0 (contact: aios@example.com)");

        cacheDir = GetDefaultCacheDir(appName);
        Directory.CreateDirectory(cacheDir);
    }

    private string GetCachePath(string provider, int z, int x, int y) =>
        Path.Combine(cacheDir, $"{provider}_{z}_{x}_{y}.png");
    
    public async Task<Image<Rgba32>> GetOsmTileAsync(int z, int x, int y, CancellationToken ct)
    {
        string cachePath = Path.Combine(cacheDir, $"osm_{z}_{x}_{y}.png");

        if (File.Exists(cachePath))
            return Image.Load<Rgba32>(await File.ReadAllBytesAsync(cachePath, ct));

        var url = $"https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        var bytes = await httpClient.GetByteArrayAsync(url, ct);

        await File.WriteAllBytesAsync(cachePath, bytes, ct);
        return Image.Load<Rgba32>(bytes);
    }

    public async Task<Image<Rgba32>> GetWeatherTileAsync(
        string apiKey, string layer, int z, int x, int y, CancellationToken ct)
    {
        var url = $"https://tile.openweathermap.org/map/{layer}/{z}/{x}/{y}.png?appid={apiKey}";
        var bytes = await httpClient.GetByteArrayAsync(url, ct);

        return Image.Load<Rgba32>(bytes);
    }
    
    public static string GetDefaultCacheDir(string appName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Voxta", appName);
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var baseDir = !string.IsNullOrEmpty(xdgCache) ? xdgCache : Path.Combine(home, ".cache");
            return Path.Combine(baseDir, "Voxta", appName);
        }
    }
}