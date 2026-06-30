namespace Voxta.Modules.Aios.PhilipsHue.ChatAugmentations;

public class PhilipsHueChatAugmentationsSettings
{
    public required PhilipsHueActionSettings[] Actions { get; init; }
    public string? Ip { get; init; }
    public string? Username { get; init; }
    public string? CharacterControlledLight { get; init; }
    public string? AuthPath { get; init; }
    public bool SendInventoryAtSessionStart { get; init; }
}

public class PhilipsHueActionSettings
{
    public required string Name { get; init; }
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public string? MatchFilter { get; init; }
    public string? FlagsFilter { get; init; }
    public string? Disabled { get; init; }
    public string? CancelReply { get; init; } = "true";
}
