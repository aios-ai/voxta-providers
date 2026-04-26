using HueApi.Models;
using Microsoft.Extensions.Logging;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public class HueManager
{
    private readonly IHueBridgeConnectionService _bridgeConnectionService;
    private readonly IHueDataService _dataService;
    private readonly IHueCommandService _commandService;
    private readonly IHueEntityMatchingService _entityMatchingService;
    private readonly IColorConverterService _colorConverterService;
    private readonly ILogger<HueManager> _logger;

    public string? LastUserMessage { get; set; }
    public bool IsConnected => _bridgeConnectionService.IsConnected;
    public bool IsAuthorizationRequired => _bridgeConnectionService.IsAuthorizationRequired;
    public HueBridgeState State { get; private set; } = HueBridgeState.Disconnected;
    public string? LastUserVisibleError { get; private set; }

    public HueManager(
        IHueBridgeConnectionService bridgeConnectionService,
        IHueDataService dataService,
        IHueCommandService commandService,
        IHueEntityMatchingService entityMatchingService,
        IColorConverterService colorConverterService,
        ILogger<HueManager> logger)
    {
        _bridgeConnectionService = bridgeConnectionService;
        _dataService = dataService;
        _commandService = commandService;
        _entityMatchingService = entityMatchingService;
        _colorConverterService = colorConverterService;
        _logger = logger;
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connected = await _bridgeConnectionService.InitializeBridgeAsync(cancellationToken);
            State = _bridgeConnectionService.State;
            LastUserVisibleError = _bridgeConnectionService.LastUserVisibleError;

            if (!connected)
                return false;

            var dataLoaded = await _dataService.RetrieveBridgeDataAsync();
            LastUserVisibleError = _dataService.LastUserVisibleError;
            if (!dataLoaded)
            {
                State = HueBridgeState.Unavailable;
                return false;
            }

            State = HueBridgeState.Connected;
            LastUserVisibleError = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hue initialization failed unexpectedly.");
            State = HueBridgeState.Unavailable;
            LastUserVisibleError = "Hue could not initialize. Please check the bridge connection and try again.";
            return false;
        }
    }

    public (Guid? id, string? type, string? matchedName) MatchTargetToId(string? target, string? groupName = null)
    {
        return _entityMatchingService.MatchTargetToId(target, groupName, LastUserMessage);
    }

    public string? TranslateColorNameToHex(string colorName)
    {
        return _colorConverterService.TranslateColorNameToHex(colorName);
    }

    public async Task<bool> SendHueCommandAsync(Guid targetId, string type, bool? state = null, string? color = null, double? brightness = null, string? scene = null)
    {
        var succeeded = await _commandService.SendHueCommandAsync(targetId, type, state, color, brightness, scene);
        LastUserVisibleError = _commandService.LastUserVisibleError ?? _bridgeConnectionService.LastUserVisibleError;
        State = succeeded ? HueBridgeState.Connected : _bridgeConnectionService.State;
        return succeeded;
    }

    public async Task<bool> ControlAllLightsAsync(bool turnOn)
    {
        var succeeded = await _commandService.ControlAllLightsAsync(turnOn);
        LastUserVisibleError = _commandService.LastUserVisibleError ?? _bridgeConnectionService.LastUserVisibleError;
        State = succeeded ? HueBridgeState.Connected : _bridgeConnectionService.State;
        return succeeded;
    }

    public void SetNoActiveTarget(string message)
    {
        State = HueBridgeState.NoActiveTarget;
        LastUserVisibleError = message;
    }

    public IList<Light> GetLights() => _dataService.Lights;
    public IList<GroupedLight> GetGroups() => _dataService.Groups;
    public IList<Room> GetRooms() => _dataService.Rooms;
    public IList<Zone> GetZones() => _dataService.Zones;
    public IList<Scene> GetScenes() => _dataService.Scenes;
}
