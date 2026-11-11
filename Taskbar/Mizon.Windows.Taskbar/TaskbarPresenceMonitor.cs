using System.ComponentModel;
using System.Diagnostics;

namespace Mizon.Windows.Taskbar;

internal interface IPresenceProcessWatch : IDisposable
{
    bool HasExited { get; }
}

/// <summary>Server-confirmed presence is a short lease, never inferred from saved authentication.</summary>
internal sealed class TaskbarPresenceMonitor(Action changed,
    Func<int, long, Action, IPresenceProcessWatch?>? watchProcess = null) : IDisposable
{
    internal static readonly TimeSpan MaximumHeartbeatAge = TimeSpan.FromSeconds(30);
    private readonly Lock _gate = new();
    private readonly Func<int, long, Action, IPresenceProcessWatch?> _watchProcess = watchProcess ?? PresenceProcessWatch.TryCreate;
    private ProfileSnapshot _snapshot = new();
    private IPresenceProcessWatch? _watch;
    private long _generation;
    private bool _online, _disposed;

    internal bool IsOnline { get { lock (_gate) return _online; } }

    internal void Apply(ProfileSnapshot snapshot, long utcNowTicks)
    {
        bool notify;
        lock (_gate)
        {
            if (_disposed) return;
            var sameOwner = _snapshot.MainProcessId == snapshot.MainProcessId
                && _snapshot.MainProcessStartTimeUtcTicks == snapshot.MainProcessStartTimeUtcTicks;
            _snapshot = snapshot;
            if (!sameOwner || !IsFresh(snapshot, utcNowTicks)) ReleaseOwner();
            if (_watch is null && IsFresh(snapshot, utcNowTicks))
            {
                var generation = ++_generation;
                _watch = _watchProcess(snapshot.MainProcessId, snapshot.MainProcessStartTimeUtcTicks,
                    () => OwnerExited(generation));
            }
            var online = IsFresh(snapshot, utcNowTicks) && _watch is { HasExited: false };
            notify = SetOnline(online);
            if (!online) ReleaseOwner();
        }
        if (notify) changed();
    }

    // Called by the companion's existing bounded timer; no file reads or process lookup.
    internal void Evaluate(long utcNowTicks)
    {
        bool notify;
        lock (_gate)
        {
            if (_disposed) return;
            var online = IsFresh(_snapshot, utcNowTicks) && _watch is { HasExited: false };
            notify = SetOnline(online);
            if (!online) ReleaseOwner();
        }
        if (notify) changed();
    }

    internal void Clear()
    {
        bool notify;
        lock (_gate)
        {
            if (_disposed) return;
            _snapshot = new();
            ReleaseOwner();
            notify = SetOnline(false);
        }
        if (notify) changed();
    }

    internal static bool IsFresh(ProfileSnapshot snapshot, long utcNowTicks)
    {
        if (!snapshot.IsAuthenticated || !snapshot.IsOnline || snapshot.MainProcessId <= 0
            || snapshot.MainProcessStartTimeUtcTicks <= 0 || snapshot.MainProcessStartTimeUtcTicks > utcNowTicks
            || snapshot.PresenceUpdatedAtUtcTicks < snapshot.MainProcessStartTimeUtcTicks
            || snapshot.PresenceUpdatedAtUtcTicks > utcNowTicks
            || utcNowTicks > DateTime.MaxValue.Ticks) return false;
        return utcNowTicks - snapshot.PresenceUpdatedAtUtcTicks <= MaximumHeartbeatAge.Ticks;
    }

    private bool SetOnline(bool value)
    {
        if (_online == value) return false;
        _online = value;
        return true;
    }

    private void OwnerExited(long generation)
    {
        bool notify;
        lock (_gate)
        {
            if (_disposed || generation != _generation) return;
            notify = SetOnline(false);
            ReleaseOwner();
        }
        if (notify) changed();
    }

    private void ReleaseOwner()
    {
        ++_generation;
        _watch?.Dispose();
        _watch = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _online = false;
            ReleaseOwner();
        }
    }
}

internal sealed class PresenceProcessWatch : IPresenceProcessWatch
{
    private Process? _process;
    private Action? _exited;
    private PresenceProcessWatch(Process process, Action exited)
    {
        _process = process;
        _exited = exited;
        process.Exited += Process_Exited;
    }
    internal static IPresenceProcessWatch? TryCreate(int processId, long startTicks, Action exited)
    {
        Process? process = null;
        PresenceProcessWatch? watcher = null;
        try
        {
            process = Process.GetProcessById(processId);
            // A recycled PID must never revive the presence of an old app instance.
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != startTicks)
            {
                process.Dispose();
                return null;
            }
            watcher = new(process, exited);
            process.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
        {
            if (watcher is not null) watcher.Dispose(); else process?.Dispose();
            return null;
        }
    }
    public bool HasExited
    {
        get
        {
            try { return _process?.HasExited != false; }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception) { return true; }
        }
    }
    private void Process_Exited(object? sender, EventArgs args) => _exited?.Invoke();
    public void Dispose()
    {
        _exited = null;
        if (Interlocked.Exchange(ref _process, null) is not { } process) return;
        process.Exited -= Process_Exited;
        process.Dispose();
    }
}
