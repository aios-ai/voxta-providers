using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Modules.Aios.PhilipsHue.Clients;

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
                Actions = BuildActions()
            }
        ];
    }

    private ScenarioActionDefinition[] BuildActions()
    {
        return philipsHueChatAugmentationsServiceInstance.Actions
            .Where(action => !PhilipsHueChatAugmentationsService.ParseActionBoolean(action.Disabled, false))
            .Select(action => new ScenarioActionDefinition
            {
                Name = action.Name,
                Layer = "HueControl",
                ShortDescription = action.ShortDescription ?? "",
                Description = action.Description ?? "",
                MatchFilter = ParseMatchFilter(action.MatchFilter),
                FlagsFilter = action.FlagsFilter,
                Timing = GetActionTiming(action.Name),
                CancelReply = PhilipsHueChatAugmentationsService.ParseActionBoolean(action.CancelReply, true),
                Arguments = GetActionArguments(action.Name),
            })
            .ToArray();
    }

    private static string[] ParseMatchFilter(string? matchFilter)
    {
        return (matchFilter ?? "")
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static FunctionTiming GetActionTiming(string actionName)
    {
        return string.Equals(actionName, "show_emotion", StringComparison.OrdinalIgnoreCase)
            ? FunctionTiming.AfterAssistantMessage
            : FunctionTiming.AfterUserMessage;
    }

    private static FunctionArgumentDefinition[] GetActionArguments(string actionName)
    {
        return actionName switch
        {
            "turn_lights_on" or "turn_lights_off" =>
            [
                new FunctionArgumentDefinition
                {
                    Name = "target",
                    Type = FunctionArgumentType.String,
                    Required = false,
                    Description =
                        "Name of the light, smart plug, light-group, room or zone the {{ user }} asked to control. If no light, smart plug or room has been named, or user want's to turn on all lights, don't select anything."
                }
            ],
            "change_color" =>
            [
                new FunctionArgumentDefinition
                {
                    Name = "target",
                    Type = FunctionArgumentType.String,
                    Required = true,
                    Description = "Name of the light, light-group, room or zone the {{ user }} asked to control."
                },
                new FunctionArgumentDefinition
                {
                    Name = "color",
                    Type = FunctionArgumentType.String,
                    Required = true,
                    Description = "Name of the color {{ user }} asked to change to as PascalCase color name."
                }
            ],
            "show_emotion" =>
            [
                new FunctionArgumentDefinition
                {
                    Name = "color",
                    Type = FunctionArgumentType.String,
                    Required = true,
                    Description = "PascalCase color name {{ char }} wants to change to."
                }
            ],
            "change_brightness" =>
            [
                new FunctionArgumentDefinition
                {
                    Name = "target",
                    Type = FunctionArgumentType.String,
                    Required = true,
                    Description = "Name of the light, light-group, room or zone the {{ user }} asked to control."
                },
                new FunctionArgumentDefinition
                {
                    Name = "brightness",
                    Type = FunctionArgumentType.String,
                    Required = true,
                    Description = "Value of brightness {{ user }} asked to change to from 1 to 100."
                }
            ],
            "activate_scene" =>
            [
                new FunctionArgumentDefinition
                {
                    Name = "target",
                    Type = FunctionArgumentType.String,
                    Required = true,
                    Description = "Name of the light, light-group, room or zone the {{ user }} asked to control."
                },
                new FunctionArgumentDefinition
                {
                    Name = "scene",
                    Type = FunctionArgumentType.String,
                    Required = true,
                    Description = "Name od the scene {{ user }} asked to activate."
                }
            ],
            _ => [],
        };
    }

    public Task SendHueInventoryNoteAsync(CancellationToken cancellationToken)
    {
        if (!hue.IsConnected)
            return Task.CompletedTask;

        return SendNoteOnly(BuildHueInventoryNote(), cancellationToken);
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

        var cancelReply = IsCancelReplyEnabled(serverActionMessage.Value);
        try
        {
            switch (serverActionMessage.Value)
            {
                case "hueBridge_connect":
                    await SendHueFailureOrDefault("The Hue bridge could not be reached. The user should make sure it is powered on and on the same network.", cancellationToken);
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
                        if (await hue.ControlAllLightsAsync(true))
                            await SendMessage("All Hue lights were turned on.", cancelReply, cancellationToken);
                        else
                            await SendHueFailureOrDefault("The lights could not be turned on.", cancellationToken);
                        return true;
                    }

                    logger.LogInformation("Target '{MatchedNameOn}' matched to {TypeOn} with ID '{TargetIdOn}'.", matchedNameOn, typeOn, targetIdOn);

                    if (await hue.SendHueCommandAsync((Guid)targetIdOn, typeOn, state: true))
                        await SendMessage($"Hue turned on {matchedNameOn ?? targetOnName}.", cancelReply, cancellationToken);
                    else
                        await SendHueFailureOrDefault($"The light '{matchedNameOn ?? targetOnName}' could not be turned on.", cancellationToken);
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
                        if (await hue.ControlAllLightsAsync(false))
                            await SendMessage("All Hue lights were turned off.", cancelReply, cancellationToken);
                        else
                            await SendHueFailureOrDefault("The lights could not be turned off.", cancellationToken);
                        return true;
                    }

                    logger.LogInformation("Target '{MatchedNameOff}' matched to {TypeOff} with ID '{TargetIdOff}'.", matchedNameOff, typeOff, targetIdOff);

                    if (await hue.SendHueCommandAsync((Guid)targetIdOff, typeOff, state: false))
                        await SendMessage($"Hue turned off {matchedNameOff ?? targetOffName}.", cancelReply, cancellationToken);
                    else
                        await SendHueFailureOrDefault($"The light '{matchedNameOff ?? targetOffName}' could not be turned off.", cancellationToken);
                    return true;
                case "change_color":
                    if (!serverActionMessage.TryGetArgument("target", out var targetNameColor) ||
                        string.IsNullOrEmpty(targetNameColor))
                    {
                        await SendHueFailureOrDefault("The requested color change could not be completed because no light, room, or zone was specified.", cancellationToken);
                        return true;
                    }

                    targetNameColor = CleanString(targetNameColor!);

                    var (targetIdColor, typeColor, matchedNameColor) = hue.MatchTargetToId(targetNameColor);

                    if (targetIdColor == null || typeColor == null)
                    {
                        logger.LogWarning("No matching target found for '{TargetNameColor}'.", targetNameColor);
                        hue.SetNoActiveTarget($"No Hue light, room, or zone matched '{targetNameColor}'.");
                        await SendHueFailureOrDefault($"No Hue light, room, or zone matched '{targetNameColor}'.", cancellationToken);
                        return true;
                    }

                    logger.LogInformation("Target '{MatchedNameColor}' matched to {TypeColor} with ID '{TargetIdColor}'.", matchedNameColor, typeColor, targetIdColor);

                    if (!serverActionMessage.TryGetArgument("color", out var colorName) || string.IsNullOrEmpty(colorName))
                    {
                        await SendHueFailureOrDefault($"The color of '{matchedNameColor ?? targetNameColor}' could not be changed because no color was specified.", cancellationToken);
                        return true;
                    }

                    colorName = CleanStringPascalCase(colorName);
                    logger.LogInformation("PascalCase Color: {ColorName}", colorName);

                    var hexCode = hue.TranslateColorNameToHex(colorName);
                    logger.LogInformation("Hex: {HexCode}", hexCode);

                    if (string.IsNullOrWhiteSpace(hexCode))
                    {
                        await SendHueFailureOrDefault($"The color of '{matchedNameColor ?? targetNameColor}' could not be changed because Hue does not know the color '{colorName}'.", cancellationToken);
                        return true;
                    }

                    if (await hue.SendHueCommandAsync((Guid)targetIdColor, typeColor, state: true, color: hexCode))
                        await SendMessage($"Hue changed {matchedNameColor ?? targetNameColor} to {colorName}.", cancelReply, cancellationToken);
                    else
                        await SendHueFailureOrDefault($"The color of '{matchedNameColor ?? targetNameColor}' could not be changed to {colorName}.", cancellationToken);
                    return true;
                case "change_brightness":
                    if (!serverActionMessage.TryGetArgument("target", out var targetNameBrightness) ||
                        string.IsNullOrEmpty(targetNameBrightness))
                    {
                        await SendHueFailureOrDefault("The brightness could not be changed because no light, room, or zone was specified.", cancellationToken);
                        return true;
                    }

                    targetNameBrightness = CleanString(targetNameBrightness!);

                    var (targetIdBrightness, typeBrightness, matchedNameBrightness) = hue.MatchTargetToId(targetNameBrightness);

                    if (targetIdBrightness == null || typeBrightness == null)
                    {
                        logger.LogWarning("No matching target found for '{TargetNameBrightness}'.", targetNameBrightness);
                        hue.SetNoActiveTarget($"No Hue light, room, or zone matched '{targetNameBrightness}'.");
                        await SendHueFailureOrDefault($"No Hue light, room, or zone matched '{targetNameBrightness}'.", cancellationToken);
                        return true;
                    }

                    if (!serverActionMessage.TryGetArgument("brightness", out var brightness) ||
                        !int.TryParse(brightness, out var brightnessLevel))
                        brightnessLevel = 100;

                    logger.LogInformation(
                        "Target '{MatchedNameBrightness}' matched to {TypeBrightness} with ID '{TargetIdBrightness}'.", matchedNameBrightness, typeBrightness, targetIdBrightness);

                    if (await hue.SendHueCommandAsync((Guid)targetIdBrightness, typeBrightness, state: true,
                            brightness: brightnessLevel))
                        await SendMessage($"Hue changed the brightness of {matchedNameBrightness} to {brightnessLevel}.", cancelReply, cancellationToken);
                    else
                        await SendHueFailureOrDefault($"The brightness of '{matchedNameBrightness}' could not be changed.", cancellationToken);
                    return true;
                case "activate_scene":
                    if (!serverActionMessage.TryGetArgument("target", out var targetNameScene) ||
                        string.IsNullOrEmpty(targetNameScene))
                    {
                        await SendHueFailureOrDefault("The scene could not be activated because no room or zone was specified.", cancellationToken);
                        return true;
                    }

                    targetNameScene = CleanString(targetNameScene!);

                    var (targetIdTarget, typeSceneTarget, matchedNameSceneTarget) = hue.MatchTargetToId(targetNameScene);

                    if (targetIdTarget == null || typeSceneTarget == null)
                    {
                        logger.LogWarning("No matching target found for '{TargetNameScene}'.", targetNameScene);
                        hue.SetNoActiveTarget($"No Hue room, group, or zone matched '{targetNameScene}'.");
                        await SendHueFailureOrDefault($"No Hue room, group, or zone matched '{targetNameScene}'.", cancellationToken);
                        return true;
                    }

                    logger.LogInformation(
                        "Target '{MatchedNameSceneTarget}' matched to {TypeSceneTarget} with ID '{TargetIdTarget}'.", matchedNameSceneTarget, typeSceneTarget, targetIdTarget);

                    if (!serverActionMessage.TryGetArgument("scene", out var sceneName) || string.IsNullOrEmpty(sceneName))
                    {
                        await SendHueFailureOrDefault($"No scene was specified for {matchedNameSceneTarget}.", cancellationToken);
                        return true;
                    }

                    sceneName = CleanString(sceneName!);

                    var (targetIdScene, typeScene, matchedNameScene) = hue.MatchTargetToId(sceneName, matchedNameSceneTarget);

                    if (targetIdScene == null || typeScene == null)
                    {
                        logger.LogWarning("No matching scene found for '{SceneName}'.", sceneName);
                        hue.SetNoActiveTarget($"No Hue scene named '{sceneName}' was found for {matchedNameSceneTarget}.");
                        await SendHueFailureOrDefault($"No Hue scene named '{sceneName}' was found for {matchedNameSceneTarget}.", cancellationToken);
                        return true;
                    }

                    logger.LogInformation("Scene '{MatchedNameScene}' matched to {TypeScene} with ID '{TargetIdScene}'.", matchedNameScene, typeScene, targetIdScene);

                    if (await hue.SendHueCommandAsync((Guid)targetIdScene, typeSceneTarget, state: true, scene: matchedNameScene))
                        await SendMessage($"Hue activated scene {matchedNameScene} for {matchedNameSceneTarget}.", cancelReply, cancellationToken);
                    else
                        await SendHueFailureOrDefault($"The scene '{matchedNameScene}' could not be activated for {matchedNameSceneTarget}.", cancellationToken);
                    return true;
                case "show_emotion":
                    if (!serverActionMessage.TryGetArgument("color", out var colorEmotion) ||
                        string.IsNullOrEmpty(colorEmotion))
                    {
                        await SendHueFailureOrDefault("The character-controlled light could not be changed because no color was specified.", cancellationToken);
                        return true;
                    }

                    colorEmotion = CleanStringPascalCase(colorEmotion);

                    var hexCodeEmotion = hue.TranslateColorNameToHex(colorEmotion);
                    logger.LogInformation("Hex {HexCodeEmotion}", hexCodeEmotion);

                    var (targetIdEmotion, typeEmotion, matchedNameEmotion) =
                        hue.MatchTargetToId(philipsHueChatAugmentationsServiceInstance.CharacterControlledLight);

                    logger.LogInformation(
                        "Target '{MatchedNameEmotion}' matched to {TypeEmotion} with ID '{TargetIdEmotion}'.", matchedNameEmotion, typeEmotion, targetIdEmotion);

                    if (targetIdEmotion == null || typeEmotion == null)
                    {
                        hue.SetNoActiveTarget("No Hue target is configured for character-controlled lighting.");
                        await SendHueFailureOrDefault("No Hue target is configured for character-controlled lighting.", cancellationToken);
                        return true;
                    }

                    if (string.IsNullOrWhiteSpace(hexCodeEmotion))
                    {
                        await SendHueFailureOrDefault($"The character-controlled light could not be changed because Hue does not know the color '{colorEmotion}'.", cancellationToken);
                        return true;
                    }

                    if (await hue.SendHueCommandAsync((Guid)targetIdEmotion, typeEmotion, state: true, color: hexCodeEmotion))
                        await SendMessage($"Hue changed {matchedNameEmotion} to {colorEmotion}.", cancelReply, cancellationToken);
                    else
                        await SendHueFailureOrDefault($"The character-controlled light '{matchedNameEmotion}' could not be changed to {colorEmotion}.", cancellationToken);
                    return true;
                case "show_hue_inventory":
                    await SendMessage(BuildHueInventoryNote(), cancelReply, cancellationToken);
                    return true;
                default:
                    return false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hue action '{Action}' failed unexpectedly.", serverActionMessage.Value);
            await SendHueFailureOrDefault("The Hue request could not be completed. The user should check the bridge and try again.", cancellationToken);
            return true;
        }
    }

    private bool IsCancelReplyEnabled(string actionName)
    {
        return philipsHueChatAugmentationsServiceInstance.Actions
            .FirstOrDefault(action => string.Equals(action.Name, actionName, StringComparison.OrdinalIgnoreCase))
            ?.CancelReply is { } value
            ? PhilipsHueChatAugmentationsService.ParseActionBoolean(value, true)
            : true;
    }

    private Task SendMessage(string message, bool cancelReply, CancellationToken cancellationToken)
    {
        return cancelReply
            ? session.SendNoteAsync(message, cancellationToken)
            : session.SendSecretAsync(message, cancellationToken);
    }

    private Task SendNoteOnly(string note, CancellationToken cancellationToken)
    {
        return session.SendNoteAsync(note, cancellationToken);
    }

    private async Task SendHueFailureOrDefault(string fallbackMessage, CancellationToken cancellationToken)
    {
        await session.SendSecretAsync(hue.LastUserVisibleError ?? fallbackMessage, cancellationToken);
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

    private string BuildHueInventoryNote()
    {
        var lights = hue.GetLights()
            .Where(x => !string.IsNullOrWhiteSpace(x.Metadata?.Name))
            .OrderBy(x => x.Metadata!.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var rooms = hue.GetRooms()
            .Where(x => !string.IsNullOrWhiteSpace(x.Metadata?.Name))
            .OrderBy(x => x.Metadata!.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var zones = hue.GetZones()
            .Where(x => !string.IsNullOrWhiteSpace(x.Metadata?.Name))
            .OrderBy(x => x.Metadata!.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var groups = hue.GetGroups()
            .Where(x => !string.IsNullOrWhiteSpace(x.Metadata?.Name))
            .OrderBy(x => x.Metadata!.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var scenes = hue.GetScenes()
            .Where(x => !string.IsNullOrWhiteSpace(x.Metadata?.Name))
            .Select(x => x.Metadata!.Name!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var smartPlugs = lights
            .Where(IsSmartPlug)
            .ToList();
        var controllableLights = lights
            .Where(x => !IsSmartPlug(x))
            .ToList();

        var representedGroupIds = rooms
            .Select(GetGroupedLightId)
            .Concat(zones.Select(GetGroupedLightId))
            .OfType<Guid>()
            .ToHashSet();

        var builder = new StringBuilder("Philips Hue objects available in this chat:");

        if (rooms.Any())
        {
            AppendSectionHeader(builder, "Rooms");
            foreach (var room in rooms)
                builder.Append("- ").AppendLine(room.Metadata!.Name);
        }

        if (zones.Any())
        {
            AppendSectionHeader(builder, "Zones");
            foreach (var zone in zones)
                builder.Append("- ").AppendLine(zone.Metadata!.Name);
        }

        var visibleGroups = groups.Where(x => !representedGroupIds.Contains(x.Id)).ToList();
        if (visibleGroups.Any())
        {
            AppendSectionHeader(builder, "Light groups");
            foreach (var group in visibleGroups)
                builder.Append("- ").AppendLine(group.Metadata!.Name);
        }

        if (controllableLights.Any())
        {
            AppendSectionHeader(builder, "Lights");
            foreach (var light in controllableLights)
                builder.Append("- ").AppendLine(light.Metadata!.Name);
        }

        if (smartPlugs.Any())
        {
            AppendSectionHeader(builder, "Smart plugs");
            foreach (var smartPlug in smartPlugs)
                builder.Append("- ").AppendLine(smartPlug.Metadata!.Name);
        }

        if (scenes.Any())
        {
            AppendSectionHeader(builder, "Scenes");
            foreach (var scene in scenes)
                builder.Append("- ").AppendLine(scene);
        }

        var duplicateNames = rooms.Select(x => x.Metadata!.Name)
            .Concat(zones.Select(x => x.Metadata!.Name))
            .Concat(visibleGroups.Select(x => x.Metadata!.Name))
            .Concat(lights.Select(x => x.Metadata!.Name))
            .GroupBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (duplicateNames.Any())
            builder.AppendLine().Append("Duplicate names: ").AppendLine(string.Join(", ", duplicateNames));

        return builder.ToString().TrimEnd();
    }

    private static void AppendSectionHeader(StringBuilder builder, string header)
    {
        builder.AppendLine().AppendLine(header);
    }

    private static bool IsSmartPlug(HueApi.Models.Light light)
    {
        return string.Equals(light.Metadata?.Archetype, "plug", StringComparison.OrdinalIgnoreCase);
    }

    private static Guid? GetGroupedLightId(HueApi.Models.Room room)
    {
        return room.Services?.FirstOrDefault(x => x.Rtype == "grouped_light")?.Rid
               ?? room.GroupedServices?.FirstOrDefault(x => x.Rtype == "grouped_light")?.Rid;
    }

    private static Guid? GetGroupedLightId(HueApi.Models.Zone zone)
    {
        return zone.Services?.FirstOrDefault(x => x.Rtype == "grouped_light")?.Rid;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
