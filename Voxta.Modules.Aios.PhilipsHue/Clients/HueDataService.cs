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

    public async Task RetrieveBridgeDataAsync()
    {
        await GetLightsAsync();
        await GetGroupsAsync();
        await GetRoomsAsync();
        await GetZonesAsync();
        await GetScenesAsync();
    }

    private async Task RetrieveDataAsync<T>(Func<Task<HueResponse<T>>> apiCall, Action<IList<T>> setData, string dataType)
    {
        if (_connectionService.HueClient == null)
        {
            _logger.LogWarning("Hue client not initialized. Cannot retrieve {DataType}.", dataType);
            return;
        }

        try
        {
            var response = await apiCall();
            var data = response.Data;

            if (data.Count == 0)
            {
                _logger.LogWarning("No {DataType} retrieved.", dataType);
                return;
            }

            setData(data);

            _logger.LogInformation("Retrieved {DataCount} {DataType}.", data.Count, dataType);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error retrieving {DataType}: {ExMessage}", dataType, ex.Message);
        }
    }

    private async Task GetLightsAsync()
    {
        await RetrieveDataAsync(
            _connectionService.HueClient!.GetLightsAsync,
            data => _lights = data,
            "lights"
        );
    }

    private async Task GetGroupsAsync()
    {
        await RetrieveDataAsync(
            _connectionService.HueClient!.GetGroupedLightsAsync,
            data => _groups = data,
            "groups"
        );
    }

    private async Task GetRoomsAsync()
    {
        await RetrieveDataAsync(
            _connectionService.HueClient!.GetRoomsAsync,
            data => _rooms = data,
            "rooms"
        );
    }

    private async Task GetZonesAsync()
    {
        await RetrieveDataAsync(
            _connectionService.HueClient!.GetZonesAsync,
            data => _zones = data,
            "zones"
        );
    }

    private async Task GetScenesAsync()
    {
        await RetrieveDataAsync(
            _connectionService.HueClient!.GetScenesAsync,
            data => _scenes = data,
            "scenes"
        );
    }
}
