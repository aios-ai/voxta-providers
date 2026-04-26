using HueApi.Models;
using Microsoft.Extensions.Logging;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public class HueDataService : IHueDataService
{
    private readonly IHueBridgeConnectionService _connectionService;
    private readonly ILogger<HueDataService> _logger;

    private IList<Light>? _lights;
    private IList<GroupedLight>? _groups;
    private IList<Room>? _rooms;
    private IList<Zone>? _zones;
    private IList<Scene>? _scenes;

    public string? LastUserVisibleError { get; private set; }
    public IList<Light> Lights => _lights ??= new List<Light>();
    public IList<GroupedLight> Groups => _groups ??= new List<GroupedLight>();
    public IList<Room> Rooms => _rooms ??= new List<Room>();
    public IList<Zone> Zones => _zones ??= new List<Zone>();
    public IList<Scene> Scenes => _scenes ??= new List<Scene>();

    public HueDataService(
        IHueBridgeConnectionService connectionService,
        ILogger<HueDataService> logger)
    {
        _connectionService = connectionService;
        _logger = logger;
    }

    public async Task<bool> RetrieveBridgeDataAsync()
    {
        var results = new[]
        {
            await GetLightsAsync(),
            await GetGroupsAsync(),
            await GetRoomsAsync(),
            await GetZonesAsync(),
            await GetScenesAsync()
        };

        return results.All(x => x);
    }

    private async Task<bool> RetrieveDataAsync<T>(Func<Task<HueResponse<T>>> apiCall, Action<IList<T>> setData, string dataType)
    {
        if (_connectionService.HueClient == null)
        {
            _logger.LogWarning("Hue client not initialized. Cannot retrieve {DataType}.", dataType);
            LastUserVisibleError = _connectionService.LastUserVisibleError ?? "Hue is not connected. Please connect the Hue bridge before controlling lights.";
            return false;
        }

        try
        {
            var response = await apiCall();
            var data = response.Data;

            if (data.Count == 0)
            {
                _logger.LogWarning("No {DataType} retrieved.", dataType);
                LastUserVisibleError = $"No Hue {dataType} were found on the bridge.";
                return false;
            }

            setData(data);
            LastUserVisibleError = null;

            _logger.LogInformation("Retrieved {DataCount} {DataType}.", data.Count, dataType);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving {DataType}.", dataType);
            LastUserVisibleError = $"Hue {dataType} could not be loaded. Please check the Hue bridge connection and try again.";
            return false;
        }
    }

    private async Task<bool> GetLightsAsync()
    {
        return await RetrieveDataAsync(
            _connectionService.HueClient!.Light.GetAllAsync,
            data => _lights = data,
            "lights"
        );
    }

    private async Task<bool> GetGroupsAsync()
    {
        return await RetrieveDataAsync(
            _connectionService.HueClient!.GroupedLight.GetAllAsync,
            data => _groups = data,
            "groups"
        );
    }

    private async Task<bool> GetRoomsAsync()
    {
        return await RetrieveDataAsync(
            _connectionService.HueClient!.Room.GetAllAsync,
            data => _rooms = data,
            "rooms"
        );
    }

    private async Task<bool> GetZonesAsync()
    {
        return await RetrieveDataAsync(
            _connectionService.HueClient!.Zone.GetAllAsync,
            data => _zones = data,
            "zones"
        );
    }

    private async Task<bool> GetScenesAsync()
    {
        return await RetrieveDataAsync(
            _connectionService.HueClient!.Scene.GetAllAsync,
            data => _scenes = data,
            "scenes"
        );
    }
}
