using HueApi.Models;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public interface IHueDataService
{
    IList<Light> Lights { get; }
    IList<GroupedLight> Groups { get; }
    IList<Room> Rooms { get; }
    IList<Zone> Zones { get; }
    IList<Scene> Scenes { get; }

    Task RetrieveBridgeDataAsync();
}
