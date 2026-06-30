namespace Voxta.Modules.Aios.Spotify.ChatAugmentations;

public class SpotifyChatAugmentationsSettings
{
    public required SpotifyActionSettings[] Actions { get; init; }
    public string? MatchFilterWakeWord { get; init; }
    public int SpeechDuckingVolumePercent { get; init; }
    public Dictionary<string, string> SpecialPlaylists { get; init; } = new();
}

public class SpotifyActionSettings
{
    public required string Name { get; init; }
    public string? Layer { get; init; }
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public string? MatchFilter { get; init; }
    public string? FlagsFilter { get; init; }
    public string? Disabled { get; init; }
    public string? CancelReply { get; init; } = "true";
}
