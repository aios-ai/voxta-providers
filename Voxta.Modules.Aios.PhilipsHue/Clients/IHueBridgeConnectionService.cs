using HueApi;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public interface IHueBridgeConnectionService
{
    LocalHueApi? HueClient { get; }
    bool IsConnected { get; }
    bool IsAuthorizationRequired { get; }
    HueBridgeState State { get; }
    string? LastUserVisibleError { get; }

    Task<bool> InitializeBridgeAsync(CancellationToken cancellationToken);
}
