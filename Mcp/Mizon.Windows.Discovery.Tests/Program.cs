using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services.Discovery;
using System.Collections.Concurrent;
using System.Text;

if (args is ["--wire"] or ["--apple-peer"] or ["--lan-media-client", "0" or "1"]
    or ["--direct-media-client", "0" or "1", _, _, _])
{
    try
    {
        await NativeMediaFixture.RunAsync(advertise: args[0] == "--apple-peer",
            receiveIndex: args[0] is "--lan-media-client" or "--direct-media-client" ? int.Parse(args[1]) : null,
            directAddress: args is ["--direct-media-client", _, var host, var port, var fingerprint]
                ? (new System.Net.IPEndPoint(System.Net.IPAddress.Parse(host), int.Parse(port)), Convert.FromHexString(fingerprint)) : null);
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("The LAN probe deadline expired.");
        Environment.ExitCode = 1;
    }
    return;
}

var id = Guid.Parse("d9714af0-350c-48b9-9c72-e3930e906cc9");
if (args is ["--advertise", var duration] && int.TryParse(duration, out var seconds) && seconds is >= 1 and <= 180)
{
    using var native = new NearbyDeviceDiscoveryService(new WindowsNearbyDeviceTransport(), error => Console.Error.WriteLine(error));
    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    native.Changed += () =>
    {
        if (native.ReadState().Status == NearbyDiscoveryStatus.Searching) ready.TrySetResult();
        else if (native.ReadState().Status == NearbyDiscoveryStatus.Unavailable)
            ready.TrySetException(new InvalidOperationException("Native discovery could not start."));
    };
    native.Start("11.0-test", "Mizon Windows probe", "windows-probe", "Windows Probe");
    await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
    Console.WriteLine($"LAN Windows probe ready for {seconds} seconds");
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    await native.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine("LAN Windows probe stopped");
    return;
}
if (args is ["--lan", var remoteId] && Guid.TryParse(remoteId, out var expected))
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    using var native = new NearbyDeviceDiscoveryService(new WindowsNearbyDeviceTransport(),
        error => Console.Error.WriteLine(error));
    var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    native.Changed += () =>
    {
        var state = native.ReadState();
        Console.WriteLine($"LAN {state.Status}: {string.Join(", ", state.Devices.Select(peer => $"{peer.Id:D} {peer.Platform} {peer.Name}"))}");
        if (state.Devices.Any(peer => peer.Id == expected))
            observed.TrySetResult();
        if (state.Status is NearbyDiscoveryStatus.Unavailable)
            observed.TrySetException(new InvalidOperationException("Native discovery could not start."));
    };
    native.Start("11.0-test", "Mizon Windows probe", "windows-probe", "Windows Probe");
    await observed.Task.WaitAsync(timeout.Token);
    Console.WriteLine("PASS native Windows detected the expected Bonjour peer");
    await Task.Delay(TimeSpan.FromSeconds(18), timeout.Token);
    await native.StopAsync().WaitAsync(timeout.Token);
    Check(native.ReadState().Status == NearbyDiscoveryStatus.Off, "native Windows withdraws the session");
    return;
}

