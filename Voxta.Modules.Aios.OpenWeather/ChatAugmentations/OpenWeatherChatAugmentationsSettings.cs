namespace Voxta.Modules.Aios.OpenWeather.ChatAugmentations;

public class OpenWeatherChatAugmentationsSettings
{
    public required OpenWeatherActionSettings[] Actions { get; init; }
    public string? MyLocation { get; init; }
    public string? Units { get; init; }
    public string[]? WeatherDetails { get; init; }
    public string[]? PollutionDetails { get; init; }
    public required string TileCachePath { get; init; }
}

public class OpenWeatherActionSettings
{
    public required string Name { get; init; }
    public string? Layer { get; init; }
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public string? MatchFilter { get; init; }
    public string? Disabled { get; init; }
    public string? CancelReply { get; init; } = "true";
}
