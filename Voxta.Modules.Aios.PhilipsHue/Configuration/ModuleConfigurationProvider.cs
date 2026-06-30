using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.Shared.Forms;
using Voxta.Modules.Aios.PhilipsHue.ChatAugmentations;
using System.Text.Json;

namespace Voxta.Modules.Aios.PhilipsHue.Configuration;

public class ModuleConfigurationProvider : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public static string[] FieldsRequiringReload => [BridgeIp.Name, BridgeUsername.Name, Actions.Name];
    private static readonly string DefaultAuthPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voxta",
        "Aios.PhilipsHue",
        "Voxta.Modules.Aios.PhilipsHue.Auth.json");
    
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

    public static readonly FormBooleanField SendInventoryAtSessionStart = new()
    {
        Name = "SendInventoryAtSessionStart",
        Label = "Send Inventory At Session Start",
        Text = "Send the available Hue inventory to chat notes when a session starts. This is sent at most once per session id.",
        DefaultValue = true
    };

    public static readonly PhilipsHueActionSettings[] DefaultActions =
    [
        new()
        {
            Name = "turn_lights_on",
            ShortDescription = "turn on Hue targets",
            Description = "When {{ user }} asks to turn on a Hue light, smart plug, light-group, room or zone.",
            MatchFilter = @"\b(?:turn|switch|set|put)\s+(?:on|up)\b|\b(?:lights?|lamps?|plugs?)\s+on\b|\b\w+(?:\s+\w+){0,3}\s+on\b",
            FlagsFilter = "hueBridge_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "turn_lights_off",
            ShortDescription = "turn off Hue targets",
            Description = "When {{ user }} asks to turn off Hue lights, smart plugs, light-groups, rooms or zones.",
            MatchFilter = @"\b(?:turn|switch|set|shut|power)\s+off\b|\b(?:lights?|lamps?|plugs?)\s+off\b|\b\w+(?:\s+\w+){0,3}\s+off\b",
            FlagsFilter = "hueBridge_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "hueBridge_connect",
            ShortDescription = "anything regarding philips hue light control",
            Description = "When {{ user }} asks to interact with lights in any way, like turning them on or off, change the color, etc.",
            MatchFilter = @"\b(?:hue|philips hue|lights?|lamps?|plugs?|rooms?|zones?|scenes?|brightness|color|colour)\b|\b(?:turn|switch|set|put|shut|power)\s+(?:on|off|up)\b",
            FlagsFilter = "hueBridge_disconnected",
            CancelReply = "true",
        },
        new()
        {
            Name = "change_color",
            ShortDescription = "change color",
            Description = "When {{ user }} asks to change the light color.",
            MatchFilter = @"\b(?:color|colour|hue)\b|\b(?:lights?|lamps?|rooms?|zones?)\s+(?!on|off|up|down|bright|brighter|dark|darker|dim|dimmer|scene)\w+\b|\b(?:make|set|change)\b.*\b(?:lights?|lamps?|rooms?|zones?)\b",
            FlagsFilter = "hueBridge_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "show_emotion",
            ShortDescription = "change color based on emotion or what fits the situation",
            Description = "When {{ char }} wants to show emotions via light color or to set the color based on the situation.",
            MatchFilter = @"\b(?:emotion|mood|feeling|atmosphere|ambience|vibe)\b",
            FlagsFilter = "hueBridge_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "change_brightness",
            ShortDescription = "change brightness or saturation",
            Description = "When {{ user }} asks to change the brightness or saturation.",
            MatchFilter = @"\b(?:brightness|brighten|dim|dimmer|brighter|darken|darker|saturation|intensity|bright|dark)\b",
            FlagsFilter = "hueBridge_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "activate_scene",
            ShortDescription = "activate scene for a specific room, group or zone",
            Description = "When {{ user }} asks to activate a scene for a specific room, group or zone.",
            MatchFilter = @"\b(?:scene|scenes)\b|\b(?:activate|start)\b.*\b(?:rooms?|groups?|zones?)\b|\b(?:activate|start)\b.*\b(?:hue|lights?|lamps?)\b",
            FlagsFilter = "hueBridge_connected",
            CancelReply = "true",
        },
        new()
        {
            Name = "show_hue_inventory",
            ShortDescription = "show available Hue targets",
            Description = "When {{ user }} asks to list available Philips Hue lights, smart plugs, groups, rooms, zones or scenes.",
            MatchFilter = @"\b(?:list|show|what|get|available)\b.*\b(?:hue|lights|lamps|plugs|rooms|zones|scenes)\b",
            FlagsFilter = "hueBridge_connected",
            CancelReply = "true",
        },
    ];

    public static readonly FormArrayOfObjectsField Actions = new()
    {
        Name = "Actions",
        Label = "Actions",
        Text = "Configure Philips Hue actions exposed to action inference.",
        AllowAddRemove = false,
        AllowRename = false,
        AllowDisable = true,
        UniqueFields = [nameof(PhilipsHueActionSettings.Name)],
        FieldTemplate =
        [
            new FormTextField
            {
                Name = nameof(PhilipsHueActionSettings.Name),
                Label = "Name",
                Required = true,
            },
            new FormTextField
            {
                Name = nameof(PhilipsHueActionSettings.ShortDescription),
                Label = "Short Description",
                Required = true,
            },
            new FormMultilineField
            {
                Name = nameof(PhilipsHueActionSettings.Description),
                Label = "Description",
                Required = true,
            },
            new FormMultilineField
            {
                Name = nameof(PhilipsHueActionSettings.MatchFilter),
                Label = "Match Filter",
                Text = "One keyword/regex pattern per line.",
                Required = true,
            },
            new FormTextField
            {
                Name = nameof(PhilipsHueActionSettings.FlagsFilter),
                Label = "Flags Filter",
                Required = false,
            },
            new FormBooleanField
            {
                Name = nameof(PhilipsHueActionSettings.CancelReply),
                Label = "Cancel Reply",
                DefaultValue = true,
            },
        ],
        DefaultValue = JsonSerializer.Serialize(DefaultActions),
    };
    
    public static readonly FormTextField AuthPath = new()
    {
        Name = "AuthPath",
        Label = "Authentication Path",
        Required = true,
        Text = "The path to store the PhilipsHue authentication token.",
        DefaultValue = DefaultAuthPath,
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
           SendInventoryAtSessionStart,
           Actions,
           AuthPath
       );
        return Task.FromResult(fields);
    }
}
