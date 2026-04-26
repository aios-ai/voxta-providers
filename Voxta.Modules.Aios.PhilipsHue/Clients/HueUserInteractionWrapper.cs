using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Chats.Objects.Chats;
using Voxta.Abstractions.Utils;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public class HueUserInteractionWrapper(IChatSessionChatAugmentationApi session) : IHueUserInteractionWrapper
{
    public Task<IUserInteractionRequestToken> RequestUserInteraction(CancellationToken cancellationToken)
    {
        return session.RequestUserAction(new UserInteractionRequestInput
        {
            Message = "Please press the link button on your Hue bridge to authorize the connection.",
        }, cancellationToken);
    }

    public Task SetBridgeConnectionStateAsync(bool connected, CancellationToken cancellationToken)
    {
        var flags = connected
            ? new[] { "hueBridge_connected", "!hueBridge_disconnected" }
            : ["hueBridge_disconnected", "!hueBridge_connected"];

        return session.SetFlags(SetFlagRequest.ParseFlags(flags), cancellationToken);
    }
}
