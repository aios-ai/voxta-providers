using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

public static class CountryCentroids
{
    private static readonly Dictionary<string, (double Lat, double Lon)> _centroids;
    private static readonly Dictionary<string, GeoBounds> _bounds;

    static CountryCentroids()
    {
        _centroids = new(StringComparer.OrdinalIgnoreCase);
        _bounds = new(StringComparer.OrdinalIgnoreCase);
        
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Voxta.Modules.Aios.OpenWeather.Data.countries.csv");
        using var reader = new StreamReader(stream!);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            Delimiters = new[] { "," },
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };

        bool first = true;
        while (!parser.EndOfData)
        {
            var parts = parser.ReadFields();
            if (parts == null) continue;

            if (first) { first = false; continue; }

            if (parts.Length > 21)
            {
                var code = parts[3];
                if (!string.IsNullOrWhiteSpace(code) &&
                    double.TryParse(parts[20], NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(parts[21], NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
                {
                    _centroids[code.Trim()] = (lat, lon);
                }
            }
        }
        
        _centroids["EU"] = (48.0, 22.0);       // Central Europe (Germany/France)
        _centroids["NA"] = (38.0, -89.0);      // Central US, covers North America better
        _centroids["SA"] = (-39.0, -44.0);     // Brazil-centered, good for South America
        _centroids["AF"] = (-18, 42.0);        // Central Africa
        _centroids["AS"] = (30.0, 105.0);      // Covers China, India, SE Asia
        _centroids["OC"] = (-38.0, 158.0);     // Australia-centered
        _centroids["AN"] = (-82.0, 125.0);     // Antarctica

        AddCountryBounds();
        AddStaticBounds();
    }

    public static bool TryGet(string code, out (double Lat, double Lon) centroid)
    {
        return _centroids.TryGetValue(code, out centroid);
    }

    public static bool TryGetBounds(string code, out GeoBounds bounds)
    {
        if (_bounds.TryGetValue(code, out bounds))
            return true;

        if (!_centroids.TryGetValue(code, out var centroid))
            return false;

        bounds = GeoBounds.Around(centroid.Lat, centroid.Lon, latitudeSpan: 8, longitudeSpan: 10);
        return true;
    }

    private static void AddCountryBounds()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Voxta.Modules.Aios.OpenWeather.Data.bounding-boxes.json");

        if (stream == null)
            return;

        using var document = JsonDocument.Parse(stream);
        foreach (var country in document.RootElement.EnumerateObject())
        {
            var fields = country.Value;
            if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() < 2)
                continue;

            var box = fields[1];
            if (box.ValueKind != JsonValueKind.Array || box.GetArrayLength() != 4)
                continue;

            var minLon = box[0].GetDouble();
            var minLat = box[1].GetDouble();
            var maxLon = box[2].GetDouble();
            var maxLat = box[3].GetDouble();

            if (minLon <= -180 && maxLon >= 180 && _centroids.TryGetValue(country.Name, out var centroid))
            {
                var latitudeSpan = Math.Abs(maxLat - minLat);
                var longitudeSpan = latitudeSpan < 10 ? 12 : 180;
                _bounds[country.Name] = GeoBounds.AroundLongitude(centroid.Lon, minLat, maxLat, longitudeSpan);
                continue;
            }

            _bounds[country.Name] = new GeoBounds(minLat, minLon, maxLat, maxLon).ClampForWebMercator();
        }
    }

    private static void AddStaticBounds()
    {
        _bounds["Global"] = new(-85.05112878, -180, 85.05112878, 180);

        _bounds["EU"] = new(34, -25, 72, 45);
        _bounds["NA"] = new(5, -170, 84, -50);
        _bounds["SA"] = new(-56, -82, 13, -34);
        _bounds["AF"] = new(-35, -18, 38, 52);
        _bounds["AS"] = new(-11, 26, 82, 180);
        _bounds["OC"] = new(-50, 110, 0, 180);
        _bounds["AN"] = new(-85, -180, -60, 180);
    }
}

public readonly record struct GeoBounds(double MinLat, double MinLon, double MaxLat, double MaxLon)
{
    private const double MaxMercatorLat = 85.05112878;

    public bool CrossesAntimeridian => MinLon > MaxLon;

    public GeoBounds ClampForWebMercator()
    {
        return new(
            Math.Clamp(MinLat, -MaxMercatorLat, MaxMercatorLat),
            Math.Clamp(MinLon, -180, 180),
            Math.Clamp(MaxLat, -MaxMercatorLat, MaxMercatorLat),
            Math.Clamp(MaxLon, -180, 180));
    }

    public static GeoBounds Around(double lat, double lon, double latitudeSpan, double longitudeSpan)
    {
        var halfLat = latitudeSpan / 2;
        var halfLon = longitudeSpan / 2;
        return new GeoBounds(lat - halfLat, lon - halfLon, lat + halfLat, lon + halfLon).ClampForWebMercator();
    }

    public static GeoBounds AroundLongitude(double centerLon, double minLat, double maxLat, double longitudeSpan)
    {
        var halfLon = longitudeSpan / 2;
        var minLon = NormalizeLongitude(centerLon - halfLon);
        var maxLon = NormalizeLongitude(centerLon + halfLon);
        return new GeoBounds(minLat, minLon, maxLat, maxLon).ClampForWebMercator();
    }

    private static double NormalizeLongitude(double lon)
    {
        while (lon < -180) lon += 360;
        while (lon > 180) lon -= 360;
        return lon;
    }
}
