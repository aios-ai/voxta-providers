using Microsoft.Extensions.Logging;
using Voxta.Model.Shared;
using Voxta.Model.WebsocketMessages.ClientMessages;
using Voxta.Model.WebsocketMessages.ServerMessages;
using Voxta.Providers.Host;
using System.Text.RegularExpressions;
using System.Text.Json;
using HueApi;
using HueApi.BridgeLocator;
using HueApi.Models;
using HueApi.Models.Clip;
using HueApi.Models.Exceptions;
using HueApi.Models.Requests;
//using HueApi.Models.Responses;
//using HueApi.Models.Sensors;
//using HueApi.Extensions;
using HueApi.ColorConverters;
using HueApi.ColorConverters.Original.Extensions;
using System.Globalization;

namespace Voxta.SampleProviderApp.Providers
{
    public class UserFunctionProviderHue(
        IRemoteChatSession session,
        ILogger<UserFunctionProviderHue> logger
    ) : ProviderBase(session, logger)
    {
        public class HueConfig
        {
            public string? CharacterControlledLight { get; set; }
        }
        private CancellationTokenSource? _cancellationTokenSource;
        private const int MaxRetries = 20;
        private const int RetryIntervalSeconds = 5;
        private readonly string AppKeyFile = Path.Combine(Directory.GetCurrentDirectory(), "Providers\\configs\\UserFunctionProviderHueAuth.json");
        private string? LastUserMessage = null;
        private LocalHueApi? _hueClient;
        private string? _bridgeIp;
        private HueConfig? _hueConfig = new HueConfig();
        private IList<Light>? _lights;
        private IList<GroupedLight>? _groups;
        private IList<Room>? _rooms;
        private IList<Zone>? _zones;
        private IList<Scene>? _scenes;
        //private IList<EntertainmentConfiguration>? _entertainmentGroups;

        protected override async Task OnStartAsync()
        {
            await base.OnStartAsync();

            _cancellationTokenSource = new CancellationTokenSource();

            LoadConfiguration();

            UpdateClientContext(["hueBridge_disconnected", "!hueBridge_connected"], []);
            InitializeBridgeAsync().Wait();

            HandleMessage(ChatMessageRole.User, RemoteChatMessageTiming.Generated, OnUserChatMessage);

            HandleMessage<ServerActionMessage>(async message =>
                {
                    if (message.Role != ChatMessageRole.User && message.Role != ChatMessageRole.Assistant) return;

                    switch (message.Value)
                    {
                        case "hueBridge_connect":
                            SendMessagePrefix("No connection to the Hue bridge could be made. Ensure you are on the same network and try again. ");
                            break;
                        case "turn_lights_on":
                            if (!message.TryGetArgument("target", out var targetOnName) || string.IsNullOrEmpty(targetOnName))
                            {
                                targetOnName = null;
                            }
                            targetOnName = CleanString(targetOnName!);

                            var (targetIdOn, typeOn, matchedNameOn) = MatchTargetToId(targetOnName);

                            if (targetIdOn == null || typeOn == null)
                            {
                                Logger.LogInformation($"No matching target found, turning on all lights.");
                                foreach (var light in _lights!)
                                {
                                    var lightCommand = new UpdateLight().TurnOn();
                                    _ = await _hueClient!.UpdateLightAsync(light.Id, lightCommand);
                                    Logger.LogInformation($"Turned light on: {light.Metadata?.Name}");
                                }
                                SendMessage($"/note {{{{ char }}}} turned on all lights");
                                return;
                            }

                            Logger.LogInformation($"Target '{matchedNameOn}' matched to {typeOn} with ID '{targetIdOn}'.");

                            await SendHueCommandAsync((Guid)targetIdOn, typeOn, state: true);
                            SendMessage($"/note {{{{ char }}}} turned on the light {targetOnName}");
                            break;
                        case "turn_lights_off":
                            if (!message.TryGetArgument("target", out var targetOffName) || string.IsNullOrEmpty(targetOffName))
                            {
                                targetOffName = null;
                            }
                            targetOffName = CleanString(targetOffName!);

                            var (targetIdOff, typeOff, matchedNameOff) = MatchTargetToId(targetOffName);

                            if (targetIdOff == null || typeOff == null)
                            {
                                Logger.LogInformation($"No matching target found, turning off all lights.");
                                foreach (var light in _lights!)
                                {
                                    var lightCommand = new UpdateLight().TurnOff();
                                    _ = await _hueClient!.UpdateLightAsync(light.Id, lightCommand);
                                    Logger.LogInformation($"Turned light on: {light.Metadata?.Name}");
                                }
                                SendMessage($"/note {{{{ char }}}} turned off all lights");
                                return;
                            }

                            Logger.LogInformation($"Target '{matchedNameOff}' matched to {typeOff} with ID '{targetIdOff}'.");

                            await SendHueCommandAsync((Guid)targetIdOff, typeOff, state: false);
                            SendMessage($"/note {{{{ char }}}} turned off the light {targetOffName}");
                            break;
                        case "change_color":
                            if (!message.TryGetArgument("target", out var targetNameColor) || string.IsNullOrEmpty(targetNameColor))
                            {
                                SendMessage("/event No target specified.");
                                targetNameColor = null;
                            }
                            targetNameColor = CleanString(targetNameColor!);

                            var (targetIdColor, typeColor, matchedNameColor) = MatchTargetToId(targetNameColor);

                            if (targetIdColor == null || typeColor == null)
                            {
                                Logger.LogWarning($"No matching target found for '{targetNameColor}'.");
                                SendMessage($"/event No matching target found for '{targetNameColor}'.");
                                return;
                            }

                            Logger.LogInformation($"Target '{matchedNameColor}' matched to {typeColor} with ID '{targetIdColor}'.");

                            if (!message.TryGetArgument("color", out var colorName) || string.IsNullOrEmpty(colorName))
                            {
                                SendMessage("/event No Color specified.");
                                break;
                            }
                            colorName = CleanStringPascalCase(colorName);
                            Logger.LogInformation($"PascalCase Color: {colorName}");

                            var hexCode = TranslateColorNameToHex(colorName);
                            Logger.LogInformation($"Hex: {hexCode}");

                            await SendHueCommandAsync((Guid)targetIdColor, typeColor, state: true, color: hexCode);

                            SendMessage($"/note {{{{ char }}}} changed the light color of {targetNameColor} to {colorName}");
                            break;
                        case "change_brightness":
                            if (!message.TryGetArgument("target", out var targetNameBrightness) || string.IsNullOrEmpty(targetNameBrightness))
                            {
                                SendMessage("/event No target specified.");
                                targetNameBrightness = null;
                            }
                            targetNameBrightness = CleanString(targetNameBrightness!);

                            var (targetIdBrightness, typeBrightness, matchedNameBrightness) = MatchTargetToId(targetNameBrightness);

                            if (targetIdBrightness == null || typeBrightness == null)
                            {
                                Logger.LogWarning($"No matching target found for '{targetNameBrightness}'.");
                                SendMessage($"/event No matching target found for '{targetNameBrightness}'.");
                                return;
                            }

                            if (!message.TryGetArgument("brightness", out var brightness) || !int.TryParse(brightness, out var brightnessLevel))
                                brightnessLevel = 100;

                            Logger.LogInformation($"Target '{matchedNameBrightness}' matched to {typeBrightness} with ID '{targetIdBrightness}'.");

                            await SendHueCommandAsync((Guid)targetIdBrightness, typeBrightness, state: true, brightness: brightnessLevel);

                            SendMessage($"/note {{{{ char }}}} changed the brightness of {matchedNameBrightness} to {brightnessLevel}");
                            break;
                        case "activate_scene":
                            if (!message.TryGetArgument("target", out var targetNameScene) || string.IsNullOrEmpty(targetNameScene))
                            {
                                SendMessage("/event No target specified.");
                                targetNameScene = null;
                            }
                            targetNameScene = CleanString(targetNameScene!);

                            var (targetIdTarget, typeSceneTarget, matchedNameSceneTarget) = MatchTargetToId(targetNameScene);

                            if (targetIdTarget == null || typeSceneTarget == null)
                            {
                                Logger.LogWarning($"No matching target found for '{targetNameScene}'.");
                                SendMessage($"/event No matching target found for '{targetNameScene}'.");
                                return;
                            }

                            Logger.LogInformation($"Target '{matchedNameSceneTarget}' matched to {typeSceneTarget} with ID '{targetIdTarget}'.");

                            if (!message.TryGetArgument("scene", out var SceneName) || string.IsNullOrEmpty(SceneName))
                            {
                                SendMessage("/event No scene specified.");
                                SceneName = null;
                            }
                            SceneName = CleanString(SceneName!);

                            var (targetIdScene, typeScene, matchedNameScene) = MatchTargetToId(SceneName, matchedNameSceneTarget);

                            if (targetIdScene == null || typeScene == null)
                            {
                                Logger.LogWarning($"No matching scene found for '{SceneName}'.");
                                SendMessage($"/event No matching target found for '{SceneName}'.");
                                return;
                            }

                            Logger.LogInformation($"Scene '{matchedNameScene}' matched to {typeScene} with ID '{targetIdScene}'.");

                            await SendHueCommandAsync((Guid)targetIdScene, typeSceneTarget, state: true, scene: matchedNameScene);

                            SendMessage($"/note {{{{ char }}}} activated scene {matchedNameScene} for {matchedNameSceneTarget}");
                            break;
                        case "show_emotion":
                            if (!message.TryGetArgument("color", out var colorEmotion) || string.IsNullOrEmpty(colorEmotion))
                            {
                                SendMessage("/event No Color specified.");
                                break;
                            }
                            colorEmotion = CleanStringPascalCase(colorEmotion);

                            var hexCodeEmotion = TranslateColorNameToHex(colorEmotion);
                            Logger.LogInformation($"Hex {hexCodeEmotion}");

                            var (targetIdEmotion, typeEmotion, matchedNameEmotion) = MatchTargetToId(_hueConfig?.CharacterControlledLight);

                            Logger.LogInformation($"Target '{matchedNameEmotion}' matched to {typeEmotion} with ID '{targetIdEmotion}'.");

                            await SendHueCommandAsync((Guid)targetIdEmotion!, typeEmotion!, state: true, color: hexCodeEmotion);

                            SendMessage($"/note {{{{ char }}}} changed the light color of {matchedNameEmotion} to {colorEmotion}");
                            break;
                    }
                });
        }

        private void OnUserChatMessage(RemoteChatMessage message)
        {
            LastUserMessage = message.Text;
        }

        protected override async Task OnStopAsync()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            await base.OnStopAsync();
        }

