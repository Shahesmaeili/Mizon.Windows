using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services.Discovery;

public sealed class NearbyDeviceDiscoveryService : IDisposable
{
    private const int MaximumEndpoints = 128;
    private readonly object _sync = new();
    private readonly Guid _localId = Guid.NewGuid();
    private readonly INearbyDeviceTransport _transport;
    private readonly Action<Exception> _reportError;
    private Session? _session;
    private Task _drain = Task.CompletedTask;
    private NearbyDiscoveryState _state = NearbyDiscoveryState.Empty(NearbyDiscoveryStatus.Off);
    private bool _disposed;

    internal NearbyDeviceDiscoveryService(INearbyDeviceTransport transport, Action<Exception> reportError)
    {
        _transport = transport;
        _reportError = reportError;
    }

    internal event Action? Changed;
    internal NearbyDiscoveryState ReadState() => Volatile.Read(ref _state);

    internal void Start(string version, string deviceName, string? account, string? displayName)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null) return;
            var local = new NearbyDevice(_localId, "windows", version, deviceName, account, displayName);
            var session = _session = new Session();
            var previous = _drain;
            Volatile.Write(ref _state, NearbyDiscoveryState.Empty(NearbyDiscoveryStatus.Starting));
            _drain = Task.Run(() => RunAsync(previous, session, local));
        }
        Changed?.Invoke();
    }

    internal Task StopAsync()
    {
        Task drain;
        bool changed;
        lock (_sync)
        {
            changed = ReadState().Status != NearbyDiscoveryStatus.Off;
            EndSession();
            drain = _drain;
        }
        if (changed) Changed?.Invoke();
        return drain;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            EndSession();
            Changed = null;
        }
    }

    private void EndSession()
    {
        var session = _session;
        _session = null;
        Volatile.Write(ref _state, NearbyDiscoveryState.Empty(NearbyDiscoveryStatus.Off));
        session?.Lifetime.Cancel();
    }

    private async Task RunAsync(Task previous, Session session, NearbyDevice local)
    {
        try
        {
            await previous.ConfigureAwait(false);
            session.Lifetime.Token.ThrowIfCancellationRequested();
            await _transport.RunAsync(local, () => Ready(session),
                (endpoint, name, txt) => Update(session, endpoint, name, txt), session.Lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested) { }
        catch (Exception exception) { _reportError(exception); }
        finally
        {
            bool failed;
            lock (_sync)
            {
                failed = ReferenceEquals(_session, session);
                if (failed)
                {
                    _session = null;
                    Volatile.Write(ref _state, NearbyDiscoveryState.Empty(NearbyDiscoveryStatus.Unavailable));
                }
                session.Lifetime.Dispose();
            }
            if (failed) Changed?.Invoke();
        }
    }

    private void Ready(Session session)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_session, session)) return;
            Publish(session, NearbyDiscoveryStatus.Searching);
        }
        Changed?.Invoke();
    }

    private void Update(Session session, string endpoint, string? name, IEnumerable<string>? txt)
    {
        if (endpoint.Length > 2048) return;
        var peer = name is not null && txt is not null ? NearbyDeviceProtocol.Parse(name, txt, _localId) : null;
        lock (_sync)
        {
            if (!ReferenceEquals(_session, session)) return;
            if (peer is null)
            {
                if (!session.Devices.Remove(endpoint)) return;
            }
            else
            {
                if (session.Devices.TryGetValue(endpoint, out var existing))
                {
                    if (peer == existing) return;
                }
                else if (session.Devices.Count >= MaximumEndpoints) return;
                session.Devices[endpoint] = peer;
            }
            Publish(session, ReadState().Status);
        }
        Changed?.Invoke();
    }

    private void Publish(Session session, NearbyDiscoveryStatus status)
    {
        var devices = session.Devices.Values.DistinctBy(static device => device.Id)
            .OrderBy(static device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static device => device.Id).ToArray();
        Volatile.Write(ref _state, new(status, Array.AsReadOnly(devices)));
    }

    private sealed class Session
    {
        internal readonly CancellationTokenSource Lifetime = new();
        internal readonly Dictionary<string, NearbyDevice> Devices = new(StringComparer.Ordinal);
    }
}

internal interface INearbyDeviceTransport
{
    Task RunAsync(NearbyDevice local, Action ready, Action<string, string?, IEnumerable<string>?> update, CancellationToken cancellationToken);
}
