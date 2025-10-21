namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public interface IHueEntityMatchingService
{
    (Guid? id, string? type, string? matchedName) MatchTargetToId(string? target, string? groupName = null, string? lastUserMessage = null);
}
