namespace Voxta.Modules.Aios.OpenWeather.ChatAugmentations;

public class OpenWeatherChatAugmentationSettings
{
    public string? MyLocation { get; init; }
    public string? Units { get; init; }
    public bool WeatherExpertMode { get; init; }
    public bool PollutionExpertMode { get; init; }
}