using System.Reflection;

namespace Voxta.Modules.Aios.OpenWeather.Helper;
public static class CountryCodeMap
{
    private static readonly Dictionary<string, string> _countryToAlpha2;

    static CountryCodeMap()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Voxta.Modules.Aios.OpenWeather.Data.countries.csv");
        using var reader = new StreamReader(stream!);

        _countryToAlpha2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? line;
        bool first = true;
        while ((line = reader.ReadLine()) != null)
        {
            if (first) { first = false; continue; }
            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                var country = parts[0].Trim();
                var alpha2 = parts[1].Trim();
                _countryToAlpha2[country] = alpha2;
            }
        }
    }

    public static bool TryGetAlpha2(string countryName, out string? alpha2) =>
        _countryToAlpha2.TryGetValue(countryName, out alpha2);
}