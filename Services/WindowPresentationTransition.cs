using System;

namespace Mizon.Windows.Services;

// Owned by the window's UI thread. enqueue must schedule a later dispatcher turn;
// Dispose is called when the window closes, before releasing native resources.
internal sealed class WindowPresentationTransition(
    Func<Action, bool> enqueue,
    Func<bool> currentState,
    Action<bool> apply,
    Action<Exception> reportFailure) : IDisposable
{
    private bool? _pending;
    private bool _queued, _applying, _disposed;

    public void Request(bool? enabled = null)
    {
        if (_disposed) return;
        try { _pending = enabled ?? !(_pending ?? currentState()); }
        catch (Exception error) { Report(error); return; }
        QueuePending();
    }

    // MCP needs a completed native transition and immediate state readback.
    public void Set(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_applying) throw new InvalidOperationException("A window presentation transition is already running.");
        _pending = null;
        try { Apply(enabled); }
        finally { QueuePending(); }
    }

    private void QueuePending()
    {
        if (_disposed || _applying || _queued || !_pending.HasValue) return;
        _queued = true;
        Exception? failure = null;
        try
        {
            if (enqueue(Drain)) return;
            failure = new InvalidOperationException("The window presentation transition could not be queued.");
        }
        catch (Exception error) { failure = error; }
        _queued = false;
        _pending = null;
        Report(failure);
    }

    private void Drain()
    {
        _queued = false;
        if (_disposed || _applying || _pending is not { } enabled) return;
        _pending = null;
        try { Apply(enabled); }
        catch (Exception error) { Report(error); }
        finally { QueuePending(); }
    }

    private void Apply(bool enabled)
    {
        _applying = true;
        try
        {
            var current = currentState();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (enabled != current) apply(enabled);
        }
        finally { _applying = false; }
    }

    private void Report(Exception error)
    {
        if (!_disposed) reportFailure(error);
    }

    public void Dispose()
    {
        _disposed = true;
        _pending = null;
    }
}
