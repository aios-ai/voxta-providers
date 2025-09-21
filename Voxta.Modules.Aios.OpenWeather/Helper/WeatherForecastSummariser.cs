using System.Globalization;
using System.Text;
using Voxta.Modules.Aios.OpenWeather.Clients;

public static class WeatherForecastSummariser
{
    public static string Summarise(List<ForecastItem> forecastItems, int days = 5, string unitSuffix = "")
    {
        if (forecastItems == null || forecastItems.Count == 0)
            return "No forecast data available.";

        var sb = new StringBuilder();
        
        var groupedByDate = forecastItems
            .GroupBy(f => DateTime.Parse(f.DtTxt!, CultureInfo.InvariantCulture).Date)
            .OrderBy(g => g.Key)
            .Take(days);

        foreach (var dayGroup in groupedByDate)
        {
            var date = dayGroup.Key;

            // Daytime: 06:00–18:00
            var dayBlock = dayGroup
                .Where(x =>
                {
                    var hour = DateTime.Parse(x.DtTxt!, CultureInfo.InvariantCulture).Hour;
                    return hour >= 6 && hour < 18;
                })
                .ToList();

            // Nighttime: 18:00–06:00 (next day)
            var nightBlock = dayGroup
                .Where(x =>
                {
                    var hour = DateTime.Parse(x.DtTxt!, CultureInfo.InvariantCulture).Hour;
                    return hour >= 18 || hour < 6;
                })
                .ToList();

            if (dayBlock.Any())
                sb.AppendLine(BuildBlockSummary(date, "Day", dayBlock, unitSuffix));

            if (nightBlock.Any())
                sb.AppendLine(BuildBlockSummary(date, "Night", nightBlock, unitSuffix));
        }

        return sb.ToString().Trim();
    }

    private static string BuildBlockSummary(DateTime date, string label, List<ForecastItem> block, string unitSuffix)
    {
        var minTemp = block.Min(x => x.Main.TempMin);
        var maxTemp = block.Max(x => x.Main.TempMax);

        var commonCondition = block
            .GroupBy(x => x.Weather[0].Description)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var totalRain = block.Sum(x => x.Rain?.ThreeHours ?? 0);
        var totalSnow = block.Sum(x => x.Snow?.ThreeHours ?? 0);

        var precipText = "";
        if (totalRain > 0) precipText += $"rain ({totalRain:0.#} mm)";
        if (totalSnow > 0) precipText += (precipText.Length > 0 ? " and " : "") + $"snow ({totalSnow:0.#} mm)";

        var text = $"{date:dddd} {label}: {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(commonCondition)}, {minTemp:0.#}–{maxTemp:0.#}{unitSuffix}";
        if (!string.IsNullOrEmpty(precipText))
            text += $", with {precipText}";

        return text + ".";
    }
}
