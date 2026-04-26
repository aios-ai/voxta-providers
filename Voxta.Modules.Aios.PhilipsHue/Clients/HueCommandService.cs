using HueApi.ColorConverters;
using HueApi.ColorConverters.Original.Extensions;
using HueApi.Models.Requests;
using Microsoft.Extensions.Logging;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public class HueCommandService : IHueCommandService
{
    private readonly IHueBridgeConnectionService _connectionService;
    private readonly IHueDataService _dataService;
    private readonly ILogger<HueCommandService> _logger;

    public string? LastUserVisibleError { get; private set; }

    public HueCommandService(
        IHueBridgeConnectionService connectionService,
        IHueDataService dataService,
        ILogger<HueCommandService> logger)
    {
        _connectionService = connectionService;
        _dataService = dataService;
        _logger = logger;
    }

    public async Task<bool> SendHueCommandAsync(Guid targetId, string type, bool? state = null, string? color = null, double? brightness = null, string? scene = null)
    {
        if (_connectionService.HueClient == null)
        {
            _logger.LogWarning("Hue client not initialized. Cannot send command.");
            LastUserVisibleError = _connectionService.LastUserVisibleError ?? "Hue is not connected. Please connect the Hue bridge before controlling lights.";
            return false;
        }

        try
        {
            if (type == "light")
            {
                var targetLight = _dataService.Lights.FirstOrDefault(x => x.Id == targetId);
                if (targetLight != null && IsSmartPlug(targetLight) && (!string.IsNullOrWhiteSpace(color) || brightness.HasValue))
                {
                    LastUserVisibleError = $"Hue target '{targetLight.Metadata?.Name}' is a smart plug and only supports on/off control.";
                    return false;
                }

                var lightCommand = new UpdateLight();
                var updates = new List<string>();

                if (state.HasValue)
                {
                    lightCommand = state.Value ? lightCommand.TurnOn() : lightCommand.TurnOff();
                    updates.Add($"state: {state.Value}");
                }

                if (!string.IsNullOrWhiteSpace(color))
                {
                    var rgbColor = new RGBColor(color);
                    lightCommand = lightCommand.SetColor(rgbColor);
                    updates.Add($"color: {color}");
                }

                if (brightness.HasValue)
                {
                    lightCommand = lightCommand.SetBrightness(brightness.Value);
                    updates.Add($"brightness: {brightness.Value}");
                }

                if (!updates.Any())
                {
                    _logger.LogInformation("Light '{TargetId}' had no changes.", targetId);
                    LastUserVisibleError = "No Hue light changes were requested.";
                    return false;
                }

                var result = await _connectionService.HueClient.Light.UpdateAsync(targetId, lightCommand);
                if (!result.HasErrors)
                {
                    _logger.LogInformation("Light '{TargetId}' updated with: {Join}", targetId, string.Join(", ", updates));
                    LastUserVisibleError = null;
                    return true;
                }

                _logger.LogWarning("Failed to update light '{TargetId}': {ResultErrors}", targetId, result.Errors);
                LastUserVisibleError = "The Hue bridge rejected the light command. Please check that the light is available.";
                return false;
            }

            if (type is "group" or "room" or "zone")
            {
                if (!string.IsNullOrWhiteSpace(scene))
                {
                    var updateScene = new UpdateScene
                    {
                        Recall = new Recall { Action = SceneRecallAction.active }
                    };

                    var sceneResult = await _connectionService.HueClient.Scene.UpdateAsync(targetId, updateScene);

                    if (!sceneResult.HasErrors)
                    {
                        _logger.LogInformation("Scene '{Scene}' activated successfully.", scene);
                        LastUserVisibleError = null;
                        return true;
                    }

                    _logger.LogWarning("Failed to activate scene '{Scene}': {ResultErrors}", scene, sceneResult.Errors);
                    LastUserVisibleError = $"Hue could not activate the scene {scene}. Please check that the scene is available for that room or zone.";
                    return false;
                }

                var hueCommand = new UpdateGroupedLight();
                var updates = new List<string>();

                if (state.HasValue)
                {
                    hueCommand = state.Value ? hueCommand.TurnOn() : hueCommand.TurnOff();
                    updates.Add($"state: {state.Value}");
                }

                if (!string.IsNullOrWhiteSpace(color))
                {
                    var rgbColor = new RGBColor(color);
                    hueCommand = hueCommand.SetColor(rgbColor);
                    updates.Add($"color: {color}");
                }

                if (brightness.HasValue)
                {
                    hueCommand = hueCommand.SetBrightness(brightness.Value);
                    updates.Add($"brightness: {brightness.Value}");
                }

                if (!updates.Any())
                {
                    _logger.LogInformation("Group/Room '{TargetId}' had no changes.", targetId);
                    LastUserVisibleError = "No Hue group or room changes were requested.";
                    return false;
                }

                var result = await _connectionService.HueClient.GroupedLight.UpdateAsync(targetId, hueCommand);
                if (!result.HasErrors)
                {
                    _logger.LogInformation("Group/Room '{TargetId}' updated with: {Join}", targetId, string.Join(", ", updates));
                    LastUserVisibleError = null;
                    return true;
                }

                _logger.LogWarning("Failed to update group/room '{TargetId}': {ResultErrors}", targetId, result.Errors);
                LastUserVisibleError = "The Hue bridge rejected the group or room command. Please check that the target is available.";
                return false;
            }

            _logger.LogWarning("Invalid type '{Type}' provided.", type);
            LastUserVisibleError = $"Hue target type '{type}' is not supported.";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hue command failed for target '{TargetId}' of type '{Type}'.", targetId, type);
            LastUserVisibleError = "Hue could not complete that command. Please check the bridge and target light, then try again.";
            return false;
        }
    }

    public async Task<bool> ControlAllLightsAsync(bool turnOn)
    {
        if (_connectionService.HueClient == null)
        {
            _logger.LogWarning("Hue client not initialized. Cannot control all lights.");
            LastUserVisibleError = _connectionService.LastUserVisibleError ?? "Hue is not connected. Please connect the Hue bridge before controlling lights.";
            return false;
        }

        var controllableLights = _dataService.Lights
            .Where(x => !IsSmartPlug(x))
            .ToList();

        if (!controllableLights.Any())
        {
            LastUserVisibleError = "No Hue lights are available to control.";
            return false;
        }

        var anySucceeded = false;
        var anyFailed = false;
        foreach (var light in controllableLights)
        {
            try
            {
                var lightCommand = turnOn ? new UpdateLight().TurnOn() : new UpdateLight().TurnOff();
                var result = await _connectionService.HueClient.Light.UpdateAsync(light.Id, lightCommand);
                if (result.HasErrors)
                {
                    anyFailed = true;
                    _logger.LogWarning("Failed to turn light {State}: {MetadataName}. Errors: {ResultErrors}", turnOn ? "on" : "off", light.Metadata?.Name, result.Errors);
                    continue;
                }

                anySucceeded = true;
                _logger.LogInformation("Turned light {State}: {MetadataName}", turnOn ? "on" : "off", light.Metadata?.Name);
            }
            catch (Exception ex)
            {
                anyFailed = true;
                _logger.LogWarning(ex, "Failed to turn light {State}: {MetadataName}", turnOn ? "on" : "off", light.Metadata?.Name);
            }
        }

        if (anySucceeded)
        {
            LastUserVisibleError = anyFailed ? "Some Hue lights could not be controlled. Please check unavailable lights in the Hue app." : null;
            return true;
        }

        LastUserVisibleError = "Hue could not control any lights. Please check that the bridge and lights are available.";
        return false;
    }

    private static bool IsSmartPlug(HueApi.Models.Light light)
    {
        return string.Equals(light.Metadata?.Archetype, "plug", StringComparison.OrdinalIgnoreCase);
    }
}
