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
            if (parts.Length >= 12)
            {
                var name = parts[1].Trim();           // English
                var iso3 = parts[2].Trim();      // ISO3
                var iso2 = parts[3].Trim();      // ISO2
                var native = parts[11].Trim();   // Native

                if (!string.IsNullOrEmpty(name))
                    _countryToAlpha2[name] = iso2;
                if (!string.IsNullOrEmpty(iso2))
                    _countryToAlpha2[iso2] = iso2;
                if (!string.IsNullOrEmpty(iso3))
                    _countryToAlpha2[iso3] = iso2;
                if (!string.IsNullOrEmpty(native))
                    _countryToAlpha2[native] = iso2;
            }
        }
    }

    public static bool TryGetAlpha2(string countryName, out string? alpha2) =>
        _countryToAlpha2.TryGetValue(countryName, out alpha2);
}