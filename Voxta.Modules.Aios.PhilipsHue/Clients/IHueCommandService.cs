namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public interface IHueCommandService
{
    Task SendHueCommandAsync(Guid targetId, string type, bool? state = null, string? color = null, double? brightness = null, string? scene = null);
    Task ControlAllLightsAsync(bool turnOn);
}
