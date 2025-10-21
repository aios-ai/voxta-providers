using Voxta.Abstractions.Chats.Sessions;
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
}
