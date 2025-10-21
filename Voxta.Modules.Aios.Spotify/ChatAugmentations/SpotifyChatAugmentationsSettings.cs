namespace Voxta.Modules.Aios.Spotify.ChatAugmentations;

public class SpotifyChatAugmentationsSettings
{
    public bool EnableMatchFilter { get; init; }
    public string? MatchFilterWakeWord { get; init; }
    public bool EnableVolumeControlDuringSpeech  { get; init; }
    public bool EnableCharacterReplies { get; init; }
    public Dictionary<string, string> SpecialPlaylists { get; init; } = new();
}