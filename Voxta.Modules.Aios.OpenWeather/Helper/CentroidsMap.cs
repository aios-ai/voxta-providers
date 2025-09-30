using System.Globalization;
using System.Reflection;
using Microsoft.VisualBasic.FileIO;

public static class CountryCentroids
{
    private static readonly Dictionary<string, (double Lat, double Lon)> _centroids;

    static CountryCentroids()
    {
        _centroids = new(StringComparer.OrdinalIgnoreCase);
        
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
    }

    public static bool TryGet(string code, out (double Lat, double Lon) centroid)
    {
        return _centroids.TryGetValue(code, out centroid);
    }
}