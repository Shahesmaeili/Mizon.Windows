using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Mizon.Windows;

public sealed class MulticastDiscoveryServiceV2 : IDisposable, IAsyncDisposable
{
    private const string MulticastAddress = "239.255.42.99";
    private const string AppId = "MyLocalShare";
    private const uint MaximumPacketBytes = 4096;
    private const int MaximumDevices = 256;
    private readonly string _deviceId = Guid.NewGuid().ToString();
    private readonly string _deviceName = Environment.MachineName + "- " + Environment.ProcessId;
    private readonly string _port;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Dictionary<string, DeviceInfo> _devices = new();
    private Session? _session;
    private bool _disposed;

    public MulticastDiscoveryServiceV2() : this(40404) { }

    // Isolated tests bind a private port and never interfere with the running app.
    internal MulticastDiscoveryServiceV2(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port.ToString(CultureInfo.InvariantCulture);
    }

    public event Action<DeviceInfo>? DeviceDiscovered;
    public event Action<string>? DeviceLost;

    public IReadOnlyCollection<DeviceInfo> Devices
    {
        get
        {
            lock (_sync)
            {
                var snapshot = new List<DeviceInfo>(_devices.Count);
                foreach (var device in _devices.Values) snapshot.Add(CopyDevice(device));
                return snapshot;
            }
        }
    }

