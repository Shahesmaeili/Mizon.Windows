using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Mizon.Windows;
using Windows.Networking.Sockets;

internal static class Program
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static int _assertions;

    private static async Task<int> Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            ("Discovery announcements serialize with generated metadata", AnnouncementSerialization),
            ("Repeated and concurrent starts retain one socket", RepeatedStart),
            ("Stop releases the port for the next page instance", StopAndRebind),
            ("Occupied-port startup cleans up and can recover", BindFailureRecovery),
            ("Cancelled startup does not retain the port", CancelledStartup),
            ("Dispose is idempotent and prevents a later start", Disposal),
            ("Twenty-five login/logout discovery cycles release every socket", RepeatedCycles),
            ("Concurrent start/stop operations settle with no retained socket", ConcurrentLifecycle),
            ("Synthetic packets are parsed and stopped sessions clear peers", PacketAndPeerLifecycle),
            ("Async disposal drains an in-flight discovery callback", InFlightCallbackCleanup)
        ];

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run().WaitAsync(TimeSpan.FromSeconds(30));
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception error)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name}: {error.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failed}/{tests.Length} groups passed; {_assertions} assertions. Actual Windows DatagramSocket, private ephemeral test ports, synthetic peers. No app, UI, authentication or production discovery port accessed.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task RepeatedStart()
    {
        var port = await GetTestPort();
        await using var service = new MulticastDiscoveryServiceV2(port);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => service.StartAsync()));
        await AssertPortBound(port);
        await service.StartAsync();
        await AssertPortBound(port);
        await service.StopAsync();
        await AssertPortAvailable(port);
    }

    private static Task AnnouncementSerialization()
    {
        Assert(!JsonSerializer.IsReflectionEnabledByDefault, "Reflection serialization must stay disabled in this regression suite.");
        var packet = new DiscoveryAnnouncement { DeviceId = "synthetic-device", DeviceName = "Synthetic test peer" };
        var json = JsonSerializer.Serialize(packet, DiscoveryJsonContext.Default.DiscoveryAnnouncement);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert(root.GetProperty("type").GetString() == "discovery" && root.GetProperty("appId").GetString() == "MyLocalShare",
            "The generated announcement must retain the existing discovery wire contract.");
        Assert(root.GetProperty("deviceId").GetString() == packet.DeviceId
            && root.GetProperty("deviceName").GetString() == packet.DeviceName
            && root.GetProperty("port").GetInt32() == 5000, "Generated announcements must contain the intended synthetic peer fields.");
        return Task.CompletedTask;
    }

    private static async Task StopAndRebind()
    {
        var port = await GetTestPort();
        await using var first = new MulticastDiscoveryServiceV2(port);
        await first.StartAsync();
        await first.StopAsync();
        await first.StopAsync();
        await AssertPortAvailable(port);

        await using var nextPage = new MulticastDiscoveryServiceV2(port);
        await nextPage.StartAsync();
        await AssertPortBound(port);
        nextPage.Stop();
        await AssertPortAvailable(port);
        await nextPage.StopAsync();

        await first.StartAsync();
        await first.StopAsync();
        await AssertPortAvailable(port);
    }

    private static async Task BindFailureRecovery()
    {
        using var blocker = new DatagramSocket();
        await blocker.BindServiceNameAsync("0");
        var port = int.Parse(blocker.Information.LocalPort);
        Assert(port != 40404, "Test must never use the production discovery port.");
        await using var service = new MulticastDiscoveryServiceV2(port);
        await ExpectAddressInUse(() => service.StartAsync(), "Startup must report an already occupied port.");
        await service.StopAsync();
        blocker.Dispose();
        await service.StartAsync();
        await AssertPortBound(port);
        await service.StopAsync();
        await AssertPortAvailable(port);
    }

    private static async Task CancelledStartup()
    {
        var port = await GetTestPort();
        await using var service = new MulticastDiscoveryServiceV2(port);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Expect<OperationCanceledException>(() => service.StartAsync(cancellation.Token));
        await AssertPortAvailable(port);
        await service.StartAsync();
        await service.StopAsync();
        await AssertPortAvailable(port);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var immediateCancellation = new CancellationTokenSource();
            var starting = service.StartAsync(immediateCancellation.Token);
            immediateCancellation.Cancel();
            try { await starting; }
            catch (OperationCanceledException) { }
            await service.StopAsync();
            await AssertPortAvailable(port);
        }
    }

    private static async Task Disposal()
    {
        var port = await GetTestPort();
        var service = new MulticastDiscoveryServiceV2(port);
        try
        {
            await service.StartAsync();
            await Task.WhenAll(service.DisposeAsync().AsTask(), service.DisposeAsync().AsTask());
            service.Dispose();
            service.Stop();
            await service.StopAsync();
            await AssertPortAvailable(port);
            await Expect<ObjectDisposedException>(() => service.StartAsync());
            await AssertPortAvailable(port);
        }
        finally { await service.DisposeAsync(); }
    }

    private static async Task RepeatedCycles()
    {
        var port = await GetTestPort();
        for (var cycle = 0; cycle < 25; cycle++)
        {
            await using var pageDiscovery = new MulticastDiscoveryServiceV2(port);
            await pageDiscovery.StartAsync();
            await AssertPortBound(port);
            await pageDiscovery.StopAsync();
            await AssertPortAvailable(port);
        }
    }

    private static async Task ConcurrentLifecycle()
    {
        var port = await GetTestPort();
        await using var service = new MulticastDiscoveryServiceV2(port);
        for (var cycle = 0; cycle < 25; cycle++)
        {
            var starts = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                try { await service.StartAsync(); }
                catch (OperationCanceledException) { } // A concurrent Stop can cancel startup.
            }));
            var stops = Enumerable.Range(0, 4).Select(_ => Task.Run(() => service.StopAsync()));
            await Task.WhenAll(starts.Concat(stops));
            await service.StopAsync();
            await AssertPortAvailable(port);
            await service.StartAsync();
            await AssertPortBound(port);
            await service.StopAsync();
        }
    }

    private static async Task PacketAndPeerLifecycle()
    {
        var port = await GetTestPort();
        await using var service = new MulticastDiscoveryServiceV2(port);
        var firstPeer = "synthetic-" + Guid.NewGuid().ToString("N");
        var discovered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DeviceDiscovered += peer => discovered.TrySetResult(peer.DeviceId);
        await service.StartAsync();
        await SendPacket(port, "malformed synthetic packet");
        await SendPacket(port, "{\"type\":\"unrelated\",\"appId\":\"MyLocalShare\"}");
        await SendPeer(port, firstPeer);
        Assert(await discovered.Task.WaitAsync(Deadline) == firstPeer, "Only the valid synthetic discovery packet should be surfaced.");
        var snapshot = service.Devices;
        Assert(snapshot.Count == 1 && snapshot.Single().DeviceName == "Synthetic test peer", "The received peer must be observable.");
        await service.StopAsync();
        Assert(service.Devices.Count == 0, "Stopped sessions must not retain their peers.");
        Assert(snapshot.Count == 1, "A caller's snapshot must remain safe after discovery stops.");

        discovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await service.StartAsync();
        Assert(service.Devices.Count == 0, "Restarted sessions must begin with no stale peer data.");
        var secondPeer = "synthetic-" + Guid.NewGuid().ToString("N");
        await SendPeer(port, secondPeer);
        Assert(await discovered.Task.WaitAsync(Deadline) == secondPeer, "The restarted socket must receive fresh peers.");
        await service.StopAsync();
        await AssertPortAvailable(port);
    }

    private static Task SendPeer(int port, string deviceId) => SendPacket(port,
        "{\"type\":\"discovery\",\"appId\":\"MyLocalShare\",\"deviceId\":\"" + deviceId +
        "\",\"deviceName\":\"Synthetic test peer\",\"port\":5000}");

    private static async Task InFlightCallbackCleanup()
    {
        var port = await GetTestPort();
        await using var service = new MulticastDiscoveryServiceV2(port);
        using var release = new ManualResetEventSlim();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFinished = false;
        service.DeviceDiscovered += _ =>
        {
            callbackStarted.TrySetResult();
            release.Wait(Deadline);
            callbackFinished = true;
        };
        try
        {
            await service.StartAsync();
            await SendPeer(port, "synthetic-callback-" + Guid.NewGuid().ToString("N"));
            await callbackStarted.Task.WaitAsync(Deadline);
            var disposing = service.DisposeAsync().AsTask();
            await Task.Delay(50);
            Assert(!disposing.IsCompleted, "Async disposal must wait for a callback already in progress.");
            release.Set();
            await disposing.WaitAsync(Deadline);
            Assert(callbackFinished, "No callback may outlive completed async disposal.");
            await AssertPortAvailable(port);
        }
        finally { release.Set(); }
    }

    private static async Task SendPacket(int port, string packet)
    {
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        await sender.SendAsync(Encoding.UTF8.GetBytes(packet), new IPEndPoint(IPAddress.Loopback, port));
    }

    private static async Task<int> GetTestPort()
    {
        using var reservation = new DatagramSocket();
        await reservation.BindServiceNameAsync("0");
        var port = int.Parse(reservation.Information.LocalPort);
        Assert(port != 40404, "Test must never use the production discovery port.");
        return port;
    }

    private static async Task AssertPortAvailable(int port)
    {
        using var probe = new DatagramSocket();
        await probe.BindServiceNameAsync(port.ToString());
        Assert(true, "Stopped discovery must release its bound port.");
    }

    private static async Task AssertPortBound(int port)
    {
        using var probe = new DatagramSocket();
        await ExpectAddressInUse(async () => await probe.BindServiceNameAsync(port.ToString()),
            "An active discovery socket must retain exclusive ownership of its port.");
    }

    private static async Task ExpectAddressInUse(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception error) when (unchecked((uint)error.HResult) == 0x80072740
            || error is SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse })
        {
            Assert(true, message);
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { Assert(true, $"Expected {typeof(T).Name}."); return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }
}
