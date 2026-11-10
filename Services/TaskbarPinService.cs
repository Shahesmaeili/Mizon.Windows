using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

internal sealed partial class TaskbarPinService : ITaskbarPinService, IDisposable
{
    private readonly ITaskbarPinBackend _backend;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _activeOperations;
    private int _resourcesReleased;
    private bool _requestInProgress;
    private bool _disposed;

    internal TaskbarPinService(ITaskbarPinBackend backend) => _backend = backend;

    public TaskbarPinState State { get; private set; } = new(false, false, false, false, "not_checked");
    public event EventHandler? StateChanged;

    public async Task<TaskbarPinState> RefreshAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed) return State;
        _backend.VerifyAccess();
        if (_requestInProgress) return State;
        Interlocked.Increment(ref _activeOperations);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var entered = false;
        try
        {
            await _gate.WaitAsync(request.Token);
            entered = true;
            request.Token.ThrowIfCancellationRequested();
            if (_requestInProgress) return State;
            var state = await _backend.ReadAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            Publish(state with { IsBusy = false });
            return State;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            Publish(new(false, false, false, false, "unavailable"));
            return State;
        }
        finally
        {
            if (entered) _gate.Release();
            FinishOperation();
        }
    }

    public async Task<TaskbarPinResult> RequestPinAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed) return Result(false, "unavailable", "The taskbar connection has closed.");
        _backend.VerifyAccess();
        if (_requestInProgress) return Result(false, "busy", "A taskbar pin request is already in progress.");
        _requestInProgress = true;
        Publish(State with { IsBusy = true });
        // The caller can stop waiting immediately, but the service retains the
        // native operation until it actually completes. Canceling a caller must
        // not let a second prompt open while Windows still owns the first one.
        var completion = RequestCoreAsync(token);
        var result = await completion.WaitAsync(token);
        return result with { State = State };
    }

    private async Task<TaskbarPinResult> RequestCoreAsync(CancellationToken token)
    {
        Interlocked.Increment(ref _activeOperations);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var entered = false;
        try
        {
            await _gate.WaitAsync(request.Token);
            entered = true;
            var state = await _backend.ReadAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            Publish(state with { IsBusy = true });
            if (state.IsPinned) return Result(true, "already_pinned", "Mizon is already pinned to the taskbar.");
            if (!state.IsSupported || !state.IsPinningAllowed)
                return Result(false, state.Code, "Windows does not currently allow this app to request taskbar pinning.");
            if (!_backend.IsForeground)
                return Result(false, "foreground_required", "Bring the Mizon window to the foreground before requesting a pin.");
            request.Token.ThrowIfCancellationRequested();
            var accepted = await _backend.RequestPinAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            // Query Windows again; do not use a persisted app preference as proof
            // of pinning, including after the user has unpinned Mizon externally.
            state = await _backend.ReadAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            Publish(state with { IsBusy = true });
            return state.IsPinned
                ? Result(true, "pinned", "Mizon is pinned to the taskbar.")
                : Result(false, accepted ? "not_pinned" : "not_confirmed", "The app was not pinned. Windows or the user declined the request.");
        }
        catch (OperationCanceledException)
        {
            return Result(false, "canceled", "The pin request was canceled. Refresh to read the current Windows state.");
        }
        catch (Exception)
        {
            Publish(State with { IsPinningAllowed = false, Code = "unavailable" });
            return Result(false, "unavailable", "Windows could not complete the taskbar pin request.");
        }
        finally
        {
            if (entered) _gate.Release();
            _requestInProgress = false;
            Publish(State with { IsBusy = false });
            FinishOperation();
        }
    }

    private TaskbarPinResult Result(bool success, string code, string message) =>
        new(success, code, message, State);

    private void Publish(TaskbarPinState state)
    {
        if (_disposed || State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _backend.Dispose();
        State = new(false, false, false, false, "unavailable");
        StateChanged = null;
        ReleaseResourcesIfIdle();
    }

    private void FinishOperation()
    {
        Interlocked.Decrement(ref _activeOperations);
        ReleaseResourcesIfIdle();
    }

    private void ReleaseResourcesIfIdle()
    {
        // Active/canceled continuations still own their semaphore release. Defer
        // disposal until they settle instead of racing a native prompt callback.
        if (!_disposed || Volatile.Read(ref _activeOperations) != 0
            || Interlocked.Exchange(ref _resourcesReleased, 1) != 0) return;
        _gate.Dispose();
        _lifetime.Dispose();
    }
}
