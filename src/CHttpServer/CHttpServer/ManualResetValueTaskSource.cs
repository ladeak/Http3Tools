using System.Threading.Tasks.Sources;

namespace CHttpServer;

internal sealed class ManualResetValueTaskSource<T> : IValueTaskSource<T>, IValueTaskSource
{
    private ManualResetValueTaskSourceCore<T> _core;
    public bool RunContinuationsAsynchronously { get => _core.RunContinuationsAsynchronously; set => _core.RunContinuationsAsynchronously = value; }
    public short Version => _core.Version;
    public void Reset() => _core.Reset();
    public void SetException(Exception error) => _core.SetException(error);
    void IValueTaskSource.GetResult(short token) => _core.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);
    public ValueTaskSourceStatus GetStatus() => _core.GetStatus(_core.Version);
    public T GetResult(short token) => _core.GetResult(token);
    public void TrySetResult(T result)
    {
        if (_core.GetStatus(_core.Version) == ValueTaskSourceStatus.Pending)
            _core.SetResult(result);
    }
}