    /// <summary>Starts once. The token cancels startup; Stop ends an established session.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session? previous;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (_session is { Stopping: false }) return;
                previous = _session;
            }

            if (previous is not null) await DrainSessionCoreAsync(previous).ConfigureAwait(false);

            Session session;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                session = new Session();
                try { session.Socket.MessageReceived += OnMessageReceived; }
                catch
                {
                    session.Socket.Dispose();
                    session.Cancellation.Dispose();
                    throw;
                }
                _session = session;
            }

            CancellationToken startupToken = default;
            try
            {
                using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, session.Cancellation.Token);
                startupToken = startupCancellation.Token;
                startupToken.ThrowIfCancellationRequested();
                await session.Socket.BindServiceNameAsync(_port).AsTask(startupToken).ConfigureAwait(false);
                startupToken.ThrowIfCancellationRequested();
                session.Socket.JoinMulticastGroup(new HostName(MulticastAddress));
                startupToken.ThrowIfCancellationRequested();

                lock (_sync)
                {
                    startupToken.ThrowIfCancellationRequested();
                    session.Running = true;
                    // Workers capture this lifetime's socket/token, never mutable service fields.
                    session.AdvertiseTask = Task.Run(() => ObserveWorkerAsync(
                        () => AdvertiseLoopAsync(session), session.Cancellation.Token));
                    session.CleanupTask = Task.Run(() => ObserveWorkerAsync(
                        () => CleanupLoopAsync(session), session.Cancellation.Token));
                }
            }
            catch
            {
                // Failed/canceled binds must release the port before another start can run.
                var startupWasCanceled = startupToken.IsCancellationRequested;
                await DrainSessionCoreAsync(session).ConfigureAwait(false);
                if (startupWasCanceled) throw new OperationCanceledException(startupToken);
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public Task StopAsync() => DrainSessionAsync(RequestStop(disposing: false));

    // Window teardown cannot depend on another dispatcher turn: close transport now,
    // then let the tracked workers drain without blocking the UI or a receive callback.
    public void Stop() => _ = StopAsync();

    public ValueTask DisposeAsync() => new(DrainSessionAsync(RequestStop(disposing: true)));

    public void Dispose() => _ = DisposeAsync();

    private Session? RequestStop(bool disposing)
    {
        lock (_sync)
        {
            if (disposing) _disposed = true;
            var session = _session;
            if (session is not null) CloseSessionLocked(session);
            return session;
        }
    }

    private void CloseSessionLocked(Session session)
    {
        if (session.Stopping) return;
        session.Stopping = true;
        session.Running = false;
        session.Cancellation.Cancel();
        try { session.Socket.MessageReceived -= OnMessageReceived; }
        catch (ObjectDisposedException) { }
        finally { session.Socket.Dispose(); }
        if (session.ActiveReceivers == 0) session.ReceiversDrained.TrySetResult();
    }

    private async Task DrainSessionAsync(Session? session)
    {
        if (session is null) return;
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            // A queued stop belongs to the captured lifetime, not a later restart.
            lock (_sync)
                if (!ReferenceEquals(_session, session)) return;
            await DrainSessionCoreAsync(session).ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    // Called only by the holder of the lifecycle gate.
    private async Task DrainSessionCoreAsync(Session session)
    {
        lock (_sync) CloseSessionLocked(session);
        await Task.WhenAll(session.AdvertiseTask, session.CleanupTask, session.ReceiversDrained.Task)
            .ConfigureAwait(false);
        lock (_sync)
        {
            if (ReferenceEquals(_session, session))
            {
                _session = null;
                _devices.Clear();
            }
            session.Cancellation.Dispose();
        }
    }

    private static async Task ObserveWorkerAsync(Func<Task> action, CancellationToken token)
    {
        try { await action().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // Discovery is optional. Observe faults without logging peer data or crashing sign-in.
            Debug.WriteLine($"Local discovery worker stopped ({exception.GetType().Name}).");
        }
    }

    private async Task AdvertiseLoopAsync(Session session)
    {
        var token = session.Cancellation.Token;
        var json = JsonSerializer.Serialize(new DiscoveryAnnouncement
        {
            DeviceId = _deviceId,
            DeviceName = _deviceName
        }, DiscoveryJsonContext.Default.DiscoveryAnnouncement);

        while (true)
        {
            token.ThrowIfCancellationRequested();
            using var stream = await session.Socket.GetOutputStreamAsync(new HostName(MulticastAddress), _port)
                .AsTask(token).ConfigureAwait(false);
            using var writer = new DataWriter(stream) { UnicodeEncoding = UnicodeEncoding.Utf8 };
            writer.WriteString(json);
            await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
            await writer.FlushAsync().AsTask(token).ConfigureAwait(false);
            await Task.Delay(2000, token).ConfigureAwait(false);
        }
    }

    private void OnMessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
    {
        Session session;
        lock (_sync)
        {
            if (_session is not { Running: true } current || !ReferenceEquals(current.Socket, sender)) return;
            session = current;
            session.ActiveReceivers++;
        }

        try
        {
            using var reader = args.GetDataReader();
            if (reader.UnconsumedBufferLength is 0 or > MaximumPacketBytes) return;
            reader.UnicodeEncoding = UnicodeEncoding.Utf8;
            using var document = JsonDocument.Parse(reader.ReadString(reader.UnconsumedBufferLength),
                new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryReadString(root, "type", 16, out var type) || type != "discovery"
                || !TryReadString(root, "appId", 32, out var appId) || appId != AppId
                || !TryReadString(root, "deviceId", 128, out var deviceId) || deviceId == _deviceId
                || !TryReadString(root, "deviceName", 256, out var deviceName)
                || !root.TryGetProperty("port", out var portValue) || portValue.ValueKind != JsonValueKind.Number
                || !portValue.TryGetInt32(out var port) || port is < 1 or > 65535
                || !IPAddress.TryParse(args.RemoteAddress.CanonicalName, out var ip)) return;

            DeviceInfo? discovered = null;
            lock (_sync)
            {
                if (!session.Running) return;
                if (_devices.TryGetValue(deviceId, out var existing)) existing.LastSeen = DateTime.Now;
                else if (_devices.Count < MaximumDevices)
                {
                    var device = new DeviceInfo
                    {
                        DeviceId = deviceId,
                        DeviceName = deviceName,
                        IP = ip,
                        Port = port,
                        LastSeen = DateTime.Now
                    };
                    _devices.Add(deviceId, device);
                    discovered = CopyDevice(device);
                }
            }
            // Never invoke consumers while holding the device/state lock.
            if (discovered is not null && !session.Cancellation.IsCancellationRequested)
                DeviceDiscovered?.Invoke(discovered);
        }
        catch (JsonException) { }
        catch (Exception exception)
        {
            if (!session.Cancellation.IsCancellationRequested)
                Debug.WriteLine($"Local discovery packet ignored ({exception.GetType().Name}).");
        }
        finally
        {
            lock (_sync)
            {
                session.ActiveReceivers--;
                if (session.Stopping && session.ActiveReceivers == 0) session.ReceiversDrained.TrySetResult();
            }
        }
    }

    private async Task CleanupLoopAsync(Session session)
    {
        var token = session.Cancellation.Token;
        while (true)
        {
            await Task.Delay(3000, token).ConfigureAwait(false);
            var removed = new List<string>();
            lock (_sync)
            {
                if (!session.Running) return;
                foreach (var device in _devices)
                    if (DateTime.Now - device.Value.LastSeen > TimeSpan.FromSeconds(6)) removed.Add(device.Key);
                foreach (var id in removed) _devices.Remove(id);
            }
            foreach (var id in removed)
            {
                token.ThrowIfCancellationRequested();
                DeviceLost?.Invoke(id);
            }
        }
    }

    private static bool TryReadString(JsonElement root, string name, int maximumLength, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength) return false;
        value = text;
        return true;
    }

    private static DeviceInfo CopyDevice(DeviceInfo device) => new()
    {
        DeviceId = device.DeviceId,
        DeviceName = device.DeviceName,
        IP = device.IP,
        Port = device.Port,
        LastSeen = device.LastSeen
    };

    private sealed class Session
    {
        internal DatagramSocket Socket { get; } = new();
        internal CancellationTokenSource Cancellation { get; } = new();
        internal Task AdvertiseTask { get; set; } = Task.CompletedTask;
        internal Task CleanupTask { get; set; } = Task.CompletedTask;
        internal TaskCompletionSource ReceiversDrained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Running { get; set; }
        internal bool Stopping { get; set; }
        internal int ActiveReceivers { get; set; }
    }
}

internal sealed class DiscoveryAnnouncement
{
    [JsonPropertyName("type")] public string Type { get; init; } = "discovery";
    [JsonPropertyName("appId")] public string AppId { get; init; } = "MyLocalShare";
    [JsonPropertyName("deviceId")] public string DeviceId { get; init; } = string.Empty;
    [JsonPropertyName("deviceName")] public string DeviceName { get; init; } = string.Empty;
    [JsonPropertyName("port")] public int Port { get; init; } = 5000;
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(DiscoveryAnnouncement))]
internal partial class DiscoveryJsonContext : JsonSerializerContext;
