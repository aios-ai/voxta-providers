namespace Voxta.Modules.Aios.Spotify.ChatAugmentations;

public class SpotifyChatAugmentationsSettings
{
    public string? MatchFilterWakeWord { get; init; }
    public int SpeechDuckingVolumePercent { get; init; }
    public bool EnableCharacterReplies { get; init; }
    public Dictionary<string, string> SpecialPlaylists { get; init; } = new();
}
