using Microsoft.Extensions.Logging;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public class HueEntityMatchingService : IHueEntityMatchingService
{
    private readonly IHueDataService _dataService;
    private readonly ILogger<HueEntityMatchingService> _logger;

    public HueEntityMatchingService(IHueDataService dataService, ILogger<HueEntityMatchingService> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    public (Guid? id, string? type, string? matchedName) MatchTargetToId(string? target, string? groupName = null, string? lastUserMessage = null)
    {
        target = target?.ToLowerInvariant();

        if (!string.IsNullOrEmpty(target) && string.IsNullOrEmpty(groupName))
        {
            var light = _dataService.Lights.FirstOrDefault(l => l.Metadata?.Name.ToLowerInvariant() == target);
            if (light != null) return (light.Id, "light", target);

            var group = _dataService.Groups.FirstOrDefault(g => g.Metadata?.Name.ToLowerInvariant() == target);
            if (group != null) return (group.Id, "group", target);

            var room = _dataService.Rooms.FirstOrDefault(r => r.Metadata?.Name.ToLowerInvariant() == target);
            if (room != null)
            {
                var groupedLightService = room.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null) return (groupedLightService.Rid, "group", target);
            }

            var zone = _dataService.Zones.FirstOrDefault(z => z.Metadata?.Name.ToLowerInvariant() == target);
            if (zone != null)
            {
                var groupedLightService = zone.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null) return (groupedLightService.Rid, "group", target);
            }
        }

        if (!string.IsNullOrEmpty(groupName))
        {
            var groupScene = _dataService.Groups.FirstOrDefault(g => string.Equals(g.Metadata?.Name, groupName, StringComparison.InvariantCultureIgnoreCase));
            if (groupScene != null)
            {
                var scene = _dataService.Scenes.FirstOrDefault(s =>
                    string.Equals(s.Metadata?.Name, target, StringComparison.InvariantCultureIgnoreCase) &&
                    s.Group?.Rid == groupScene.Id);
                if (scene != null) return (scene.Id, "scene", target);
            }

            var roomScene = _dataService.Rooms.FirstOrDefault(r => string.Equals(r.Metadata?.Name, groupName, StringComparison.InvariantCultureIgnoreCase));
            if (roomScene != null)
            {
                var groupedLightService = roomScene.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null)
                {
                    var scene = _dataService.Scenes.FirstOrDefault(s =>
                        string.Equals(s.Metadata?.Name, target, StringComparison.InvariantCultureIgnoreCase) &&
                        s.Group?.Rid == groupedLightService.Rid);
                    if (scene != null) return (scene.Id, "scene", target);
                }
            }

            var zoneScene = _dataService.Zones.FirstOrDefault(z => string.Equals(z.Metadata?.Name, groupName, StringComparison.InvariantCultureIgnoreCase));
            if (zoneScene != null)
            {
                var groupedLightService = zoneScene.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null)
                {
                    var scene = _dataService.Scenes.FirstOrDefault(s =>
                        string.Equals(s.Metadata?.Name, target, StringComparison.InvariantCultureIgnoreCase) &&
                        s.Group?.Rid == groupedLightService.Rid);
                    if (scene != null) return (scene.Id, "scene", target);
                }
            }
        }

        if (!string.IsNullOrEmpty(lastUserMessage))
        {
            var lastMessage = lastUserMessage;
            if (!string.IsNullOrEmpty(target) && string.IsNullOrEmpty(groupName))
            {
                var light = _dataService.Lights.FirstOrDefault(l =>
                    !string.IsNullOrEmpty(l.Metadata?.Name) &&
                    lastMessage.Contains(l.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (light != null)
                {
                    _logger.LogInformation("LastUserMessage match on light: {MetadataName}", light.Metadata?.Name);
                    return (light.Id, "light", light.Metadata?.Name);
                }

                var group = _dataService.Groups.FirstOrDefault(g =>
                    !string.IsNullOrEmpty(g.Metadata?.Name) &&
                    lastMessage.Contains(g.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (group != null)
                {
                    _logger.LogInformation("LastUserMessage match on group: {MetadataName}", group.Metadata?.Name);
                    return (group.Id, "group", group.Metadata?.Name);
                }

                var room = _dataService.Rooms.FirstOrDefault(r =>
                    !string.IsNullOrEmpty(r.Metadata?.Name) &&
                    lastMessage.Contains(r.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (room != null)
                {
                    _logger.LogInformation("LastUserMessage match on room: {MetadataName}", room.Metadata?.Name);
                    var groupedLightService = room.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                    {
                        _logger.LogInformation("Grouped light service found: {Guid}", groupedLightService.Rid);
                        return (groupedLightService.Rid, "group", room.Metadata?.Name);
                    }
                }

                var zone = _dataService.Zones.FirstOrDefault(z =>
                    !string.IsNullOrEmpty(z.Metadata?.Name) &&
                    lastMessage.Contains(z.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (zone != null)
                {
                    _logger.LogInformation("LastUserMessage match on zone: {MetadataName}", zone.Metadata?.Name);
                    var groupedLightService = zone.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                    {
                        _logger.LogInformation("Grouped light service found: {Guid}", groupedLightService.Rid);
                        return (groupedLightService.Rid, "group", zone.Metadata?.Name);
                    }
                }
            }

            if (!string.IsNullOrEmpty(groupName))
            {
                var groupScene = _dataService.Groups.FirstOrDefault(g =>
                    !string.IsNullOrEmpty(g.Metadata?.Name) &&
                    groupName.Contains(g.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (groupScene != null)
                {
                    _logger.LogInformation("LastUserMessage match on group for scene: {MetadataName}", groupScene.Metadata?.Name);
                    var scene = _dataService.Scenes.FirstOrDefault(s =>
                        !string.IsNullOrEmpty(s.Metadata?.Name) &&
                        lastMessage.Contains(s.Metadata.Name, StringComparison.InvariantCultureIgnoreCase) &&
                        s.Group?.Rid == groupScene.Id);
                    if (scene != null)
                    {
                        _logger.LogInformation("Scene match for group: {MetadataName}", scene.Metadata?.Name);
                        return (scene.Id, "scene", scene.Metadata?.Name);
                    }
                }

                var roomScene = _dataService.Rooms.FirstOrDefault(r =>
                    !string.IsNullOrEmpty(r.Metadata?.Name) &&
                    groupName.Contains(r.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (roomScene != null)
                {
                    _logger.LogInformation("LastUserMessage match on room for scene: {MetadataName}", roomScene.Metadata?.Name);
                    var roomId = roomScene.Id;
                    _logger.LogInformation("Grouped light service found for room: {RoomId}", roomId);
                    var scene = _dataService.Scenes.FirstOrDefault(s =>
                        !string.IsNullOrEmpty(s.Metadata?.Name) &&
                        lastMessage.Contains(s.Metadata.Name.ToLowerInvariant()) &&
                        s.Group?.Rid == roomId);
                    if (scene != null)
                    {
                        _logger.LogInformation("Scene match for room: {MetadataName}", scene.Metadata?.Name);
                        return (scene.Id, "scene", scene.Metadata?.Name);
                    }
                }

                var zoneScene = _dataService.Zones.FirstOrDefault(z =>
                    !string.IsNullOrEmpty(z.Metadata?.Name) &&
                    groupName.Contains(z.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (zoneScene != null)
                {
                    _logger.LogInformation("LastUserMessage match on zone for scene: {MetadataName}", zoneScene.Metadata?.Name);
                    var zoneId = zoneScene.Id;
                    _logger.LogInformation("Grouped light service found for zone: {ZoneId}", zoneId);
                    var scene = _dataService.Scenes.FirstOrDefault(s =>
                        !string.IsNullOrEmpty(s.Metadata?.Name) &&
                        lastMessage.Contains(s.Metadata.Name, StringComparison.InvariantCultureIgnoreCase) &&
                        s.Group?.Rid == zoneId);
                    if (scene != null)
                    {
                        _logger.LogInformation("Scene match for zone: {MetadataName}", scene.Metadata?.Name);
                        return (scene.Id, "scene", scene.Metadata?.Name);
                    }
                }
            }
        }

        return (null, null, null);
    }
}