        //////////////////////////////////////////////////////////////////////////////////
        // Load config json
        //////////////////////////////////////////////////////////////////////////////////

        private void LoadConfiguration()
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Providers\\configs\\UserFunctionProviderHueConfig.json");

            if (!File.Exists(configPath))
            {
                logger.LogError("Configuration file not found at {ConfigPath}", configPath);
                _hueConfig = new HueConfig();
                return;
            }

            try
            {
                var json = File.ReadAllText(configPath);
                _hueConfig = JsonSerializer.Deserialize<HueConfig>(json);

                if (_hueConfig?.CharacterControlledLight == null || string.IsNullOrWhiteSpace(_hueConfig.CharacterControlledLight))
                {
                    _hueConfig!.CharacterControlledLight = null;
                    logger.LogInformation("Hue configuration loaded with default value for CharacterControlledLight.");
                }
                else
                {
                    logger.LogInformation("Hue configuration loaded successfully.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error loading configuration: {Message}", ex.Message);
                _hueConfig = new HueConfig();
            }
        }

        //////////////////////////////////////////////////////////////////////////////////
        // Bridge discovery & Connect
        //////////////////////////////////////////////////////////////////////////////////
        public async Task InitializeBridgeAsync()
        {
            Logger.LogInformation("Initializing Hue Bridge...");

            if (File.Exists(AppKeyFile))
            {
                Logger.LogInformation("Authentication file found. Attempting to connect using saved configuration...");
                var config = LoadAppKey();

                if (config != null && !string.IsNullOrEmpty(config.Ip) && !string.IsNullOrEmpty(config.Username))
                {
                    _bridgeIp = config.Ip;
                    try
                    {
                        _hueClient = new LocalHueApi(_bridgeIp, config.Username);
                        Logger.LogInformation("Connected to Hue bridge using saved configuration.");
                        await RetrieveBridgeDataAsync();
                        UpdateClientContext(["hueBridge_connected", "!hueBridge_disconnected"], []);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to connect to the Hue bridge using saved configuration. Falling back to discovery...");
                    }
                }
            }
            else
            {
                Logger.LogInformation("No Authentication file found. Starting bridge discovery...");
            }

            await DiscoverAndConnectBridgeAsync();
        }

