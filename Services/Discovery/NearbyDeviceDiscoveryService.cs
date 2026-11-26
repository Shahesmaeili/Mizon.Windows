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
                (endpoint, name, txt, host) => Update(session, endpoint, name, txt, host), session.Lifetime.Token).ConfigureAwait(false);
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

    private void Update(Session session, string endpoint, string? name, IEnumerable<string>? txt, string? host)
    {
        if (endpoint.Length > 2048) return;
        var peer = name is not null && txt is not null ? NearbyDeviceProtocol.Parse(name, txt, _localId) : null;
        var dnsHost = host?.TrimEnd('.');
        dnsHost = dnsHost is { Length: > 6 and <= 253 } && dnsHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            ? dnsHost.ToUpperInvariant() : null;
        lock (_sync)
        {
            if (!ReferenceEquals(_session, session)) return;
            session.Devices.TryGetValue(endpoint, out var existing);
            if (peer is null)
            {
                if (!session.Devices.Remove(endpoint)) return;
            }
            else
            {
                if (existing is null && session.Devices.Count >= MaximumEndpoints) return;
                var instance = session.Instances.GetValueOrDefault(peer.Id);
                dnsHost ??= instance.Host;
                if (peer == existing?.Device && dnsHost == instance.Host) return;

                var order = ++session.LastObservation;
                session.Instances[peer.Id] = new(dnsHost, instance.FirstSeen == 0 ? order : instance.FirstSeen);
                session.Devices[endpoint] = new(peer, order);
            }

            if (existing is not null && existing.Device.Id != peer?.Id
                && !session.Devices.Values.Any(source => source.Device.Id == existing.Device.Id))
                session.Instances.Remove(existing.Device.Id);

            if (!Publish(session, ReadState().Status)) return;
        }
        Changed?.Invoke();
    }

    private bool Publish(Session session, NearbyDiscoveryStatus status)
    {
        // Each service instance can arrive through both Windows discovery sources.
        // An app restart can register a new instance on the same host before the old one is withdrawn.
        var hosts = new HashSet<(string Platform, string Host, string DeviceName)>();
        var devices = session.Devices.Values
            .GroupBy(static source => source.Device.Id)
            .Select(static group => group.MaxBy(static source => source.Order)!)
            .Select(source => (source.Device, Instance: session.Instances[source.Device.Id]))
            .OrderByDescending(static source => source.Instance.FirstSeen)
            .Where(source => source.Instance.Host is null
                || hosts.Add((source.Device.Platform, source.Instance.Host, source.Device.Name.ToUpperInvariant())))
            .Select(static source => source.Device)
            .OrderBy(static device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static device => device.Id).ToArray();

        if (_state.Status == status && _state.Devices.SequenceEqual(devices)) return false;
        Volatile.Write(ref _state, new(status, Array.AsReadOnly(devices)));
        return true;
    }

    private sealed record Observation(NearbyDevice Device, long Order);
    private readonly record struct Instance(string? Host, long FirstSeen);

    private sealed class Session
    {
        internal readonly CancellationTokenSource Lifetime = new();
        internal readonly Dictionary<string, Observation> Devices = new(StringComparer.Ordinal);
        // Cache a resolved host only while at least one source still reports this instance.
        internal readonly Dictionary<Guid, Instance> Instances = new();
        internal long LastObservation;
    }
}

internal interface INearbyDeviceTransport
{
    Task RunAsync(NearbyDevice local, Action ready, Action<string, string?, IEnumerable<string>?, string?> update, CancellationToken cancellationToken);
}
