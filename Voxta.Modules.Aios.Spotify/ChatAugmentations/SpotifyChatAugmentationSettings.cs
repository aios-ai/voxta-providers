namespace Voxta.Modules.Aios.Spotify.ChatAugmentations;

public class SpotifyChatAugmentationSettings
{
    public bool EnableMatchFilter { get; init; }
    public string? MatchFilterWakeWord { get; init; }
    public bool EnableCharacterReplies { get; init; }
    public Dictionary<string, string> SpecialPlaylists { get; init; } = new();

}