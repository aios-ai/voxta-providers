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

    public async Task InitializeBridgeAsync(CancellationToken cancellationToken)
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
                    await _session.SetFlags(SetFlagRequest.ParseFlags(["hueBridge_connected", "!hueBridge_disconnected"]), cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to the Hue bridge using saved configuration. Falling back to discovery...");
                }
            }
        }
        else
        {
            _logger.LogInformation("No Authentication file found. Starting bridge discovery...");
        }

        await DiscoverAndConnectBridgeAsync(cancellationToken);
    }

    private async Task DiscoverAndConnectBridgeAsync(CancellationToken cancellationToken)
    {
        var retryCount = 0;
        _logger.LogInformation("Discovering Hue bridge...");

        while (retryCount < MaxRetries)
        {
            var bridgeLocator = new HttpBridgeLocator();
            var bridges = (await bridgeLocator.LocateBridgesAsync(TimeSpan.FromSeconds(RetryIntervalSeconds))).ToArray();

            if (bridges.Length != 0)
            {
                var bridgeInfo = bridges.First();
                _bridgeIp = bridgeInfo.IpAddress;
                _logger.LogInformation("Bridge discovered: {BridgeIp}", _bridgeIp);
                await ConnectBridgeAsync(cancellationToken);
                return;
            }

            _logger.LogWarning("No Hue bridges found. Retrying...");
            retryCount++;
            await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds), cancellationToken);
        }

        _logger.LogWarning("Max retries reached. No Hue bridges found.");
    }

    private async Task ConnectBridgeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_bridgeIp))
        {
            _logger.LogWarning("No bridge discovered.");
            return;
        }

        var savedAppKey = LoadAppKey();
        if (savedAppKey != null)
        {
            try
            {
                _hueClient = new LocalHueApi(_bridgeIp, savedAppKey.Username);
                _logger.LogInformation("Connected to Hue bridge using saved app key...");
                await _session.SetFlags(SetFlagRequest.ParseFlags(["hueBridge_connected", "!hueBridge_disconnected"]), cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to the Hue bridge using saved app key. Falling back to registration...");
            }
        }

        await AttemptBridgeRegistrationAsync(cancellationToken);
    }

    private async Task AttemptBridgeRegistrationAsync(CancellationToken cancellationToken)
    {
        await using var _ = await _userInteractionWrapper.RequestUserInteraction(cancellationToken);
        
        var registrationSuccessful = false;
        var retryDelay = TimeSpan.FromSeconds(RetryIntervalSeconds);
        var retries = 0;

        while (!registrationSuccessful && retries < MaxRetries && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var appKey = await LocalHueApi.RegisterAsync(_bridgeIp!, "Voxta", Environment.MachineName, false);
                await SaveAppKey(appKey);
                _logger.LogInformation("Bridge connected and app key saved.");

                _hueClient = new LocalHueApi(_bridgeIp, appKey.Username);
                await _session.SetFlags(SetFlagRequest.ParseFlags(["hueBridge_connected", "!hueBridge_disconnected"]), cancellationToken);

                registrationSuccessful = true;
            }
            catch (LinkButtonNotPressedException)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                retries++;
                _logger.LogWarning("Link button not pressed. Attempt {Retries} of {MaxRetries}. Retrying in {RetryDelaySeconds} seconds.", retries, MaxRetries, retryDelay.Seconds);
                await Task.Delay(retryDelay, cancellationToken);
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register with the Hue bridge.");
                return;
            }
        }
        
        if (!registrationSuccessful && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Failed to register with Hue Bridge after {MaxRetries} retries.", MaxRetries);
        }
    }
    
    private RegisterEntertainmentResult? LoadAppKey()
    {
        if (File.Exists(_authPath))
        {
            return JsonSerializer.Deserialize<RegisterEntertainmentResult>(File.ReadAllText(_authPath));
        }
        return null;
    }

    private async Task SaveAppKey(RegisterEntertainmentResult? appKey)
    {
        if (appKey == null)
        {
            _logger.LogError("AppKey is null. Cannot save to file.");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_authPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(appKey);
            await File.WriteAllTextAsync(_authPath, json);
            _logger.LogInformation("App key saved to file.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save app key to file: {Message}", ex.Message);
            throw;
        }
    }
}
