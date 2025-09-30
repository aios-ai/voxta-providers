using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public class TileFetcher
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;

    public TileFetcher(HttpClient client, string cacheDir)
    {
        _httpClient = client;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "MyWeatherApp/1.0 (contact: aios@example.com)");

        _cacheDir = cacheDir ?? throw new ArgumentNullException(nameof(cacheDir));

        if (!Directory.Exists(_cacheDir))
            Directory.CreateDirectory(_cacheDir);
    }
    
    public async Task<Image<Rgba32>> GetOsmTileAsync(int z, int x, int y, CancellationToken ct)
    {
        string cachePath = Path.Combine(_cacheDir, $"osm_{z}_{x}_{y}.png");

        if (File.Exists(cachePath))
            return Image.Load<Rgba32>(await File.ReadAllBytesAsync(cachePath, ct));

        var url = $"https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        var bytes = await _httpClient.GetByteArrayAsync(url, ct);

        await File.WriteAllBytesAsync(cachePath, bytes, ct);
        return Image.Load<Rgba32>(bytes);
    }

    public async Task<Image<Rgba32>> GetWeatherTileAsync(
        string apiKey, string layer, int z, int x, int y, CancellationToken ct)
    {
        var url = $"https://tile.openweathermap.org/map/{layer}/{z}/{x}/{y}.png?appid={apiKey}";
        var bytes = await _httpClient.GetByteArrayAsync(url, ct);

        return Image.Load<Rgba32>(bytes);
    }
}