        private async Task DiscoverAndConnectBridgeAsync()
        {
            int retryCount = 0;
            Logger.LogInformation("Discovering Hue bridge...");

            while (retryCount < MaxRetries)
            {
                //Basic Bridge Discovery options:
                var bridgeLocator = new HttpBridgeLocator();
                // Or:
                //var bridgeLocator = new LocalNetworkScanBridgeLocator();
                //var bridgeLocator = new MdnsBridgeLocator();
                //var bridgeLocator = new MUdpBasedBridgeLocator();
                var bridges = await bridgeLocator.LocateBridgesAsync(TimeSpan.FromSeconds(RetryIntervalSeconds));

                //Advanced Bridge Discovery options:
                //var bridges = await HueBridgeDiscovery.CompleteDiscoveryAsync(TimeSpan.FromSeconds(RetryIntervalSeconds), TimeSpan.FromSeconds(30));
                //var bridges = await HueBridgeDiscovery.FastDiscoveryWithNetworkScanFallbackAsync(TimeSpan.FromSeconds(RetryIntervalSeconds), TimeSpan.FromSeconds(30));

                if (bridges.Any())
                {
                    var bridgeInfo = bridges.First();
                    _bridgeIp = bridgeInfo.IpAddress;
                    Logger.LogInformation($"Bridge discovered: {_bridgeIp}");
                    await ConnectBridgeAsync();
                    return;
                }

                Logger.LogWarning("No Hue bridges found. Retrying...");
                retryCount++;
                await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds));
            }

            Logger.LogWarning("Max retries reached. No Hue bridges found.");
        }

        private async Task ConnectBridgeAsync()
        {
            if (string.IsNullOrEmpty(_bridgeIp))
            {
                Logger.LogWarning("No bridge discovered.");
                return;
            }

            var savedAppKey = LoadAppKey();
            if (savedAppKey != null)
            {
                try
                {
                    _hueClient = new LocalHueApi(_bridgeIp, savedAppKey.Username);
                    Logger.LogInformation("Connected to Hue bridge using saved app key...");
                    await RetrieveBridgeDataAsync();
                    UpdateClientContext(["hueBridge_connected", "!hueBridge_disconnected"], []);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to connect to the Hue bridge using saved app key.");
                    return;
                }
            }

            Logger.LogInformation("Please press the link button on your Hue bridge...");
            await AttemptBridgeRegistrationAsync();
        }

        private async Task AttemptBridgeRegistrationAsync()
        {
            var registrationSuccessful = false;
            var retryDelay = TimeSpan.FromSeconds(RetryIntervalSeconds);
            var retries = 0;

            while (!registrationSuccessful && retries < MaxRetries)
            {
                try
                {
                    var appKey = await LocalHueApi.RegisterAsync(_bridgeIp!, "SampleProviderApp", Environment.MachineName, false);
                    await SaveAppKey(appKey);
                    Logger.LogInformation("Bridge connected and app key saved.");
                    registrationSuccessful = true;
                    await ConnectBridgeAsync();
                }
                catch (LinkButtonNotPressedException)
                {
                    retries++;
                    Logger.LogWarning($"Link button not pressed. Attempt {retries} of {MaxRetries}. Retrying in {retryDelay.Seconds} seconds. Please press the button and wait...");
                    await Task.Delay(retryDelay);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to connect to the Hue bridge. Please try again.");
                    return;
                }
            }
        }

        private async Task SaveAppKey(RegisterEntertainmentResult? appKey)
        {
            if (appKey == null)
            {
                Logger.LogError("AppKey is null. Cannot save to file.");
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(appKey);
                await File.WriteAllTextAsync(AppKeyFile, json);
                Logger.LogInformation("App key saved to file.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to save app key to file: {Message}", ex.Message);
                throw;
            }
        }

        private RegisterEntertainmentResult? LoadAppKey()
        {
            if (File.Exists(AppKeyFile))
            {
                return JsonSerializer.Deserialize<RegisterEntertainmentResult>(File.ReadAllText(AppKeyFile));
            }
            return null;
        }

        //////////////////////////////////////////////////////////////////////////////////
        // Get lights, groups, rooms & scenes
        //////////////////////////////////////////////////////////////////////////////////
        private async Task RetrieveBridgeDataAsync()
        {
            await GetLightsAsync();
            await GetGroupsAsync();
            await GetRoomsAsync();
            await GetZonesAsync();
            await GetScenesAsync();
            //await GetEntertainmentAsync();
        }
        private async Task RetrieveDataAsync<T>(Func<Task<HueResponse<T>>> apiCall, Action<IList<T>> setData, string dataType)

        {
            if (_hueClient == null)
            {
                Logger.LogWarning("Hue client not initialized.");
                return;
            }

            try
            {
                var response = await apiCall();
                var data = response.Data;

                if (data == null || !data.Any())
                {
                    Logger.LogWarning($"No {dataType} retrieved.");
                    return;
                }

                setData(data);

                Logger.LogInformation($"Retrieved {data?.Count ?? 0} {dataType}.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error retrieving {dataType}: {ex.Message}");
            }
        }

        private async Task GetLightsAsync()
        {
            await RetrieveDataAsync(
                _hueClient!.GetLightsAsync,
                data =>
                {
                    //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    //Logger.LogInformation($"Serialized lights: {json}");
                    _lights = data;
                    foreach (var light in data)
                    {
                        Logger.LogInformation($"Light: {light.Metadata?.Name}");
                    }
                },
                "lights"
            );
        }

        private async Task GetGroupsAsync()
        {
            await RetrieveDataAsync(
                _hueClient!.GetGroupedLightsAsync,
                data =>
                {
                    //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    //Logger.LogInformation($"Serialized groups: {json}");
                    _groups = data;
                    foreach (var group in data)
                    {
                        //Logger.LogInformation($"Group: {group.Metadata?.Name}");
                    }
                },
                "groups"
            );
        }

        private async Task GetRoomsAsync()
        {
            await RetrieveDataAsync(
                _hueClient!.GetRoomsAsync,
                data =>
                {
                    //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    //Logger.LogInformation($"Serialized rooms: {json}");
                    _rooms = data;
                    foreach (var room in data)
                    {
                        Logger.LogInformation($"Room: {room.Metadata?.Name}");
                    }
                },
                "rooms"
            );
        }

        private async Task GetZonesAsync()
        {
            await RetrieveDataAsync(
                _hueClient!.GetZonesAsync,
                data =>
                {
                    //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    //Logger.LogInformation($"Serialized zones: {json}");
                    _zones = data;
                    foreach (var scene in data)
                    {
                        Logger.LogInformation($"Zone: {scene.Metadata?.Name}");
                    }
                },
                "zones"
            );
        }
        private async Task GetScenesAsync()
        {
            await RetrieveDataAsync(
                _hueClient!.GetScenesAsync,
                data =>
                {
                    //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    //Logger.LogInformation($"Serialized scenes: {json}");
                    _scenes = data;
                    foreach (var scene in data)
                    {
                        var sceneName = scene.Metadata?.Name ?? "Unknown";

                        if (scene.Group != null)
                        {
                            string groupName = "Unknown";
                            if (scene.Group.Rtype == "zone")
                            {
                                var zone = _zones!.FirstOrDefault(z => z.Id == scene.Group.Rid);
                                groupName = zone?.Metadata?.Name ?? "Unknown Zone";
                            }
                            else if (scene.Group.Rtype == "room")
                            {
                                var room = _rooms!.FirstOrDefault(r => r.Id == scene.Group.Rid);
                                groupName = room?.Metadata?.Name ?? "Unknown Room";
                            }

                            sceneName = $"{sceneName} ({groupName})";
                        }

                        Logger.LogInformation($"Scene: {sceneName}");
                    }
                },
                "scenes"
            );
        }

        /*private async Task GetEntertainmentAsync()
        {
            await RetrieveDataAsync(
                _hueClient!.GetEntertainmentConfigurationsAsync,
                data =>
                {
                    //var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    //Logger.LogInformation($"Serialized EntertainmentGroups: {json}");
                    _entertainmentGroups = data;
                    foreach (var scene in data)
                    {
                        Logger.LogInformation($"EntertainmentGroups: {scene.Metadata?.Name}");
                    }
                },
                "entertainmentGroups"
            );
        }*/

        //////////////////////////////////////////////////////////////////////////////////
        // Send Message
        //////////////////////////////////////////////////////////////////////////////////
        private void SendMessage(string message)
        {
            Send(new ClientSendMessage
            {
                SessionId = SessionId,
                DoUserActionInference = false,
                Text = message
            });
        }

        private void SendMessagePrefix(string message)
        {
            Send(new ClientSendMessage
            {
                SessionId = SessionId,
                DoUserActionInference = false,
                CharacterResponsePrefix = message
            });
        }

        //////////////////////////////////////////////////////////////////////////////////
        // Update Client Context Actions
        //////////////////////////////////////////////////////////////////////////////////
        private void UpdateClientContext(string[] flags, string[] contexts)
        {
            // Register our action
            Send(new ClientUpdateContextMessage
            {
                SessionId = SessionId,
                ContextKey = "Lights",
                Contexts = contexts.Select(context => new ContextDefinition { Text = context }).ToArray(),
                SetFlags = flags,
                Actions =
                [
                    // Example action
                    new()
                    {
                        // The name used by the LLM to select the action. Make sure to select a clear name.
                        Name = "turn_lights_on",
                        // Layers allow you to run your actions separately from the scene
                        Layer = "HueControl",
                        // A short description of the action to be included in the functions list, typically used for character action inference
                        ShortDescription = "turn on lights",
                        // The condition for executing this function
                        Description = "When {{ user }} asks to turn on a light, a light-group, room or zone.",
                        /*Effect = new ActionEffect
                        {
                            Secret = "{{ char }} turned on the light.",
                        },*/
                        // This match will ensure user action inference is only going to be triggered if this regex matches the message.
                        // For example, if you use "please" in all functions, this can avoid running user action inference at all unless
                        // the user said "please".
                        //MatchFilter = [@"\b(?:toggle|change|set|activate|light|lights|room|zone)\b"],
                        // Only available when the specific flag is set
                        FlagsFilter = "hueBridge_connected",
                        // Only run in response to the user messages 
                        Timing = FunctionTiming.AfterUserMessage,
                        // Do not generate a response, we will instead handle the action ourselves
                        CancelReply = true,
                        Arguments =
                            [
                                new FunctionArgumentDefinition
                                {
                                    Name = "target",
                                    Type = FunctionArgumentType.String,
                                    Required = false,
                                    Description = "Name of the light, light-group, room or zone the {{ user }} asked to control. If no light or room has been named, or user want's to turn on all lights, don't select anything."
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
                                    Description = "Name of the light, light-group, room or zone the {{ user }} asked to control. If no light or room has been named, or user want's to turn off all lights, don't select anything."
                                }
                            ]
                    },
                new()
                {
                    Name = "hueBridge_connect",
                    Layer = "HueControl",
                    ShortDescription = "anything regarding philips hue light control",
                    Description = "When {{ user }} asks to interact with lights in any way, like turning them on or off, change the color, etc.",
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
                    //MatchFilter = [@"\b(?:change|set|activate|light|lights|room|zone|color|colour)\b"],
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
                                Description = "Name of the light, light-group, room or zone the {{ user }} asked to control."
                            },
                            new FunctionArgumentDefinition
                            {
                                Name = "color",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description = "Name of the color {{ user }} asked to change to as PascalCase color name."
                            }
                        ]
                },
                new()
                {
                    Name = "show_emotion",
                    Layer = "HueControl",
                    ShortDescription = "change color based on emotion or what fits the situation",
                    Description = "When {{ char }} wants to show emotions via light color or to set the color based on the situation.",
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
                    Disabled = _hueConfig?.CharacterControlledLight == null
                },
                new()
                {
                    Name = "change_brightness",
                    Layer = "HueControl",
                    ShortDescription = "change brightness or saturation",
                    Description = "When {{ user }} asks to change the brightness or saturation.",
                    //MatchFilter = [@"\b(?:change|set|activate|light|lights|room|zone|brightness)\b"],
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
                                Description = "Name of the light, light-group, room or zone the {{ user }} asked to control."
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
                                Description = "Name of the light, light-group, room or zone the {{ user }} asked to control."
                            },
                            new FunctionArgumentDefinition
                            {
                                Name = "scene",
                                Type = FunctionArgumentType.String,
                                Required = true,
                                Description = "Name od the scene {{ user }} asked to activate."
                            }
                        ]
                }
                ]
            });
        }

        //////////////////////////////////////////////////////////////////////////////////
        // Match objects
        //////////////////////////////////////////////////////////////////////////////////
        private (Guid? id, string? type, string? matchedName) MatchTargetToId(string? target, string? groupName = null)
        {
            // Normalize the input
            target = target?.ToLowerInvariant();

            // Direct matching by target name
            if (!string.IsNullOrEmpty(target) && string.IsNullOrEmpty(groupName))
            {
                // Check Lights
                var light = _lights?.FirstOrDefault(l => l.Metadata?.Name?.ToLowerInvariant() == target);
                if (light != null)
                    return (light.Id, "light", target);

                // Check Groups
                var group = _groups?.FirstOrDefault(g => g.Metadata?.Name?.ToLowerInvariant() == target);
                if (group != null)
                    return (group.Id, "group", target);

                // Check Rooms
                var room = _rooms?.FirstOrDefault(r => r.Metadata?.Name?.ToLowerInvariant() == target);
                if (room != null)
                {
                    var groupedLightService = room.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                        return (groupedLightService.Rid, "group", target);
                }

                // Check Zones
                var zone = _zones?.FirstOrDefault(z => z.Metadata?.Name?.ToLowerInvariant() == target);
                if (zone != null)
                {
                    var groupedLightService = zone.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                        return (groupedLightService.Rid, "group", target);
                }

                // Check Entertainment Groups
                /*var entertainmentGroup = _entertainmentGroups?.FirstOrDefault(e => e.Metadata?.Name?.ToLowerInvariant() == target);
                if (entertainmentGroup != null)
                    return (entertainmentGroup.Id, "entertainment_configuration", target);*/
            }

            // Check Scenes for the specific room
            if (!string.IsNullOrEmpty(groupName))
            {
                // Check if groupName matches a group
                var groupScene = _groups?.FirstOrDefault(g => g.Metadata?.Name?.ToLowerInvariant() == groupName.ToLowerInvariant());
                if (groupScene != null)
                {
                    var scene = _scenes?.FirstOrDefault(s =>
                        s.Metadata?.Name?.ToLowerInvariant() == target &&
                        s.Group?.Rid == groupScene.Id);

                    if (scene != null)
                        return (scene.Id, "scene", target);
                }

                // Check if groupName matches a room
                var roomScene = _rooms?.FirstOrDefault(r => r.Metadata?.Name?.ToLowerInvariant() == groupName.ToLowerInvariant());
                if (roomScene != null)
                {
                    var groupedLightService = roomScene.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                    {
                        var scene = _scenes?.FirstOrDefault(s =>
                            s.Metadata?.Name?.ToLowerInvariant() == target &&
                            s.Group?.Rid == groupedLightService.Rid);

                        if (scene != null)
                            return (scene.Id, "scene", target);
                    }
                }

                // Check if groupName matches a zone
                var zoneScene = _zones?.FirstOrDefault(z => z.Metadata?.Name?.ToLowerInvariant() == groupName.ToLowerInvariant());
                if (zoneScene != null)
                {
                    var groupedLightService = zoneScene.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                    {
                        var scene = _scenes?.FirstOrDefault(s =>
                            s.Metadata?.Name?.ToLowerInvariant() == target &&
                            s.Group?.Rid == groupedLightService.Rid);

                        if (scene != null)
                            return (scene.Id, "scene", target);
                    }
                }

                // Check if groupName matches a entertainmentGroups
                /*var entertainmentGroupScene = _entertainmentGroups?.FirstOrDefault(e => e.Metadata?.Name?.ToLowerInvariant() == groupName.ToLowerInvariant());
                if (entertainmentGroupScene != null)
                {
                    var groupedLightService = entertainmentGroupScene.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                    if (groupedLightService != null)
                    {
                        var scene = _scenes?.FirstOrDefault(s =>
                            s.Metadata?.Name?.ToLowerInvariant() == target &&
                            s.Group?.Rid == groupedLightService.Rid);

                        if (scene != null)
                            return (scene.Id, "scene", target);
                    }
                }*/
            }

            // No target provided, match Metadata.Name within LastUserMessage
            if (!string.IsNullOrEmpty(LastUserMessage))
            {
                var normalizedMessage = LastUserMessage.ToLowerInvariant();
                if (!string.IsNullOrEmpty(target) && string.IsNullOrEmpty(groupName))
                {
                    // Check Lights
                    var light = _lights?.FirstOrDefault(l =>
                        !string.IsNullOrEmpty(l.Metadata?.Name) &&
                        normalizedMessage.Contains(l.Metadata.Name.ToLowerInvariant()));
                    if (light != null)
                    {
                        Logger.LogInformation($"LastUserMessage match on light: {light?.Metadata?.Name}");
                        return (light?.Id, "light", light?.Metadata?.Name);
                    }

                    // Check Groups
                    var group = _groups?.FirstOrDefault(g =>
                        !string.IsNullOrEmpty(g.Metadata?.Name) &&
                        normalizedMessage.Contains(g.Metadata.Name.ToLowerInvariant()));
                    if (group != null)
                    {
                        Logger.LogInformation($"LastUserMessage match on group: {group?.Metadata?.Name}");
                        return (group?.Id, "group", group?.Metadata?.Name);
                    }

                    // Check Rooms
                    var room = _rooms?.FirstOrDefault(r =>
                        !string.IsNullOrEmpty(r.Metadata?.Name) &&
                        normalizedMessage.Contains(r.Metadata.Name.ToLowerInvariant()));
                    if (room != null)
                    {
                        Logger.LogInformation($"LastUserMessage match on room: {room?.Metadata?.Name}");

                        var groupedLightService = room?.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                        if (groupedLightService != null)
                        {
                            Logger.LogInformation($"Grouped light service found: {groupedLightService.Rid}");
                            return (groupedLightService.Rid, "group", room?.Metadata?.Name);
                        }
                    }

                    // Check Zones
                    var zone = _zones?.FirstOrDefault(z =>
                        !string.IsNullOrEmpty(z.Metadata?.Name) &&
                        normalizedMessage.Contains(z.Metadata.Name.ToLowerInvariant()));
                    if (zone != null)
                    {
                        Logger.LogInformation($"LastUserMessage match on zone: {zone?.Metadata?.Name}");

                        var groupedLightService = zone?.Services?.FirstOrDefault(s => s.Rtype == "grouped_light");
                        if (groupedLightService != null)
                        {
                            Logger.LogInformation($"Grouped light service found: {groupedLightService.Rid}");
                            return (groupedLightService.Rid, "group", zone?.Metadata?.Name);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(groupName))
                {
                    var normalizedGroupName = groupName.ToLowerInvariant();

                    // Check if groupName matches a group
                    var groupScene = _groups?.FirstOrDefault(g =>
                        !string.IsNullOrEmpty(g.Metadata?.Name) &&
                        normalizedGroupName.Contains(g.Metadata.Name.ToLowerInvariant()));
                    if (groupScene != null)
                    {
                        Logger.LogInformation($"LastUserMessage match on group for scene: {groupScene?.Metadata?.Name}");

                        var scene = _scenes?.FirstOrDefault(s =>
                            !string.IsNullOrEmpty(s.Metadata?.Name) &&
                            normalizedMessage.Contains(s.Metadata.Name.ToLowerInvariant()) &&
                            s.Group?.Rid == groupScene?.Id);

                        if (scene != null)
                        {
                            Logger.LogInformation($"Scene match for group: {scene?.Metadata?.Name}");
                            return (scene?.Id, "scene", scene?.Metadata?.Name);
                        }
                    }

                    // Check if groupName matches a room
                    var roomScene = _rooms?.FirstOrDefault(r =>
                        !string.IsNullOrEmpty(r.Metadata?.Name) &&
                        normalizedGroupName.Contains(r.Metadata.Name.ToLowerInvariant()));
                    if (roomScene != null)
                    {
                        Logger.LogInformation($"LastUserMessage match on room for scene: {roomScene?.Metadata?.Name}");

                        var roomID = roomScene?.Id;
                        if (roomID != null)
                        {
                            Logger.LogInformation($"Grouped light service found for room: {roomID}");

                            var scene = _scenes?.FirstOrDefault(s =>
                                !string.IsNullOrEmpty(s.Metadata?.Name) &&
                                normalizedMessage.Contains(s.Metadata.Name.ToLowerInvariant()) &&
                                s.Group?.Rid == roomID);

                            if (scene != null)
                            {
                                Logger.LogInformation($"Scene match for room: {scene?.Metadata?.Name}");
                                return (scene?.Id, "scene", scene?.Metadata?.Name);
                            }
                        }
                    }

                    // Check if groupName matches a zone
                    var zoneScene = _zones?.FirstOrDefault(z =>
                        !string.IsNullOrEmpty(z.Metadata?.Name) &&
                        normalizedGroupName.Contains(z.Metadata.Name.ToLowerInvariant()));
                    if (zoneScene != null)
                    {
                        Logger.LogInformation($"LastUserMessage match on zone for scene: {zoneScene?.Metadata?.Name}");

                        var zoneID = zoneScene?.Id;
                        if (zoneID != null)
                        {
                            Logger.LogInformation($"Grouped light service found for zone: {zoneID}");

                            var scene = _scenes?.FirstOrDefault(s =>
                                !string.IsNullOrEmpty(s.Metadata?.Name) &&
                                normalizedMessage.Contains(s.Metadata.Name.ToLowerInvariant()) &&
                                s.Group?.Rid == zoneID);

                            if (scene != null)
                            {
                                Logger.LogInformation($"Scene match for zone: {scene?.Metadata?.Name}");
                                return (scene?.Id, "scene", scene?.Metadata?.Name);
                            }
                        }
                    }
                }
            }

            return (null, null, null);
        }

        private string? TranslateColorNameToHex(string colorName)
        {
            // Try to parse the color name
            try
            {
                var color = System.Drawing.Color.FromName(colorName);

                // Check if the color is valid (KnownColor.Unknown means invalid)
                if (color.ToArgb() != 0)
                {
                    return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                }
            }
            catch
            {
                Logger.LogWarning($"Color name '{colorName}' could not be translated.");
            }
            return null;
        }

        private async Task SendHueCommandAsync(Guid targetId, string type, bool? state = null, string? color = null, double? brightness = null, string? scene = null)
        {
            if (type == "light")
            {
                var lightCommand = new UpdateLight();
                var updates = new List<string>();

                // Handle state change
                if (state.HasValue)
                {
                    lightCommand = state.Value ? lightCommand.TurnOn() : lightCommand.TurnOff();
                    updates.Add($"state: {state.Value}");
                }

                // Handle color change
                if (!string.IsNullOrWhiteSpace(color))
                {
                    var rgbColor = new RGBColor(color);
                    lightCommand = lightCommand.SetColor(rgbColor);
                    updates.Add($"color: {color}");
                }

                // Handle brightness change
                if (brightness.HasValue)
                {
                    lightCommand = lightCommand.SetBrightness(brightness.Value);
                    updates.Add($"brightness: {brightness.Value}");
                }

                if (updates.Any())
                {
                    _ = await _hueClient!.UpdateLightAsync(targetId, lightCommand);
                    Logger.LogInformation($"Group/Room '{targetId}' updated with: {string.Join(", ", updates)}");
                }
                else
                {
                    Logger.LogInformation($"Group/Room '{targetId}' had no changes.");
                }
            }
            else if (type == "group" || type == "room" || type == "zone")
            {
                // If a scene is specified, activate it
                if (!string.IsNullOrWhiteSpace(scene))
                {

                    if (scene != null)
                    {
                        // Prepare the scene recall request
                        var updateScene = new UpdateScene
                        {
                            Recall = new Recall { Action = SceneRecallAction.active }
                        };

                        // Activate the scene
                        var result = await _hueClient!.UpdateSceneAsync(targetId, updateScene);

                        if (!result.HasErrors)
                        {
                            Logger.LogInformation($"Scene '{scene}' activated successfully.");
                        }
                        else
                        {
                            Logger.LogWarning($"Failed to activate scene '{scene}': {result.Errors}");
                        }
                        return;
                    }
                    else
                    {
                        Logger.LogWarning($"Scene '{scene}' not found.");
                    }
                }

                var hueCommand = new UpdateGroupedLight();
                var updates = new List<string>();

                // Handle state change
                if (state.HasValue)
                {
                    hueCommand = state.Value ? hueCommand.TurnOn() : hueCommand.TurnOff();
                    updates.Add($"state: {state.Value}");
                }

                // Handle color change
                if (!string.IsNullOrWhiteSpace(color))
                {
                    var rgbColor = new RGBColor(color);
                    hueCommand = hueCommand.SetColor(rgbColor);
                    updates.Add($"color: {color}");
                }

                // Handle brightness change
                if (brightness.HasValue)
                {
                    hueCommand = hueCommand.SetBrightness(brightness.Value);
                    updates.Add($"brightness: {brightness.Value}");
                }

                if (updates.Any())
                {
                    _ = await _hueClient!.UpdateGroupedLightAsync(targetId, hueCommand);
                    Logger.LogInformation($"Group/Room '{targetId}' updated with: {string.Join(", ", updates)}");
                }
                else
                {
                    Logger.LogInformation($"Group/Room '{targetId}' had no changes.");
                }
            }
            else if (type == "entertainment_configuration")
            {
                var hueCommand = new UpdateEntertainmentConfiguration();
                var updates = new List<string>();

                if (updates.Any())
                {
                    _ = await _hueClient!.UpdateEntertainmentConfigurationAsync(targetId, hueCommand);
                    Logger.LogInformation($"Group/Room '{targetId}' updated with: {string.Join(", ", updates)}");
                }
                else
                {
                    Logger.LogInformation($"Group/Room '{targetId}' had no changes.");
                }
            }
            else
            {
                Logger.LogWarning($"Invalid type '{type}' provided.");
            }
        }

        private static string CleanString(string input)
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
    }
}