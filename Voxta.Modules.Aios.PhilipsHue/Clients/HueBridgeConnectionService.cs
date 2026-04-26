using System.Text.Json;
using HueApi;
using HueApi.BridgeLocator;
using HueApi.Models.Clip;
using HueApi.Models.Exceptions;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Objects.Chats;
using Voxta.Abstractions.Chats.Sessions;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public class HueBridgeConnectionService : IHueBridgeConnectionService
{
    private const int MaxRetries = 20;
    private const int RetryIntervalSeconds = 5;

    private readonly ILogger<HueBridgeConnectionService> _logger;
    private readonly IHueUserInteractionWrapper _userInteractionWrapper;
    private readonly IChatSessionChatAugmentationApi _session;
    private readonly string _authPath;

    private LocalHueApi? _hueClient;
    private string? _bridgeIp;

    public LocalHueApi? HueClient => _hueClient;
    public bool IsConnected => _hueClient != null;
    public bool IsAuthorizationRequired => State == HueBridgeState.AuthRequired;
    public HueBridgeState State { get; private set; } = HueBridgeState.Disconnected;
    public string? LastUserVisibleError { get; private set; }

    public HueBridgeConnectionService(
        ILogger<HueBridgeConnectionService> logger,
        IHueUserInteractionWrapper userInteractionWrapper,
        IChatSessionChatAugmentationApi session,
        string authPath)
    {
        _logger = logger;
        _userInteractionWrapper = userInteractionWrapper;
        _session = session;
        _authPath = authPath;
    }

    public async Task<bool> InitializeBridgeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing Hue Bridge...");

        if (File.Exists(_authPath))
        {
            _logger.LogInformation("Authentication file found. Attempting to connect using saved configuration...");
            var appKey = LoadAppKey();

            if (appKey != null && !string.IsNullOrEmpty(appKey.Ip) && !string.IsNullOrEmpty(appKey.Username))
            {
                _bridgeIp = appKey.Ip;
                try
                {
                    _hueClient = new LocalHueApi(_bridgeIp, appKey.Username);
                    _logger.LogInformation("Connected to Hue bridge using saved configuration.");
                    await SetStateAsync(HueBridgeState.Connected, null, cancellationToken);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to the Hue bridge using saved configuration. Falling back to discovery...");
                    await SetStateAsync(HueBridgeState.Unavailable, "The saved Hue bridge connection could not be reached. I will try to discover the bridge again.", cancellationToken);
                }
            }
            else
            {
                await SetStateAsync(HueBridgeState.AuthRequired, "Hue authorization is incomplete. Please press the Hue bridge link button when Voxta asks for authorization.", cancellationToken);
            }
        }
        else
        {
            _logger.LogInformation("No Authentication file found. Starting bridge discovery...");
            await SetStateAsync(HueBridgeState.AuthRequired, "Hue authorization is required. Please press the Hue bridge link button when Voxta asks for authorization.", cancellationToken);
        }

        return await DiscoverAndConnectBridgeAsync(cancellationToken);
    }

    private async Task<bool> DiscoverAndConnectBridgeAsync(CancellationToken cancellationToken)
    {
        var retryCount = 0;
        _logger.LogInformation("Discovering Hue bridge...");

        while (retryCount < MaxRetries)
        {
            LocatedBridge[] bridges;
            try
            {
                var bridgeLocator = new HttpBridgeLocator();
                bridges = (await bridgeLocator.LocateBridgesAsync(TimeSpan.FromSeconds(RetryIntervalSeconds))).ToArray();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hue bridge discovery failed.");
                await SetStateAsync(HueBridgeState.Unavailable, "Hue bridge discovery failed. Please check that Voxta is on the same network as the Hue bridge.", cancellationToken);
                return false;
            }

            if (bridges.Length != 0)
            {
                var bridgeInfo = bridges.First();
                _bridgeIp = bridgeInfo.IpAddress;
                _logger.LogInformation("Bridge discovered: {BridgeIp}", _bridgeIp);
                return await ConnectBridgeAsync(cancellationToken);
            }

            _logger.LogWarning("No Hue bridges found. Retrying...");
            retryCount++;
            await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds), cancellationToken);
        }

        _logger.LogWarning("Max retries reached. No Hue bridges found.");
        await SetStateAsync(HueBridgeState.Unavailable, "No Hue bridge was found. Please check that the bridge is powered on and on the same network as Voxta.", cancellationToken);
        return false;
    }

    private async Task<bool> ConnectBridgeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_bridgeIp))
        {
            _logger.LogWarning("No bridge discovered.");
            await SetStateAsync(HueBridgeState.Unavailable, "No Hue bridge was found. Please check that it is powered on and on the same network as Voxta.", cancellationToken);
            return false;
        }

        var savedAppKey = LoadAppKey();
        if (savedAppKey != null)
        {
            try
            {
                _hueClient = new LocalHueApi(_bridgeIp, savedAppKey.Username);
                _logger.LogInformation("Connected to Hue bridge using saved app key...");
                await SetStateAsync(HueBridgeState.Connected, null, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to the Hue bridge using saved app key. Falling back to registration...");
                await SetStateAsync(HueBridgeState.AuthRequired, "The saved Hue authorization could not be used. Please press the Hue bridge link button when Voxta asks for authorization.", cancellationToken);
            }
        }

        return await AttemptBridgeRegistrationAsync(cancellationToken);
    }

    private async Task<bool> AttemptBridgeRegistrationAsync(CancellationToken cancellationToken)
    {
        await using var _ = await _userInteractionWrapper.RequestUserInteraction(cancellationToken);
        await SetStateAsync(HueBridgeState.AuthRequired, "Hue authorization is required. Please press the link button on the Hue bridge.", cancellationToken);

        var registrationSuccessful = false;
        var retryDelay = TimeSpan.FromSeconds(RetryIntervalSeconds);
        var retries = 0;

        while (!registrationSuccessful && retries < MaxRetries && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var appKey = await LocalHueApi.RegisterAsync(_bridgeIp!, "Voxta", Environment.MachineName, false);
                if (!await SaveAppKey(appKey))
                    return false;

                _logger.LogInformation("Bridge connected and app key saved.");
                _hueClient = new LocalHueApi(_bridgeIp!, appKey!.Username);
                await SetStateAsync(HueBridgeState.Connected, null, cancellationToken);
                registrationSuccessful = true;
            }
            catch (LinkButtonNotPressedException)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                retries++;
                _logger.LogWarning("Link button not pressed. Attempt {Retries} of {MaxRetries}. Retrying in {RetryDelaySeconds} seconds.", retries, MaxRetries, retryDelay.Seconds);
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register with the Hue bridge.");
                await SetStateAsync(HueBridgeState.Unavailable, "Hue authorization failed while contacting the bridge. Please check the bridge and try again.", cancellationToken);
                return false;
            }
        }

        if (!registrationSuccessful && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Failed to register with Hue Bridge after {MaxRetries} retries.", MaxRetries);
            await SetStateAsync(HueBridgeState.AuthRequired, "Hue authorization timed out. Please press the Hue bridge link button and try again.", cancellationToken);
        }

        return registrationSuccessful;
    }

    private RegisterEntertainmentResult? LoadAppKey()
    {
        if (!File.Exists(_authPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<RegisterEntertainmentResult>(File.ReadAllText(_authPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Hue authentication file.");
            LastUserVisibleError = "The Hue authorization file could not be read. Please authorize Hue again.";
            State = HueBridgeState.AuthRequired;
            return null;
        }
    }

    private async Task<bool> SaveAppKey(RegisterEntertainmentResult? appKey)
    {
        if (appKey == null)
        {
            _logger.LogError("AppKey is null. Cannot save to file.");
            LastUserVisibleError = "Hue authorization returned no app key. Please try authorizing again.";
            State = HueBridgeState.AuthRequired;
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(_authPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(appKey);
            await File.WriteAllTextAsync(_authPath, json);
            _logger.LogInformation("App key saved to file.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save app key to file: {Message}", ex.Message);
            LastUserVisibleError = "Hue was authorized, but Voxta could not save the authorization file. Please check the configured auth path.";
            State = HueBridgeState.MissingConfiguration;
            return false;
        }
    }

    private async Task SetStateAsync(HueBridgeState state, string? userVisibleError, CancellationToken cancellationToken)
    {
        State = state;
        LastUserVisibleError = userVisibleError;
        if (state != HueBridgeState.Connected)
            _hueClient = null;

        var flags = state == HueBridgeState.Connected
            ? new[] { "hueBridge_connected", "!hueBridge_disconnected" }
            : ["hueBridge_disconnected", "!hueBridge_connected"];

        try
        {
            await _session.SetFlags(SetFlagRequest.ParseFlags(flags), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update Hue bridge state flags.");
        }
    }
}
