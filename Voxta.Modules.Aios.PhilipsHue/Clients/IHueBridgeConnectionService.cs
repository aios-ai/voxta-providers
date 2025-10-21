using HueApi;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public interface IHueBridgeConnectionService
{
    LocalHueApi? HueClient { get; }
    bool IsConnected { get; }

    Task InitializeBridgeAsync(CancellationToken cancellationToken);
}
