using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Modules.Aios.PhilipsHue.Clients;
using System.Text.Json;

namespace Voxta.Modules.Aios.PhilipsHue.ChatAugmentations;

public class PhilipsHueChatAugmentationsServiceInstance(
    IChatSessionChatAugmentationApi session,
    HueManager hue,
    PhilipsHueChatAugmentationsSettings philipsHueChatAugmentationsServiceInstance,
    ILogger<PhilipsHueChatAugmentationsServiceInstance> logger
) : IActionInferenceAugmentation, IChatPreProcessAugmentation
{
    public ServiceTypes[] GetRequiredServiceTypes() => [ServiceTypes.ActionInference];
    public string[] GetAugmentationNames() => [VoxtaModule.AugmentationKey];

    public ValueTask<string> PreProcessTextAsync(ChatMessageRole role, string text, CancellationToken cancellationToken)
    {
        if (role == ChatMessageRole.User)
            hue.LastUserMessage = text;
        return ValueTask.FromResult(text);
    }

    public IEnumerable<ClientUpdateContextMessage> RegisterChatContext()
    {
        return
        [
            new ClientUpdateContextMessage
            {
                ContextKey = VoxtaModule.ServiceName,
                SessionId = session.SessionId,
                SetFlags = hue.IsConnected ? ["hueBridge_connected", "!hueBridge_disconnected"] : ["hueBridge_disconnected", "!hueBridge_connected"],
                Actions =
                [
                    new()
                    {
                        Name = "turn_lights_on",
                        Layer = "HueControl",
                        ShortDescription = "turn on lights",
                        Description = "When {{ user }} asks to turn on a light, a light-group, room or zone.",
                        /*Effect = new ActionEffect
                        {
                            Secret = "{{ char }} turned on the light.",
                        },*/
                        //MatchFilter = [@"\b(?:toggle|change|set|activate|light|lights|room|zone)\b"],
                        FlagsFilter = "hueBridge_connected",
                        Timing = FunctionTiming.AfterUserMessage,
                        CancelReply = true,
                        Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "target",
                                Type = FunctionArgumentType.String,
                                Required = false,
                                Description =
                                    "Name of the light, light-group, room or zone the {{ user }} asked to control. If no light or room has been named, or user want's to turn on all lights, don't select anything."
                            }
                        ]
                    },

                    new()
                    {
                        Name = "turn_lights_off",
                        Layer = "HueControl",
                        ShortDescription = "turn off lights",
                        Description = "When {{ user }} asks to turn off the lights.",
                        //MatchFilter = [@"\b(?:toggle|change|set|activate|light|lights|room|zone)\b"],
                        FlagsFilter = "hueBridge_connected",
                        Timing = FunctionTiming.AfterUserMessage,
                        CancelReply = true,
                        Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "target",
                                Type = FunctionArgumentType.String,
                                Required = false,
                                Description =
                                    "Name of the light, light-group, room or zone the {{ user }} asked to control. If no light or room has been named, or user want's to turn off all lights, don't select anything."
                            }
                        ]
                    },
                    new()
                    {
                        Name = "hueBridge_connect",
                        Layer = "HueControl",
                        ShortDescription = "anything regarding philips hue light control",
                        Description =
                            "When {{ user }} asks to interact with lights in any way, like turning them on or off, change the color, etc.",
                        FlagsFilter = "hueBridge_disconnected",
                        Timing = FunctionTiming.AfterUserMessage,
                        CancelReply = true,
                    },
                    new()
                    {
                        Name = "change_color",
                        Layer = "HueControl",
                        ShortDescription = "change color",
                        Description = "When {{ user }} asks to change the light color.",
                        //MatchFilter = [@"\b(?:toggle|change|set|activate|light|lights|room|zone|color|colour)\b"],
                        FlagsFilter = "hueBridge_connected",
                        Timing = FunctionTiming.AfterUserMessage,
                        CancelReply = true,
                        Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "target",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description =
                                    "Name of the light, light-group, room or zone the {{ user }} asked to control."
                            },
                            new FunctionArgumentDefinition
                            {
                                Name = "color",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description =
                                    "Name of the color {{ user }} asked to change to as PascalCase color name."
                            }
                        ]
                    },
                    new()
                    {
                        Name = "show_emotion",
                        Layer = "HueControl",
                        ShortDescription = "change color based on emotion or what fits the situation",
                        Description =
                            "When {{ char }} wants to show emotions via light color or to set the color based on the situation.",
                        FlagsFilter = "hueBridge_connected",
                        Timing = FunctionTiming.AfterAssistantMessage,
                        CancelReply = true,
                        Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "color",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description = "PascalCase color name {{ char }} wants to change to."
                            }
                        ],
                        Disabled = philipsHueChatAugmentationsServiceInstance.CharacterControlledLight == null
                    },
                    new()
                    {
                        Name = "change_brightness",
                        Layer = "HueControl",
                        ShortDescription = "change brightness or saturation",
                        Description = "When {{ user }} asks to change the brightness or saturation.",
                        //MatchFilter = [@"\b(?:toggle|change|set|activate|light|lights|room|zone|brightness)\b"],
                        FlagsFilter = "hueBridge_connected",
                        Timing = FunctionTiming.AfterUserMessage,
                        CancelReply = true,
                        Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "target",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description =
                                    "Name of the light, light-group, room or zone the {{ user }} asked to control."
                            },
                            new FunctionArgumentDefinition
                            {
                                Name = "brightness",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description = "Value of brightness {{ user }} asked to change to from 1 to 100."
                            }
                        ]
                    },
                    new()
                    {
                        Name = "activate_scene",
                        Layer = "HueControl",
                        ShortDescription = "activate scene for a specific room, group or zone",
                        Description = "When {{ user }} asks to activate a scene for a specific room, group or zone.",
                        //MatchFilter = [@"\b(?:change|set|activate|light|lights|room|zone|scene)\b"],

                        FlagsFilter = "hueBridge_connected",
                        Timing = FunctionTiming.AfterUserMessage,
                        CancelReply = true,
                        Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "target",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description =
                                    "Name of the light, light-group, room or zone the {{ user }} asked to control."
                            },
                            new FunctionArgumentDefinition
                            {
                                Name = "scene",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description = "Name od the scene {{ user }} asked to activate."
                            }
                        ]
                    },
                    new()
                    {
                        Name = "show_available_types",
                        Layer = "HueControl",
                        ShortDescription = "show available lights, groups, rooms, zones or scenes",
                        Description = "When {{ user }} asks to list available lights, groups, rooms, zones or scenes.",
                        FlagsFilter = "hueBridge_connected",
                        Timing = FunctionTiming.AfterUserMessage,
                        CancelReply = true,
                        Arguments =
                        [
                            new FunctionArgumentDefinition
                            {
                                Name = "type",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description = "The type of objects to list, e.g., 'lights', 'groups', 'rooms', 'zones', or 'scenes'."
                            }
                        ]
                    }
                ]
            }
        ];
    }

    public async ValueTask<bool> TryHandleActionInference(
        ChatMessageData? message,
        ServerActionMessage serverActionMessage,
        CancellationToken cancellationToken
    )
    {
        if (serverActionMessage.ContextKey != VoxtaModule.ServiceName)
            return false;
        if (serverActionMessage.Role != ChatMessageRole.User && serverActionMessage.Role != ChatMessageRole.Assistant)
            return false;

        switch (serverActionMessage.Value)
        {
            case "hueBridge_connect":
                await SendMessage("No connection to the Hue bridge could be made. Ensure you are on the same network and try again.", cancellationToken);
                return true;
            case "turn_lights_on":
                if (!serverActionMessage.TryGetArgument("target", out var targetOnName) ||
                    string.IsNullOrEmpty(targetOnName))
                {
                    targetOnName = null;
                }

                targetOnName = CleanString(targetOnName);

                var (targetIdOn, typeOn, matchedNameOn) = hue.MatchTargetToId(targetOnName);

                if (targetIdOn == null || typeOn == null)
                {
                    logger.LogInformation($"No matching target found, turning on all lights.");
                    await hue.ControlAllLightsAsync(true);

                    await SendMessage($"/note {{{{ char }}}} turned on all lights", cancellationToken);
                    return true;
                }

                logger.LogInformation("Target '{MatchedNameOn}' matched to {TypeOn} with ID '{TargetIdOn}'.", matchedNameOn, typeOn, targetIdOn);

                await hue.SendHueCommandAsync((Guid)targetIdOn, typeOn, state: true);
                await SendMessage($"/note {{{{ char }}}} turned on the light {targetOnName}", cancellationToken);
                return true;
            case "turn_lights_off":
                if (!serverActionMessage.TryGetArgument("target", out var targetOffName) ||
                    string.IsNullOrEmpty(targetOffName))
                {
                    targetOffName = null;
                }

                targetOffName = CleanString(targetOffName!);

                var (targetIdOff, typeOff, matchedNameOff) = hue.MatchTargetToId(targetOffName);

                if (targetIdOff == null || typeOff == null)
                {
                    logger.LogInformation($"No matching target found, turning off all lights.");
                    await hue.ControlAllLightsAsync(false);

                    await SendMessage($"/note {{{{ char }}}} turned off all lights", cancellationToken);
                    return true;
                }

                logger.LogInformation("Target '{MatchedNameOff}' matched to {TypeOff} with ID '{TargetIdOff}'.", matchedNameOff, typeOff, targetIdOff);

                await hue.SendHueCommandAsync((Guid)targetIdOff, typeOff, state: false);
                await SendMessage($"/note {{{{ char }}}} turned off the light {targetOffName}", cancellationToken);
                return true;
            case "change_color":
                if (!serverActionMessage.TryGetArgument("target", out var targetNameColor) ||
                    string.IsNullOrEmpty(targetNameColor))
                {
                    await SendMessage("/event No target specified.", cancellationToken);
                    targetNameColor = null;
                }

                targetNameColor = CleanString(targetNameColor!);

                var (targetIdColor, typeColor, matchedNameColor) = hue.MatchTargetToId(targetNameColor);

                if (targetIdColor == null || typeColor == null)
                {
                    logger.LogWarning("No matching target found for '{TargetNameColor}'.", targetNameColor);
                    await SendMessage($"/event No matching target found for '{targetNameColor}'.", cancellationToken);
                    return true;
                }

                logger.LogInformation("Target '{MatchedNameColor}' matched to {TypeColor} with ID '{TargetIdColor}'.", matchedNameColor, typeColor, targetIdColor);

                if (!serverActionMessage.TryGetArgument("color", out var colorName) || string.IsNullOrEmpty(colorName))
                {
                    await SendMessage("/event No Color specified.", cancellationToken);
                    return true;
                }

                colorName = CleanStringPascalCase(colorName);
                logger.LogInformation("PascalCase Color: {ColorName}", colorName);

                var hexCode = hue.TranslateColorNameToHex(colorName);
                logger.LogInformation("Hex: {HexCode}", hexCode);

                await hue.SendHueCommandAsync((Guid)targetIdColor, typeColor, state: true, color: hexCode);

                await SendMessage($"/note {{{{ char }}}} changed the light color of {targetNameColor} to {colorName}", cancellationToken);
                return true;
            case "change_brightness":
                if (!serverActionMessage.TryGetArgument("target", out var targetNameBrightness) ||
                    string.IsNullOrEmpty(targetNameBrightness))
                {
                    await SendMessage("/event No target specified.", cancellationToken);
                    targetNameBrightness = null;
                }

                targetNameBrightness = CleanString(targetNameBrightness!);

                var (targetIdBrightness, typeBrightness, matchedNameBrightness) = hue.MatchTargetToId(targetNameBrightness);

                if (targetIdBrightness == null || typeBrightness == null)
                {
                    logger.LogWarning("No matching target found for '{TargetNameBrightness}'.", targetNameBrightness);
                    await SendMessage($"/event No matching target found for '{targetNameBrightness}'.", cancellationToken);
                    return true;
                }

                if (!serverActionMessage.TryGetArgument("brightness", out var brightness) ||
                    !int.TryParse(brightness, out var brightnessLevel))
                    brightnessLevel = 100;

                logger.LogInformation(
                    "Target '{MatchedNameBrightness}' matched to {TypeBrightness} with ID '{TargetIdBrightness}'.", matchedNameBrightness, typeBrightness, targetIdBrightness);

                await hue.SendHueCommandAsync((Guid)targetIdBrightness, typeBrightness, state: true,
                    brightness: brightnessLevel);

                await SendMessage(
                    $"/note {{{{ char }}}} changed the brightness of {matchedNameBrightness} to {brightnessLevel}", cancellationToken);
                return true;
            case "activate_scene":
                if (!serverActionMessage.TryGetArgument("target", out var targetNameScene) ||
                    string.IsNullOrEmpty(targetNameScene))
                {
                    await SendMessage("/event No target specified.", cancellationToken);
                    targetNameScene = null;
                }

                targetNameScene = CleanString(targetNameScene!);

                var (targetIdTarget, typeSceneTarget, matchedNameSceneTarget) = hue.MatchTargetToId(targetNameScene);

                if (targetIdTarget == null || typeSceneTarget == null)
                {
                    logger.LogWarning("No matching target found for '{TargetNameScene}'.", targetNameScene);
                    await SendMessage($"/event No matching target found for '{targetNameScene}'.", cancellationToken);
                    return true;
                }

                logger.LogInformation(
                    "Target '{MatchedNameSceneTarget}' matched to {TypeSceneTarget} with ID '{TargetIdTarget}'.", matchedNameSceneTarget, typeSceneTarget, targetIdTarget);

                if (!serverActionMessage.TryGetArgument("scene", out var sceneName) || string.IsNullOrEmpty(sceneName))
                {
                    await SendMessage("/event No scene specified.", cancellationToken);
                    sceneName = null;
                }

                sceneName = CleanString(sceneName!);

                var (targetIdScene, typeScene, matchedNameScene) = hue.MatchTargetToId(sceneName, matchedNameSceneTarget);

                if (targetIdScene == null || typeScene == null)
                {
                    logger.LogWarning("No matching scene found for '{SceneName}'.", sceneName);
                    await SendMessage($"/event No matching target found for '{sceneName}'.", cancellationToken);
                    return true;
                }

                logger.LogInformation("Scene '{MatchedNameScene}' matched to {TypeScene} with ID '{TargetIdScene}'.", matchedNameScene, typeScene, targetIdScene);

                await hue.SendHueCommandAsync((Guid)targetIdScene, typeSceneTarget, state: true, scene: matchedNameScene);

                await SendMessage($"/note {{{{ char }}}} activated scene {matchedNameScene} for {matchedNameSceneTarget}", cancellationToken);
                return true;
            case "show_emotion":
                if (!serverActionMessage.TryGetArgument("color", out var colorEmotion) ||
                    string.IsNullOrEmpty(colorEmotion))
                {
                    await SendMessage("/event No Color specified.", cancellationToken);
                    return true;
                }

                colorEmotion = CleanStringPascalCase(colorEmotion);

                var hexCodeEmotion = hue.TranslateColorNameToHex(colorEmotion);
                logger.LogInformation("Hex {HexCodeEmotion}", hexCodeEmotion);

                var (targetIdEmotion, typeEmotion, matchedNameEmotion) =
                    hue.MatchTargetToId(philipsHueChatAugmentationsServiceInstance.CharacterControlledLight);

                logger.LogInformation(
                    "Target '{MatchedNameEmotion}' matched to {TypeEmotion} with ID '{TargetIdEmotion}'.", matchedNameEmotion, typeEmotion, targetIdEmotion);

                await hue.SendHueCommandAsync((Guid)targetIdEmotion!, typeEmotion!, state: true, color: hexCodeEmotion);

                await SendMessage($"/note {{{{ char }}}} changed the light color of {matchedNameEmotion} to {colorEmotion}", cancellationToken);
                return true;
            case "show_available_types":
                if (!serverActionMessage.TryGetArgument("type", out var typeName) || string.IsNullOrEmpty(typeName))
                {
                    await SendMessage("/event No type specified. Please specify 'lights', 'groups', 'rooms', 'zones', or 'scenes'.", cancellationToken);
                    return true;
                }

                typeName = CleanString(typeName!).ToLowerInvariant();
                object? itemToSerialize = null;
                string responseHeader = "";

                switch (typeName)
                {
                    case "lights":
                        itemToSerialize = hue.GetLights().FirstOrDefault();
                        responseHeader = "First available light object:";
                        break;
                    case "groups":
                        itemToSerialize = hue.GetGroups().FirstOrDefault();
                        responseHeader = "First available group object:";
                        break;
                    case "rooms":
                        itemToSerialize = hue.GetRooms().FirstOrDefault();
                        responseHeader = "First available room object:";
                        break;
                    case "zones":
                        itemToSerialize = hue.GetZones().FirstOrDefault();
                        responseHeader = "First available zone object:";
                        break;
                    case "scenes":
                        itemToSerialize = hue.GetScenes().FirstOrDefault();
                        responseHeader = "First available scene object:";
                        break;
                    default:
                        await SendMessage($"/event Unknown type '{typeName}'. Please specify 'lights', 'groups', 'rooms', 'zones', or 'scenes'.", cancellationToken);
                        return true;
                }

                if (itemToSerialize != null)
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(itemToSerialize, options);
                    await SendMessage($"/note {responseHeader}\n```json\n{json}\n```", cancellationToken);
                }
                else
                {
                    await SendMessage($"/note No {typeName} found in your network.", cancellationToken);
                }
                return true;
            default:
                return false;
        }
    }

    private async Task SendMessage(string message, CancellationToken cancellationToken)
    {
        await session.SendSecretAsync(message, cancellationToken);
        await session.TriggerReplyAsync(cancellationToken);
    }

    private static string CleanString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove unwanted characters, preserving alphanumerics and spaces
        input = Regex.Replace(input, @"[^\w\säöüÄÖÜß]", "");

        // Normalize whitespace to a single space
        input = Regex.Replace(input, @"\s+", " ").Trim();

        return input;
    }

    private static string CleanStringPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove unwanted characters
        input = Regex.Replace(input, @"[^a-zA-Z0-9\säöüÄÖÜß]", " ");

        // Normalize whitespace to a single space and trim leading/trailing spaces
        input = Regex.Replace(input, @"\s+", " ").Trim();

        // Convert to PascalCase (capitalize each word and remove spaces)
        return CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(input.ToLower())
            .Replace(" ", "");
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}