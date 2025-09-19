namespace Voxta.Modules.Aios.OpenWeather.Helper;

public static class ForecastCntCalculator
{
    public static int CalculateCnt(DateTime now, string timePhrase)
    {
        timePhrase = timePhrase.ToLowerInvariant();

        // Default: full 5 days
        int skipBlocks = 0;
        int includeBlocks = 8; // one full day

        if (timePhrase.Contains("this evening"))
        {
            // Evening = 18:00–21:00
            var next18 = new DateTime(now.Year, now.Month, now.Day, 18, 0, 0);
            if (now > next18) next18 = next18.AddDays(1);

            skipBlocks = (int)Math.Ceiling((next18 - now).TotalHours / 3);
            includeBlocks = 2; // 18:00 and 21:00
        }
        else if (timePhrase.Contains("tomorrow"))
        {
            var tomorrowStart = now.Date.AddDays(1);
            skipBlocks = (int)Math.Ceiling((tomorrowStart - now).TotalHours / 3);
            includeBlocks = 8; // full day
        }
        else if (timePhrase.Contains("day after tomorrow"))
        {
            var dayAfterStart = now.Date.AddDays(2);
            skipBlocks = (int)Math.Ceiling((dayAfterStart - now).TotalHours / 3);
            includeBlocks = 8;
        }
        // You can add more phrases: "weekend", "next week", "tonight", etc.

        return skipBlocks + includeBlocks;
    }
}