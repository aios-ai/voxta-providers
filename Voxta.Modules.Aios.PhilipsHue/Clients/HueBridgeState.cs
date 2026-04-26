namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public enum HueBridgeState
{
    Disconnected,
    Connected,
    AuthRequired,
    Unavailable,
    MissingConfiguration,
    NoActiveTarget
}
