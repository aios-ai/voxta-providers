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

    public HueCommandService(
        IHueBridgeConnectionService connectionService,
        IHueDataService dataService,
        ILogger<HueCommandService> logger)
    {
        _connectionService = connectionService;
        _dataService = dataService;
        _logger = logger;
    }

    public async Task SendHueCommandAsync(Guid targetId, string type, bool? state = null, string? color = null, double? brightness = null, string? scene = null)
    {
        if (_connectionService.HueClient == null)
        {
            _logger.LogWarning("Hue client not initialized. Cannot send command.");
            return;
        }

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
                _ = await _connectionService.HueClient.UpdateLightAsync(targetId, lightCommand);
                _logger.LogInformation("Light '{TargetId}' updated with: {Join}", targetId, string.Join(", ", updates));
            }
            else
            {
                _logger.LogInformation("Light '{TargetId}' had no changes.", targetId);
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
                var result = await _connectionService.HueClient.UpdateSceneAsync(targetId, updateScene);

                if (!result.HasErrors)
                {
                    _logger.LogInformation("Scene '{Scene}' activated successfully.", scene);
                }
                else
                {
                    _logger.LogWarning("Failed to activate scene '{Scene}': {ResultErrors}", scene, result.Errors);
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
                _ = await _connectionService.HueClient.UpdateGroupedLightAsync(targetId, hueCommand);
                _logger.LogInformation("Group/Room '{TargetId}' updated with: {Join}", targetId, string.Join(", ", updates));
            }
            else
            {
                _logger.LogInformation("Group/Room '{TargetId}' had no changes.", targetId);
            }
        }
        else if (type == "entertainment_configuration")
        {
            var hueCommand = new UpdateEntertainmentConfiguration();
            var updates = new List<string>();

            if (updates.Any())
            {
                _ = await _connectionService.HueClient.UpdateEntertainmentConfigurationAsync(targetId, hueCommand);
                _logger.LogInformation("Entertainment Configuration '{TargetId}' updated with: {Join}", targetId, string.Join(", ", updates));
            }
            else
            {
                _logger.LogInformation("Entertainment Configuration '{TargetId}' had no changes.", targetId);
            }
        }
        else
        {
            _logger.LogWarning("Invalid type '{Type}' provided.", type);
        }
    }

    public async Task ControlAllLightsAsync(bool turnOn)
    {
        if (_connectionService.HueClient == null)
        {
            _logger.LogWarning("Hue client not initialized. Cannot control all lights.");
            return;
        }

        foreach (var light in _dataService.Lights)
        {
            var lightCommand = turnOn ? new UpdateLight().TurnOn() : new UpdateLight().TurnOff();
            _ = await _connectionService.HueClient.UpdateLightAsync(light.Id, lightCommand);
            _logger.LogInformation("Turned light {State}: {MetadataName}", turnOn ? "on" : "off", light.Metadata?.Name);
        }
    }
}
