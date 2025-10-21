using System.Text.Json;
using HueApi;
using HueApi.BridgeLocator;
using HueApi.ColorConverters;
using HueApi.ColorConverters.Original.Extensions;
using HueApi.Models;
using HueApi.Models.Clip;
using HueApi.Models.Exceptions;
using HueApi.Models.Requests;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Objects.Chats;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Modules.Aios.PhilipsHue.Clients;

namespace Voxta.Modules.Aios.PhilipsHue.ChatAugmentations;

public class HueManager(
    IChatSessionChatAugmentationApi session,
    ILogger<HueManager> logger,
    string authPath,
    IHueUserInteractionWrapper userInteractionWrapper
)
{
    private const int MaxRetries = 20;
    private const int RetryIntervalSeconds = 5;
    
    private readonly string _appKeyFile = authPath;
    
    public LocalHueApi HueClient => _hueClient ?? throw new InvalidOperationException("Hue client not initialized.");
    public IList<Light> Lights => _lights ??= new List<Light>();
    
    public string? LastUserMessage = null;
    public bool IsConnected => _hueClient != null; // Added IsConnected property

    private LocalHueApi? _hueClient;
    private string? _bridgeIp;
    private IList<Light>? _lights;
    private IList<GroupedLight>? _groups;
    private IList<Room>? _rooms;
    private IList<Zone>? _zones;
    private IList<Scene>? _scenes;
    //private IList<EntertainmentConfiguration>? _entertainmentGroups;

    //////////////////////////////////////////////////////////////////////////////////
    // Bridge discovery & Connect
    //////////////////////////////////////////////////////////////////////////////////
    public async Task InitializeBridgeAsync(CancellationToken cancellationToken)
    {
            logger.LogInformation("Initializing Hue Bridge...");

            if (File.Exists(_appKeyFile))
            {
                logger.LogInformation("Authentication file found. Attempting to connect using saved configuration...");
                var appKey = LoadAppKey();

                if (appKey != null && !string.IsNullOrEmpty(appKey.Ip) && !string.IsNullOrEmpty(appKey.Username))
                {
                    _bridgeIp = appKey.Ip;
                    try
                    {
                        _hueClient = new LocalHueApi(_bridgeIp, appKey.Username);
                        logger.LogInformation("Connected to Hue bridge using saved configuration.");
                        await RetrieveBridgeDataAsync();
                        await session.SetFlags(SetFlagRequest.ParseFlags(["hueBridge_connected", "!hueBridge_disconnected"]), cancellationToken);
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to connect to the Hue bridge using saved configuration. Falling back to discovery...");
                    }
                }
            }
            else
            {
                logger.LogInformation("No Authentication file found. Starting bridge discovery...");
            }

            await DiscoverAndConnectBridgeAsync(cancellationToken);

    }

    private async Task DiscoverAndConnectBridgeAsync(CancellationToken cancellationToken)
    {
        var retryCount = 0;
        logger.LogInformation("Discovering Hue bridge...");

        while (retryCount < MaxRetries)
        {
            //Basic Bridge Discovery options:
            var bridgeLocator = new HttpBridgeLocator();
            // Or:
            //var bridgeLocator = new LocalNetworkScanBridgeLocator();
            //var bridgeLocator = new MdnsBridgeLocator();
            //var bridgeLocator = new MUdpBasedBridgeLocator();
            var bridges = (await bridgeLocator.LocateBridgesAsync(TimeSpan.FromSeconds(RetryIntervalSeconds))).ToArray();

            //Advanced Bridge Discovery options:
            //var bridges = await HueBridgeDiscovery.CompleteDiscoveryAsync(TimeSpan.FromSeconds(RetryIntervalSeconds), TimeSpan.FromSeconds(30));
            //var bridges = await HueBridgeDiscovery.FastDiscoveryWithNetworkScanFallbackAsync(TimeSpan.FromSeconds(RetryIntervalSeconds), TimeSpan.FromSeconds(30));

            if (bridges.Length != 0)
            {
                var bridgeInfo = bridges.First();
                _bridgeIp = bridgeInfo.IpAddress;
                logger.LogInformation("Bridge discovered: {BridgeIp}", _bridgeIp);
                await ConnectBridgeAsync(cancellationToken);
                return;
            }

            logger.LogWarning("No Hue bridges found. Retrying...");
            retryCount++;
            await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds), cancellationToken);
        }

        logger.LogWarning("Max retries reached. No Hue bridges found.");
    }

    private async Task ConnectBridgeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_bridgeIp))
        {
            logger.LogWarning("No bridge discovered.");
            return;
        }

        var savedAppKey = LoadAppKey();
        if (savedAppKey != null)
        {
            try
            {
                _hueClient = new LocalHueApi(_bridgeIp, savedAppKey.Username);
                logger.LogInformation("Connected to Hue bridge using saved app key...");
                await RetrieveBridgeDataAsync();
                await session.SetFlags(SetFlagRequest.ParseFlags(["hueBridge_connected", "!hueBridge_disconnected"]), cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to connect to the Hue bridge using saved app key. Falling back to registration...");
            }
        }

        await AttemptBridgeRegistrationAsync(cancellationToken);
    }

    private async Task AttemptBridgeRegistrationAsync(CancellationToken cancellationToken)
    {
        await using var _ = await userInteractionWrapper.RequestUserInteraction(cancellationToken);
        
        var registrationSuccessful = false;
        var retryDelay = TimeSpan.FromSeconds(RetryIntervalSeconds);
        var retries = 0;

        while (!registrationSuccessful && retries < MaxRetries && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var appKey = await LocalHueApi.RegisterAsync(_bridgeIp!, "Voxta", Environment.MachineName, false);
                await SaveAppKey(appKey);
                logger.LogInformation("Bridge connected and app key saved.");

                _hueClient = new LocalHueApi(_bridgeIp, appKey.Username);
                await RetrieveBridgeDataAsync();
                await session.SetFlags(SetFlagRequest.ParseFlags(["hueBridge_connected", "!hueBridge_disconnected"]), cancellationToken);

                registrationSuccessful = true;
            }
            catch (LinkButtonNotPressedException)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                retries++;
                logger.LogWarning("Link button not pressed. Attempt {Retries} of {MaxRetries}. Retrying in {RetryDelaySeconds} seconds.", retries, MaxRetries, retryDelay.Seconds);
                await Task.Delay(retryDelay, cancellationToken);
                
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register with the Hue bridge.");
                return;
            }
        }
        
        if (!registrationSuccessful && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Failed to register with Hue Bridge after {MaxRetries} retries.", MaxRetries);
        }
    }
    
    private RegisterEntertainmentResult? LoadAppKey()
    {
        if (File.Exists(_appKeyFile))
        {
            return JsonSerializer.Deserialize<RegisterEntertainmentResult>(File.ReadAllText(_appKeyFile));
        }
        return null;
    }

    private async Task SaveAppKey(RegisterEntertainmentResult? appKey)
    {
        if (appKey == null)
        {
            logger.LogError("AppKey is null. Cannot save to file.");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_appKeyFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(appKey);
            await File.WriteAllTextAsync(_appKeyFile, json);
            logger.LogInformation("App key saved to file.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save app key to file: {Message}", ex.Message);
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////
    // Get lights, groups, rooms & scenes
    //////////////////////////////////////////////////////////////////////////////////
    private async Task RetrieveBridgeDataAsync()
    {
        await GetLightsAsync();
        await GetGroupsAsync();
        await GetRoomsAsync();
        await GetZonesAsync();
        await GetScenesAsync();
        //await GetEntertainmentAsync();
    }
    private async Task RetrieveDataAsync<T>(Func<Task<HueResponse<T>>> apiCall, Action<IList<T>> setData, string dataType)

    {
        if (_hueClient == null)
        {
            logger.LogWarning("Hue client not initialized.");
            return;
        }

        try
        {
            var response = await apiCall();
            var data = response.Data;

            if (data.Count == 0)
            {
                logger.LogWarning("No {DataType} retrieved.", dataType);
                return;
            }

            setData(data);

            logger.LogInformation("Retrieved {DataCount} {DataType}.", data.Count, dataType);
        }
        catch (Exception ex)
        {
            logger.LogError("Error retrieving {DataType}: {ExMessage}", dataType, ex.Message);
        }
    }

    private async Task GetLightsAsync()
    {
        await RetrieveDataAsync(
            _hueClient!.GetLightsAsync,
            data =>
            {
                //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                //logger.LogInformation($"Serialized lights: {json}");
                _lights = data;
                foreach (var light in data)
                {
                    logger.LogInformation("Light: {MetadataName}", light.Metadata?.Name);
                }
            },
            "lights"
        );
    }

    private async Task GetGroupsAsync()
    {
        await RetrieveDataAsync(
            _hueClient!.GetGroupedLightsAsync,
            data =>
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                logger.LogInformation($"Serialized groups: {json}");
                _groups = data;
                foreach (var group in data)
                {
                    logger.LogInformation($"Group: {group.Metadata?.Name}");
                }
            },
            "groups"
        );
    }

    private async Task GetRoomsAsync()
    {
        await RetrieveDataAsync(
            _hueClient!.GetRoomsAsync,
            data =>
            {
                //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                //logger.LogInformation($"Serialized rooms: {json}");
                _rooms = data;
                foreach (var room in data)
                {
                    logger.LogInformation("Room: {MetadataName}", room.Metadata?.Name);
                }
            },
            "rooms"
        );
    }

    private async Task GetZonesAsync()
    {
        await RetrieveDataAsync(
            _hueClient!.GetZonesAsync,
            data =>
            {
                //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                //logger.LogInformation($"Serialized zones: {json}");
                _zones = data;
                foreach (var scene in data)
                {
                    logger.LogInformation("Zone: {MetadataName}", scene.Metadata?.Name);
                }
            },
            "zones"
        );
    }
    private async Task GetScenesAsync()
    {
        await RetrieveDataAsync(
            _hueClient!.GetScenesAsync,
            data =>
            {
                //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                //logger.LogInformation($"Serialized scenes: {json}");
                _scenes = data;
                foreach (var scene in data)
                {
                    var sceneName = scene.Metadata?.Name ?? "Unknown";

                    if (scene.Group != null)
                    {
                        var groupName = "Unknown";
                        if (scene.Group.Rtype == "zone")
                        {
                            var zone = _zones!.FirstOrDefault(z => z.Id == scene.Group.Rid);
                            groupName = zone?.Metadata?.Name ?? "Unknown Zone";
                        }
                        else if (scene.Group.Rtype == "room")
                        {
                            var room = _rooms!.FirstOrDefault(r => r.Id == scene.Group.Rid);
                            groupName = room?.Metadata?.Name ?? "Unknown Room";
                        }

                        sceneName = $"{sceneName} ({groupName})";
                    }

                    logger.LogInformation("Scene: {SceneName}", sceneName);
                }
            },
            "scenes"
        );
    }

    /*private async Task GetEntertainmentAsync()
    {
        await RetrieveDataAsync(
            _hueClient!.GetEntertainmentConfigurationsAsync,
            data =>
            {
                //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                //logger.LogInformation($"Serialized EntertainmentGroups: {json}");
                _entertainmentGroups = data;
                foreach (var scene in data)
                {
                    logger.LogInformation($"EntertainmentGroups: {scene.Metadata?.Name}");
                }
            },
            "entertainmentGroups"
        );
    }*/
        
    //////////////////////////////////////////////////////////////////////////////////
    // Match objects
    //////////////////////////////////////////////////////////////////////////////////
    public (Guid? id, string? type, string? matchedName) MatchTargetToId(string? target, string? groupName = null)
    {
        // Normalize the input
        target = target?.ToLowerInvariant();

        // Direct matching by target name
        if (!string.IsNullOrEmpty(target) && string.IsNullOrEmpty(groupName))
        {
            // Check Lights
            var light = _lights?.FirstOrDefault(l => l.Metadata?.Name.ToLowerInvariant() == target);
            if (light != null)
                return (light.Id, "light", target);

            // Check Groups
            var group = _groups?.FirstOrDefault(g => g.Metadata?.Name.ToLowerInvariant() == target);
            if (group != null)
                return (group.Id, "group", target);

            // Check Rooms
            var room = _rooms?.FirstOrDefault(r => r.Metadata?.Name.ToLowerInvariant() == target);
            if (room != null)
            {
                var groupedLightService = room.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null)
                    return (groupedLightService.Rid, "group", target);
            }

            // Check Zones
            var zone = _zones?.FirstOrDefault(z => z.Metadata?.Name.ToLowerInvariant() == target);
            if (zone != null)
            {
                var groupedLightService = zone.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null)
                    return (groupedLightService.Rid, "group", target);
            }

            // Check Entertainment Groups
            /*var entertainmentGroup = _entertainmentGroups?.FirstOrDefault(e => e.Metadata?.Name?.ToLowerInvariant() == target);
            if (entertainmentGroup != null)
                return (entertainmentGroup.Id, "entertainment_configuration", target);*/
        }

        // Check Scenes for the specific room
        if (!string.IsNullOrEmpty(groupName))
        {
            // Check if groupName matches a group
            var groupScene = _groups?.FirstOrDefault(g => string.Equals(g.Metadata?.Name, groupName, StringComparison.InvariantCultureIgnoreCase));
            if (groupScene != null)
            {
                var scene = _scenes?.FirstOrDefault(s =>
                    string.Equals(s.Metadata?.Name, target, StringComparison.InvariantCultureIgnoreCase) &&
                    s.Group?.Rid == groupScene.Id);

                if (scene != null)
                    return (scene.Id, "scene", target);
            }

            // Check if groupName matches a room
            var roomScene = _rooms?.FirstOrDefault(r => string.Equals(r.Metadata?.Name, groupName, StringComparison.InvariantCultureIgnoreCase));
            if (roomScene != null)
            {
                var groupedLightService = roomScene.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null)
                {
                    var scene = _scenes?.FirstOrDefault(s =>
                        string.Equals(s.Metadata?.Name, target, StringComparison.InvariantCultureIgnoreCase) &&
                        s.Group?.Rid == groupedLightService.Rid);

                    if (scene != null)
                        return (scene.Id, "scene", target);
                }
            }

            // Check if groupName matches a zone
            var zoneScene = _zones?.FirstOrDefault(z => string.Equals(z.Metadata?.Name, groupName, StringComparison.InvariantCultureIgnoreCase));
            if (zoneScene != null)
            {
                var groupedLightService = zoneScene.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null)
                {
                    var scene = _scenes?.FirstOrDefault(s =>
                        string.Equals(s.Metadata?.Name, target, StringComparison.InvariantCultureIgnoreCase) &&
                        s.Group?.Rid == groupedLightService.Rid);

                    if (scene != null)
                        return (scene.Id, "scene", target);
                }
            }

            // Check if groupName matches a entertainmentGroups
            /*var entertainmentGroupScene = _entertainmentGroups?.FirstOrDefault(e => e.Metadata?.Name?.ToLowerInvariant() == groupName.ToLowerInvariant());
            if (entertainmentGroupScene != null)
            {
                var groupedLightService = entertainmentGroupScene.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                if (groupedLightService != null)
                {
                    var scene = _scenes?.FirstOrDefault(s =>
                        s.Metadata?.Name?.ToLowerInvariant() == target &&
                        s.Group?.Rid == groupedLightService.Rid);

                    if (scene != null)
                        return (scene.Id, "scene", target);
                }
            }*/
        }

        // No target provided, match Metadata.Name within LastUserMessage
        if (!string.IsNullOrEmpty(LastUserMessage))
        {
            var lastMessage = LastUserMessage;
            if (!string.IsNullOrEmpty(target) && string.IsNullOrEmpty(groupName))
            {
                // Check Lights
                var light = _lights?.FirstOrDefault(l =>
                    !string.IsNullOrEmpty(l.Metadata?.Name) &&
                    lastMessage.Contains(l.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (light != null)
                {
                    logger.LogInformation("LastUserMessage match on light: {MetadataName}", light.Metadata?.Name);
                    return (light.Id, "light", light.Metadata?.Name);
                }

                // Check Groups
                var group = _groups?.FirstOrDefault(g =>
                    !string.IsNullOrEmpty(g.Metadata?.Name) &&
                    lastMessage.Contains(g.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (group != null)
                {
                    logger.LogInformation("LastUserMessage match on group: {MetadataName}", group.Metadata?.Name);
                    return (group.Id, "group", group.Metadata?.Name);
                }

                // Check Rooms
                var room = _rooms?.FirstOrDefault(r =>
                    !string.IsNullOrEmpty(r.Metadata?.Name) &&
                    lastMessage.Contains(r.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (room != null)
                {
                    logger.LogInformation("LastUserMessage match on room: {MetadataName}", room.Metadata?.Name);

                    var groupedLightService = room.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                    {
                        logger.LogInformation("Grouped light service found: {Guid}", groupedLightService.Rid);
                        return (groupedLightService.Rid, "group", room.Metadata?.Name);
                    }
                }

                // Check Zones
                var zone = _zones?.FirstOrDefault(z =>
                    !string.IsNullOrEmpty(z.Metadata?.Name) &&
                    lastMessage.Contains(z.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (zone != null)
                {
                    logger.LogInformation("LastUserMessage match on zone: {MetadataName}", zone.Metadata?.Name);

                    var groupedLightService = zone.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                    {
                        logger.LogInformation("Grouped light service found: {Guid}", groupedLightService.Rid);
                        return (groupedLightService.Rid, "group", zone.Metadata?.Name);
                    }
                }
            }

            if (!string.IsNullOrEmpty(groupName))
            {
                // Check if groupName matches a group
                var groupScene = _groups?.FirstOrDefault(g =>
                    !string.IsNullOrEmpty(g.Metadata?.Name) &&
                    groupName.Contains(g.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (groupScene != null)
                {
                    logger.LogInformation("LastUserMessage match on group for scene: {MetadataName}", groupScene.Metadata?.Name);

                    var scene = _scenes?.FirstOrDefault(s =>
                        !string.IsNullOrEmpty(s.Metadata?.Name) &&
                        lastMessage.Contains(s.Metadata.Name, StringComparison.InvariantCultureIgnoreCase) &&
                        s.Group?.Rid == groupScene.Id);

                    if (scene != null)
                    {
                        logger.LogInformation("Scene match for group: {MetadataName}", scene.Metadata?.Name);
                        return (scene.Id, "scene", scene.Metadata?.Name);
                    }
                }

                // Check if groupName matches a room
                var roomScene = _rooms?.FirstOrDefault(r =>
                    !string.IsNullOrEmpty(r.Metadata?.Name) &&
                    groupName.Contains(r.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (roomScene != null)
                {
                    logger.LogInformation("LastUserMessage match on room for scene: {MetadataName}", roomScene.Metadata?.Name);

                    var roomId = roomScene.Id;
                    logger.LogInformation("Grouped light service found for room: {RoomId}", roomId);

                    var scene = _scenes?.FirstOrDefault(s =>
                        !string.IsNullOrEmpty(s.Metadata?.Name) &&
                        lastMessage.Contains(s.Metadata.Name.ToLowerInvariant()) &&
                        s.Group?.Rid == roomId);

                    if (scene != null)
                    {
                        logger.LogInformation("Scene match for room: {MetadataName}", scene.Metadata?.Name);
                        return (scene.Id, "scene", scene.Metadata?.Name);
                    }
                }

                // Check if groupName matches a zone
                var zoneScene = _zones?.FirstOrDefault(z =>
                    !string.IsNullOrEmpty(z.Metadata?.Name) &&
                    groupName.Contains(z.Metadata.Name, StringComparison.InvariantCultureIgnoreCase));
                if (zoneScene != null)
                {
                    logger.LogInformation("LastUserMessage match on zone for scene: {MetadataName}", zoneScene.Metadata?.Name);

                    var zoneId = zoneScene.Id;
                    logger.LogInformation("Grouped light service found for zone: {ZoneId}", zoneId);

                    var scene = _scenes?.FirstOrDefault(s =>
                        !string.IsNullOrEmpty(s.Metadata?.Name) &&
                        lastMessage.Contains(s.Metadata.Name, StringComparison.InvariantCultureIgnoreCase) &&
                        s.Group?.Rid == zoneId);

                    if (scene != null)
                    {
                        logger.LogInformation("Scene match for zone: {MetadataName}", scene.Metadata?.Name);
                        return (scene.Id, "scene", scene.Metadata?.Name);
                    }
                }
            }
        }

        return (null, null, null);
    }

    public string? TranslateColorNameToHex(string colorName)
    {
        // Try to parse the color name
        try
        {
            var color = System.Drawing.Color.FromName(colorName);

            // Check if the color is valid (KnownColor.Unknown means invalid)
            if (color.ToArgb() != 0)
            {
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
        }
        catch
        {
            logger.LogWarning("Color name '{ColorName}' could not be translated.", colorName);
        }
        return null;
    }

    public async Task SendHueCommandAsync(Guid targetId, string type, bool? state = null, string? color = null, double? brightness = null, string? scene = null)
    {
        if (type == "light")
        {
            var lightCommand = new UpdateLight();
            var updates = new List<string>();

            // Handle state change
            if (state.HasValue)
            {
                lightCommand = state.Value ? lightCommand.TurnOn() : lightCommand.TurnOff();
                updates.Add($"state: {state.Value}");
            }

            // Handle color change
            if (!string.IsNullOrWhiteSpace(color))
            {
                var rgbColor = new RGBColor(color);
                lightCommand = lightCommand.SetColor(rgbColor);
                updates.Add($"color: {color}");
            }

            // Handle brightness change
            if (brightness.HasValue)
            {
                lightCommand = lightCommand.SetBrightness(brightness.Value);
                updates.Add($"brightness: {brightness.Value}");
            }

            if (updates.Any())
            {
                _ = await _hueClient!.UpdateLightAsync(targetId, lightCommand);
                logger.LogInformation("Group/Room '{TargetId}' updated with: {Join}", targetId, string.Join(", ", updates));
            }
            else
            {
                logger.LogInformation("Group/Room '{TargetId}' had no changes.", targetId);
            }
        }
        else if (type is "group" or "room" or "zone")
        {
            // If a scene is specified, activate it
            if (!string.IsNullOrWhiteSpace(scene))
            {
                // Prepare the scene recall request
                var updateScene = new UpdateScene
                {
                    Recall = new Recall { Action = SceneRecallAction.active }
                };

                // Activate the scene
                var result = await _hueClient!.UpdateSceneAsync(targetId, updateScene);

                if (!result.HasErrors)
                {
                    logger.LogInformation("Scene '{Scene}' activated successfully.", scene);
                }
                else
                {
                    logger.LogWarning("Failed to activate scene '{Scene}': {ResultErrors}", scene, result.Errors);
                }
                return;
            }

            var hueCommand = new UpdateGroupedLight();
            var updates = new List<string>();

            // Handle state change
            if (state.HasValue)
            {
                hueCommand = state.Value ? hueCommand.TurnOn() : hueCommand.TurnOff();
                updates.Add($"state: {state.Value}");
            }

            // Handle color change
            if (!string.IsNullOrWhiteSpace(color))
            {
                var rgbColor = new RGBColor(color);
                hueCommand = hueCommand.SetColor(rgbColor);
                updates.Add($"color: {color}");
            }

            // Handle brightness change
            if (brightness.HasValue)
            {
                hueCommand = hueCommand.SetBrightness(brightness.Value);
                updates.Add($"brightness: {brightness.Value}");
            }

            if (updates.Any())
            {
                _ = await _hueClient!.UpdateGroupedLightAsync(targetId, hueCommand);
                logger.LogInformation("Group/Room '{TargetId}' updated with: {Join}", targetId, string.Join(", ", updates));
            }
            else
            {
                logger.LogInformation("Group/Room '{TargetId}' had no changes.", targetId);
            }
        }
        else if (type == "entertainment_configuration")
        {
            var hueCommand = new UpdateEntertainmentConfiguration();
            var updates = new List<string>();

            if (updates.Any())
            {
                _ = await _hueClient!.UpdateEntertainmentConfigurationAsync(targetId, hueCommand);
                logger.LogInformation("Group/Room '{TargetId}' updated with: {Join}", targetId, string.Join(", ", updates));
            }
            else
            {
                logger.LogInformation("Group/Room '{TargetId}' had no changes.", targetId);
            }
        }
        else
        {
            logger.LogWarning("Invalid type '{Type}' provided.", type);
        }
    }

}