namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public interface IHueCommandService
{
    string? LastUserVisibleError { get; }
    Task<bool> SendHueCommandAsync(Guid targetId, string type, bool? state = null, string? color = null, double? brightness = null, string? scene = null);
    Task<bool> ControlAllLightsAsync(bool turnOn);
}
