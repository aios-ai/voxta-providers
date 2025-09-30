using System.Globalization;
using System.Text;
using Voxta.Modules.Aios.OpenWeather.Clients;

public static class AirPollutionForecastSummariser
{
    public static string Summarise(
        List<AirPollutionData> forecastItems,
        CultureInfo culture,
        HashSet<string> pollutionDetails,
        int days = 5)
    {
        if (forecastItems == null || forecastItems.Count == 0)
            return "No air pollution forecast data available.";

        var sb = new StringBuilder();
        
        var groupedByDate = forecastItems
            .GroupBy(f => DateTimeOffset.FromUnixTimeSeconds(f.Dt).ToLocalTime().Date)
            .OrderBy(g => g.Key)
            .Take(days);

        foreach (var dayGroup in groupedByDate)
        {
            var date = dayGroup.Key;
            
            var dayBlock = dayGroup
                .Where(x =>
                {
                    var hour = DateTimeOffset.FromUnixTimeSeconds(x.Dt).ToLocalTime().Hour;
                    return hour >= 6 && hour < 18;
                })
                .ToList();
            
            var nightBlock = dayGroup
                .Where(x =>
                {
                    var hour = DateTimeOffset.FromUnixTimeSeconds(x.Dt).ToLocalTime().Hour;
                    return hour >= 18 || hour < 6;
                })
                .ToList();

            if (dayBlock.Any())
                sb.AppendLine(BuildBlockSummary(date, "Day", dayBlock, pollutionDetails, culture));

            if (nightBlock.Any())
                sb.AppendLine(BuildBlockSummary(date, "Night", nightBlock, pollutionDetails, culture));
        }

        return sb.ToString().Trim();
    }

    private static string BuildBlockSummary(
        DateTime date,
        string label,
        List<AirPollutionData> block,
        HashSet<string> pollutionDetails,
        CultureInfo culture)
    {
        var avgAqi = (int)Math.Round(block.Average(x => x.Main.Aqi));
        var aqiLabel = GetAqiLabel(avgAqi);

        var avg = new
        {
            Co = block.Average(x => x.Components.Co),
            No = block.Average(x => x.Components.No),
            No2 = block.Average(x => x.Components.No2),
            O3 = block.Average(x => x.Components.O3),
            So2 = block.Average(x => x.Components.So2),
            Pm2_5 = block.Average(x => x.Components.Pm2_5),
            Pm10 = block.Average(x => x.Components.Pm10),
            Nh3 = block.Average(x => x.Components.Nh3)
        };

        var sb = new StringBuilder();
        sb.Append($"{date.ToString("dddd", culture)} {label}: ");

        if (pollutionDetails.Contains("AQI"))
            sb.Append($"AQI {avgAqi} ({aqiLabel})");

        if (pollutionDetails.Contains("CO"))
            sb.Append($", CO: {avg.Co:0.#} µg/m³");
        if (pollutionDetails.Contains("NO"))
            sb.Append($", NO: {avg.No:0.#} µg/m³");
        if (pollutionDetails.Contains("NO2"))
            sb.Append($", NO₂: {avg.No2:0.#} µg/m³");
        if (pollutionDetails.Contains("O3"))
            sb.Append($", O₃: {avg.O3:0.#} µg/m³");
        if (pollutionDetails.Contains("SO2"))
            sb.Append($", SO₂: {avg.So2:0.#} µg/m³");
        if (pollutionDetails.Contains("PM2.5"))
            sb.Append($", PM2.5: {avg.Pm2_5:0.#} µg/m³");
        if (pollutionDetails.Contains("PM10"))
            sb.Append($", PM10: {avg.Pm10:0.#} µg/m³");
        if (pollutionDetails.Contains("NH3"))
            sb.Append($", NH₃: {avg.Nh3:0.#} µg/m³");

        return sb.ToString();
    }

    public static string GetAqiLabel(int aqi) => aqi switch
    {
        1 => "Good",
        2 => "Fair",
        3 => "Moderate",
        4 => "Poor",
        5 => "Very Poor",
        _ => "Unknown"
    };
}