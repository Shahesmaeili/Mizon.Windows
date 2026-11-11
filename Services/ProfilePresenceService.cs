using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

/// <summary>
/// One focus owner, one authenticated SignalR connection, and the same confirmed
/// state for the header and taskbar. No socket is allocated on the sign-in page.
/// </summary>
public sealed class ProfilePresenceService : IProfilePresenceService, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Func<IProfilePresenceTransport> _createTransport;
    private readonly Action<long, ProfilePresenceState> _publish;
    private readonly Action<Exception> _reportError;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lifetime = new();
    private IProfilePresenceTransport? _transport;
    private ITimer? _leaseTimer;
    private ITimer? _retryTimer;
    private ProfilePresenceState _state = new(false, false, false, false, false);
    private long _session;
    private long _sendVersion;
    private int _retryCount;
    private bool _disposed;

    internal ProfilePresenceService(Func<IProfilePresenceTransport> createTransport,
        Action<long, ProfilePresenceState> publish, Action<Exception> reportError, TimeProvider? timeProvider = null)
    {
        _createTransport = createTransport;
        _publish = publish;
        _reportError = reportError;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler? StateChanged;

    public ProfilePresenceState ReadState()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _state;
        }
    }

    internal void SetSession(bool authenticated, long session)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_state.HasSession == authenticated && _session == session) return;
            _session = session;
            _state = _state with { HasSession = authenticated, IsOnline = false, HasConfirmedState = false };
            if (authenticated && _transport is null)
            {
                _transport = _createTransport();
                _transport.StateChanged += TransportStateChanged;
            }
            SynchronizeLocked();
            RefreshLocked(forceNotification: true);
        }
    }

    internal void SetWindowActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _state.IsWindowActive == active) return;
            _state = _state with { IsWindowActive = active, IsOnline = false, HasConfirmedState = false };
            SynchronizeLocked();
            RefreshLocked(forceNotification: true);
        }
    }

    internal void RefreshSynchronization()
    {
        lock (_gate)
        {
            if (_disposed) return;
            SynchronizeLocked();
            RefreshLocked();
        }
    }

    private void SynchronizeLocked(bool resetRetries = true)
    {
        if (_transport is null) return;
        var version = ++_sendVersion;
        _retryTimer?.Dispose();
        _retryTimer = null;
        if (resetRetries) _retryCount = 0;
        // The SDK records the newest desired state before its first await. Calling
        // under this gate orders rapid activation, deactivation and account changes.
        try
        {
            var task = _transport.SetWindowPresenceAsync(_state.HasSession && _state.IsWindowActive, _lifetime.Token);
            _ = ObserveAsync(task, version);
        }
        catch (Exception error)
        {
            _reportError(error);
            RefreshLocked();
            ScheduleRetryLocked(version);
        }
    }

    private async Task ObserveAsync(Task operation, long version)
    {
        var failed = false;
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception error) { failed = true; _reportError(error); }
        finally
        {
            lock (_gate)
                if (!_disposed)
                {
                    RefreshLocked();
                    if (failed) ScheduleRetryLocked(version);
                }
        }
    }

    private void ScheduleRetryLocked(long version)
    {
        if (_disposed || version != _sendVersion || !_state.HasSession || !_state.IsConnected
            || _state.HasConfirmedState || _retryCount >= 3) return;
        var delay = TimeSpan.FromSeconds(++_retryCount switch { 1 => 1, 2 => 2, _ => 5 });
        _retryTimer?.Dispose();
        _retryTimer = _timeProvider.CreateTimer(static state =>
        {
            var retry = (PresenceRetry)state!;
            lock (retry.Owner._gate)
            {
                if (retry.Owner._disposed || retry.Version != retry.Owner._sendVersion
                    || !retry.Owner._state.HasSession || !retry.Owner._state.IsConnected
                    || retry.Owner._state.HasConfirmedState) return;
                retry.Owner.SynchronizeLocked(resetRetries: false);
                retry.Owner.RefreshLocked();
            }
        }, new PresenceRetry(this, version), delay, Timeout.InfiniteTimeSpan);
    }

    private sealed record PresenceRetry(ProfilePresenceService Owner, long Version);

    private void TransportStateChanged(object? sender, EventArgs args)
    {
        lock (_gate)
            if (!_disposed) RefreshLocked();
    }

    private void RefreshLocked(bool forceNotification = false)
    {
        var transport = _transport?.ReadState() ?? default;
        var connected = _state.HasSession && transport.IsConnected;
        var confirmed = connected && transport.ConfirmedOnline is { } online && online == _state.IsWindowActive;
        var next = _state with
        {
            IsConnected = connected,
            HasConfirmedState = confirmed,
            IsOnline = confirmed && _state.IsWindowActive
        };
        if (!forceNotification && next == _state) return;
        _state = next;
        if (next.HasConfirmedState || !next.IsConnected)
        {
            _retryTimer?.Dispose();
            _retryTimer = null;
        }
        if (next.IsOnline)
        {
            _leaseTimer ??= _timeProvider.CreateTimer(static state => ((ProfilePresenceService)state!).RenewLease(),
                this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _leaseTimer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }
        else _leaseTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        PublishLocked();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenewLease()
    {
        lock (_gate)
            if (!_disposed && _state.IsOnline) PublishLocked();
    }

    private void PublishLocked()
    {
        // The publisher only queues atomic local IPC; it does not do disk I/O here.
        try { _publish(_session, _state); }
        catch (Exception error) { _reportError(error); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _state = new(false, false, false, false, false);
            PublishLocked();
            _disposed = true;
            _leaseTimer?.Dispose();
            _leaseTimer = null;
            _retryTimer?.Dispose();
            _retryTimer = null;
            if (_transport is { } transport)
            {
                transport.StateChanged -= TransportStateChanged;
                transport.Dispose();
                _transport = null;
            }
            StateChanged = null;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
