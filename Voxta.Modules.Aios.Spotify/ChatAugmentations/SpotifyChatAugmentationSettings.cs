namespace Voxta.Modules.Aios.Spotify.ChatAugmentations;

public class SpotifyChatAugmentationSettings
{
    public bool EnableMatchFilter { get; init; }
    public string? MatchFilterWakeWord { get; init; }
    public bool EnableCharacterReplies { get; init; }
    public string? ReleaseRadarPlaylistId { get; init; }
    public string? DiscoverWeeklyPlaylistId { get; init; }
    public string? DailyMix1PlaylistId { get; init; }
    public string? DailyMix2PlaylistId { get; init; }
    public string? DailyMix3PlaylistId { get; init; }
    public string? DailyMix4PlaylistId { get; init; }
    public string? DailyMix5PlaylistId { get; init; }
    public string? DailyMix6PlaylistId { get; init; }
}