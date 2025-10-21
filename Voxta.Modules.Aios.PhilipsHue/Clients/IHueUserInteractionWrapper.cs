using Voxta.Abstractions.Utils;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public interface IHueUserInteractionWrapper
{
    Task<IUserInteractionRequestToken> RequestUserInteraction(CancellationToken cancellationToken);
}
