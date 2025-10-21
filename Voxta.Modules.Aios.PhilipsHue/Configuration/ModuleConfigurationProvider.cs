using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;

namespace Voxta.Modules.Aios.PhilipsHue.Configuration;

public class ModuleConfigurationProvider : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public static string[] FieldsRequiringReload => [BridgeIp.Name, BridgeUsername.Name];
    
    public static readonly FormTextField BridgeIp = new()
    {
        Name = "BridgeIp",
        Label = "Bridge IP",
        Required = true,
        DefaultValue = "192.168.0.1",
        Text = "IP address of your Philips Hue Bridge",
    };
    
    public static readonly FormTextField BridgeUsername = new()
    {
        Name = "BridgeUsername",
        Label = "Bridge Username",
        Required = true,
        DefaultValue = "user",
        Text = "Username for your Philips Hue Bridge",
    };
    
    public static readonly FormTextField CharacterControlledLight = new()
    {
        Name = "CharacterControlledLight",
        Label = "Character Controlled Light",
        DefaultValue = "",
        Text = "Target light, zone or room name the character can control on it's own (optional)",
    };
    
    public static readonly FormTextField AuthPath = new()
    {
        Name = "AuthPath",
        Label = "Authentication Path",
        Required = true,
        Text = "The path to store the PhilipsHue authentication token.",
        DefaultValue = @"%LOCALAPPDATA%\Voxta\Aios.PhilipsHue\Voxta.Modules.Aios.PhilipsHue.Auth.json",
        Advanced = true,
    };

    
   public Task<FormField[]> GetModuleConfigurationFieldsAsync(
        IAuthenticationContext auth,
        ISettingsSource settings,
        CancellationToken cancellationToken
    )
   {
       var fields = FormBuilder.Build(
           CharacterControlledLight,
           AuthPath
       );
        return Task.FromResult(fields);
    }
}