var self = Guid.NewGuid();
var device = new NearbyDevice(id, "macos", "11.0", "مک آلیس", "alice", "آلیس");
var properties = NearbyDeviceProtocol.ToTxt(device).Select(pair => $"{pair.Key}={pair.Value}").ToArray();
var name = NearbyDeviceProtocol.FullName(id);
Check(NearbyDeviceProtocol.Parse(name, properties, self) == device, "Swift/Windows DNS-SD fields survive UTF-8 round trip");
Check(NearbyDeviceProtocol.Parse(name.TrimEnd('.'), properties, self) == device, "Windows normalized hostname works");
Check(NearbyDeviceProtocol.Parse(name, properties, id) is null, "own instance is excluded");
Check(NearbyDeviceProtocol.Parse(NearbyDeviceProtocol.FullName(Guid.NewGuid()), properties, self) is null, "mismatched instance ID is rejected");
Check(NearbyDeviceProtocol.Parse(name, [.. properties, "v=2"], self) is null, "duplicate/conflicting keys are rejected");
Check(NearbyDeviceProtocol.Parse(name, properties.Select(x => x == "v=1" ? "v=2" : x), self) is null, "future incompatible contract is ignored");
Check(NearbyDeviceProtocol.Parse(name, properties.Select(x => x.StartsWith("device=") ? "device=" + new string('ب', 49) : x), self) is null, "receive byte cap is enforced");
var unicodeName = string.Concat(Enumerable.Repeat("😀", 49)) + "\r\n";
var wire = NearbyDeviceProtocol.ToTxt(device with { Name = unicodeName, DisplayName = "\u202eآلیس" });
Check(Encoding.UTF8.GetByteCount(wire["device"]) == 96 && !wire["device"].Contains('\uFFFD')
    && wire["display"] == "آلیس", "advertisement limits preserve Unicode and strip direction overrides");

var transport = new SyntheticTransport();
var errors = new ConcurrentQueue<Exception>();
using var service = new NearbyDeviceDiscoveryService(transport, errors.Enqueue);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
Check(service.ReadState().Status == NearbyDiscoveryStatus.Off && transport.Runs == 0, "service starts off without socket");
service.Start("11.0", "Windows probe", "bob", null);
service.Start("11.0", "Windows probe", "bob", null);
var first = await transport.Started.Reader.ReadAsync(deadline.Token);
Check(service.ReadState().Status == NearbyDiscoveryStatus.Searching && transport.Runs == 1, "explicit start is idempotent");
first.Update("wifi", name, properties);
first.Update("ethernet", name, properties);
first.Update("self", NearbyDeviceProtocol.FullName(first.Local.Id),
    NearbyDeviceProtocol.ToTxt(first.Local).Select(pair => $"{pair.Key}={pair.Value}"));
Check(service.ReadState().Devices.SequenceEqual([device]), "different accounts visible; two adapters deduplicated; self hidden");
first.Update("wifi", null, null);
Check(service.ReadState().Devices.Count == 1, "another interface keeps a peer visible");
first.Update("ethernet", null, null);
Check(service.ReadState().Devices.Count == 0, "last removal clears the row");

var restartedDevice = device with { Id = Guid.NewGuid(), AppVersion = "11.1" };
var restartedName = NearbyDeviceProtocol.FullName(restartedDevice.Id);
var restartedProperties = NearbyDeviceProtocol.ToTxt(restartedDevice).Select(pair => $"{pair.Key}={pair.Value}").ToArray();
first.Update("old-winrt", name, properties);
first.Update("old-dns", name, properties, "Test-Mac.local.");
first.Update("new-dns", restartedName, restartedProperties, "test-mac.LOCAL");
first.Update("new-winrt", restartedName, restartedProperties);
Check(service.ReadState().Devices.SequenceEqual([restartedDevice]), "a restart changes the instance ID without duplicating the device");
var deduplicated = service.ReadState();
first.Update("old-late", name, properties, "test-mac.local");
Check(ReferenceEquals(service.ReadState(), deduplicated), "a late observation of the old instance cannot replace the current row");
first.Update("old-late", null, null);

var otherMac = device with { Id = Guid.NewGuid() };
var otherName = NearbyDeviceProtocol.FullName(otherMac.Id);
var otherProperties = NearbyDeviceProtocol.ToTxt(otherMac).Select(pair => $"{pair.Key}={pair.Value}").ToArray();
first.Update("other-mac", otherName, otherProperties, "another-mac.local");
Check(service.ReadState().Devices.Count == 2, "different DNS hosts with the same label and account remain separate");

