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

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _bridgeConnectionService.InitializeBridgeAsync(cancellationToken);
        if (_bridgeConnectionService.IsConnected)
        {
            await _dataService.RetrieveBridgeDataAsync();
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

    public async Task SendHueCommandAsync(Guid targetId, string type, bool? state = null, string? color = null, double? brightness = null, string? scene = null)
    {
        await _commandService.SendHueCommandAsync(targetId, type, state, color, brightness, scene);
    }

    public async Task ControlAllLightsAsync(bool turnOn)
    {
        await _commandService.ControlAllLightsAsync(turnOn);
    }
}
