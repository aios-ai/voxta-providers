using System.Reflection;
using Microsoft.VisualBasic.FileIO;

namespace Voxta.Modules.Aios.OpenWeather.Helper;

public static class CountryCodeMap
{
    private static readonly Dictionary<string, string> _countryToAlpha2;

    static CountryCodeMap()
    {
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

        _countryToAlpha2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        bool first = true;
        while (!parser.EndOfData)
        {
            var parts = parser.ReadFields();
            if (parts == null) continue;

            if (first) { first = false; continue; }
            if (parts.Length >= 19)
            {
                var name = parts[1].Trim();           // English
                var iso3 = parts[2].Trim();      // ISO3
                var iso2 = parts[3].Trim();      // ISO2
                var tld = parts[10].Trim();      // TLD
                var native = parts[11].Trim();   // Native
                var nationality = parts[18].Trim();

                AddAlias(name, iso2);
                AddAlias(iso2, iso2);
                AddAlias(iso3, iso2);
                AddAlias(native, iso2);

                if (!string.IsNullOrWhiteSpace(tld))
                {
                    AddAlias(tld, iso2);
                    AddAlias(tld.TrimStart('.'), iso2);
                }

                foreach (var alias in nationality.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    AddAlias(alias, iso2);
            }
        }
    }

    public static bool TryGetAlpha2(string countryName, out string? alpha2) =>
        _countryToAlpha2.TryGetValue(countryName, out alpha2);

    private static void AddAlias(string? alias, string alpha2)
    {
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(alpha2))
            return;

        _countryToAlpha2[alias.Trim()] = alpha2;
    }
}