var nearbyMcp = new McpNearbyDevicesService(service.ReadState);
var nearbyRequest = new McpRequest { Operation = "get_nearby_devices" };
Check(nearbyMcp.Read(nearbyRequest, deadline.Token).NearbyDevices is { DeviceCount: 2 } nearby
    && nearby.PlatformCounts.SequenceEqual([new("macos", 2)]), "MCP reports the same deduplicated platform counts as the header");
first.Update("old-dns", null, null);
Check(service.ReadState().Devices.Count == 2, "the remaining source retains the last resolved host without reviving a duplicate");
first.Update("old-winrt", null, null);
Check(service.ReadState().Devices.Count == 2 && service.ReadState().Devices.Contains(restartedDevice),
    "withdrawal of the previous instance keeps the current row");
first.Update("new-dns", null, null);
Check(service.ReadState().Devices.Count == 2, "a remaining source keeps the current row visible");
first.Update("new-winrt", null, null);
first.Update("other-mac", null, null);
Check(nearbyMcp.Read(nearbyRequest, deadline.Token).NearbyDevices is { DeviceCount: 0 }, "withdrawals clear the MCP count");

for (var i = 0; i < 150; i++)
{
    var peer = device with { Id = Guid.NewGuid() };
    first.Update(i.ToString(), NearbyDeviceProtocol.FullName(peer.Id), NearbyDeviceProtocol.ToTxt(peer).Select(pair => $"{pair.Key}={pair.Value}"));
}
Check(service.ReadState().Devices.Count == 128, "discovery state is bounded");
var stop = service.StopAsync();
Check(service.ReadState() is { Status: NearbyDiscoveryStatus.Off, Devices.Count: 0 }, "stop immediately hides peers");
service.Start("11.0", "Windows probe", "new-login", null);
await stop.WaitAsync(deadline.Token);
var second = await transport.Started.Reader.ReadAsync(deadline.Token);
first.Update("late", name, properties);
Check(service.ReadState().Devices.Count == 0 && second.Local.Account == "new-login" && transport.MaximumActive == 1,
    "relogin is drained; stale callbacks cannot cross sessions");
second.Failed.TrySetException(new IOException("Synthetic multicast failure."));
while (service.ReadState().Status != NearbyDiscoveryStatus.Unavailable)
    await Task.Delay(10, deadline.Token);
Check(errors.Count == 1, "transport failure produces a retryable state");
service.Start("11.0", "Windows probe", null, null);
var last = await transport.Started.Reader.ReadAsync(deadline.Token);
Check(last.Local.Account is null, "a retry starts a fresh session");
await service.StopAsync().WaitAsync(deadline.Token);
Check(transport.Active == 0, "transport owned by stopped service is released");
service.Dispose();
service.Dispose();
try
{
    service.Start("11.0", "Windows probe", null, null);
    throw new InvalidOperationException("Disposed service accepted start.");
}
catch (ObjectDisposedException) { Console.WriteLine("PASS idempotent disposal rejects restart"); }

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}

internal sealed class SyntheticTransport : INearbyDeviceTransport
{
    internal readonly System.Threading.Channels.Channel<Run> Started = System.Threading.Channels.Channel.CreateUnbounded<Run>();
    internal int Runs;
    internal int Active;
    internal int MaximumActive;

    public async Task RunAsync(NearbyDevice local, Action ready, Action<string, string?, IEnumerable<string>?, string?> update, CancellationToken cancellationToken)
    {
        MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref Active));
        Interlocked.Increment(ref Runs);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ready();
        Started.Writer.TryWrite(new(local, update, failed));
        try { await failed.Task.WaitAsync(cancellationToken); }
        finally { Interlocked.Decrement(ref Active); }
    }

    internal sealed record Run(NearbyDevice Local, Action<string, string?, IEnumerable<string>?, string?> Publish, TaskCompletionSource Failed)
    {
        internal void Update(string endpoint, string? name, IEnumerable<string>? txt, string? host = null) =>
            Publish(endpoint, name, txt, host);
    }
}
