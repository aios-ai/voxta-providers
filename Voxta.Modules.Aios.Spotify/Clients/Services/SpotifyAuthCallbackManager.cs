namespace Voxta.Modules.Aios.Spotify.Clients.Services;

public interface ISpotifyAuthCallbackManager
{
    void Callback(string code);
    Task<string> WaitForCodeAsync(CancellationToken cancellationToken);
}

public class SpotifyAuthCallbackManager : ISpotifyAuthCallbackManager
{
    private readonly Lock _lock = new();
    private TaskCompletionSource<string>? _codeTcs;
    
    public void Callback(string code)
    {
        lock (_lock)
        {
            if(_codeTcs == null) throw new NullReferenceException("There is no listener for the code");
            _codeTcs.SetResult(code);
            _codeTcs = null;
        }
    }

    public Task<string> WaitForCodeAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if(_codeTcs != null) throw new NullReferenceException("There is already a listener for the code");
            var tcs = new TaskCompletionSource<string>(_codeTcs);
            cancellationToken.Register(() =>
            {
                if (tcs.Task.IsCompleted) return;
                tcs.SetCanceled(cancellationToken);
                lock (_lock)
                {
                    if (_codeTcs == tcs)
                        _codeTcs = null;
                }
            });
            _codeTcs = tcs;
            return tcs.Task;
        }
    }
}