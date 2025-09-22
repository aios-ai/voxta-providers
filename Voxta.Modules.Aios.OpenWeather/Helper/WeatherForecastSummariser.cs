using System.Globalization;
using System.Text;
using Voxta.Modules.Aios.OpenWeather.Clients;

public static class WeatherForecastSummariser
{
    public static string Summarise(List<ForecastItem> forecastItems, CultureInfo culture,  bool weatherExpertMode, int days = 5, string unitSuffix = "")
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

            var dayBlock = dayGroup
                .Where(x =>
                {
                    var hour = DateTime.Parse(x.DtTxt!, CultureInfo.InvariantCulture).Hour;
                    return hour >= 8 && hour < 20;
                })
                .ToList();

            var nightBlock = dayGroup
                .Where(x =>
                {
                    var hour = DateTime.Parse(x.DtTxt!, CultureInfo.InvariantCulture).Hour;
                    return hour >= 20 || hour < 8;
                })
                .ToList();

            if (dayBlock.Any())
                sb.AppendLine(BuildBlockSummary(date, "Day", dayBlock, unitSuffix, culture, weatherExpertMode));

            if (nightBlock.Any())
                sb.AppendLine(BuildBlockSummary(date, "Night", nightBlock, unitSuffix, culture, weatherExpertMode));
        }

        return sb.ToString().Trim();
    }

    private static string BuildBlockSummary(DateTime date, string label, List<ForecastItem> block, string unitSuffix, CultureInfo culture, bool weatherExpertMode)
    {
        var minTemp = block.Min(x => x.Main.TempMin);
        var maxTemp = block.Max(x => x.Main.TempMax);

        var commonCondition = block
            .GroupBy(x => x.Weather[0].Description)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var totalRain = block.Sum(x => x.Rain?.ThreeHours ?? 0);
        var totalSnow = block.Sum(x => x.Snow?.ThreeHours ?? 0);

        var rainText = totalRain > 0
            ? $" and estimated {totalRain:0.#} mm precipitation of rain"
            : "";

        var snowText = totalSnow > 0
            ? $" and estimated {totalSnow:0.#} mm precipitation of snow"
            : "";

        var text = $"{date.ToString("dddd", culture)} {label}: " +
                   $"{culture.TextInfo.ToTitleCase(commonCondition)}, " +
                   $"{minTemp:0.#}–{maxTemp:0.#}{unitSuffix}" +
                   $"{rainText}{snowText}";

        if (weatherExpertMode)
        {
            // Example: if ForecastItem has Wind, Clouds, Visibility
            var avgWindSpeed = block.Average(x => x.Wind.Speed);
            var avgWindDir = block.Average(x => x.Wind.Deg);
            var avgClouds = block.Average(x => x.Clouds.All);
            var avgVisibility = block.Average(x => x.Visibility) / 1000.0;

            text += $". Wind: {avgWindSpeed:0.#} m/s at {avgWindDir:0.#}°, " +
                    $"Cloud cover: {avgClouds:0.#}%, " +
                    $"Visibility: {avgVisibility:0.#} km";
        }

        return text + ".";
    }
}