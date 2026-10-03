namespace DisplaySelector.App;

/// <summary>
/// "Newest request wins" for async UI work (a launch waiting for displays, an audio confirm): starting
/// an operation cancels the one still running in the same slot.
/// </summary>
internal sealed class LatestOperation
{
    private CancellationTokenSource? _current;

    /// <summary>Cancels the running operation (if any) and starts tracking a new one.</summary>
    public CancellationTokenSource Start()
    {
        _current?.Cancel();
        _current = new CancellationTokenSource();
        return _current;
    }

    public void Cancel() => _current?.Cancel();

    /// <summary>Call when an operation from <see cref="Start"/> finishes, however it ended.</summary>
    public void End(CancellationTokenSource operation)
    {
        if (_current == operation)
        {
            _current = null;
        }
        operation.Dispose();
    }
}
