namespace Voxta.Modules.Aios.OpenWeather.ChatAugmentations;

public class OpenWeatherChatAugmentationSettings
{
    public string? MyLocation { get; init; }
    public string? Units { get; init; }
    public string[]? WeatherDetails { get; init; }
    public string[]? PollutionDetails { get; init; }
    public required string TileCachePath { get; init; }
}