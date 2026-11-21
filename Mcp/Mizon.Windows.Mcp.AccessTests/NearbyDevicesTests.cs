using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services.Discovery;

internal static partial class Program
{
    private static async Task NearbyDevicesConsentAndState()
    {
        var path = Path.Combine(StorageRoot, "nearby-devices", "grants.dat");
        var proof = Proof();
        await using (var host = await Host.StartAsync(path))
        await using (var peer = await host.ConnectAsync(proof))
        {
            var connection = peer.Hello.Connection!;
            var reads = 0;
            var snapshot = new NearbyDiscoveryState(NearbyDiscoveryStatus.Searching,
                [new(Guid.NewGuid(), "macos", "11.0", "private-host", "private-account", "private-display"),
                 new(Guid.NewGuid(), "ios", "11.0", "iPhone", null, null)]);
            host.ReadNearby = () => { reads++; return snapshot; };
            Assert((await peer.Call("get_nearby_devices")) is { Code: "approval_required", NearbyDevices: null } && reads == 0,
                "Nearby network data requires separate user consent");
            await host.Approve(connection.Id, true, true, true, presenceRead: true);
            Assert(!(await peer.Call("get_nearby_devices")).Success && reads == 0,
                "Authentication and presence grants confer no local discovery access");
            await host.Ui(() => host.Service.RevokeAsync(host.Item(connection.Id)));
            await host.Approve(connection.Id, false, false, false, nearbyRead: true);
            var response = await peer.Call("get_nearby_devices");
            Assert(response is { Success: true, NearbyDevices: { DiscoveryStatus: "searching", DeviceCount: 2 } }
                && response.NearbyDevices.PlatformCounts.SequenceEqual([new("ios", 1), new("macos", 1)]),
                "Authorized local discovery MCP wire response matches the shared state");
            Assert(!JsonSerializer.Serialize(response, McpJsonContext.Default.McpResponse).Contains("private-", StringComparison.Ordinal),
                "MCP never returns device names or account metadata");
            var invalid = await peer.Call(new McpRequest { Operation = "get_nearby_devices", Page = "login" });
            Assert(invalid.Code == "invalid_request" && reads == 1, "Local status reads reject mutation payloads");
            Assert(!(await peer.Call("start_nearby_discovery")).Success && !(await peer.Call("get_state")).Success,
                "Discovery consent cannot initiate advertising or read another domain");
            snapshot = NearbyDiscoveryState.Empty(NearbyDiscoveryStatus.Unavailable);
            Assert((await peer.Call("get_nearby_devices")).NearbyDevices is { DiscoveryStatus: "unavailable", DeviceCount: 0 },
                "Transport failure remains distinguishable from an empty discovery list");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                new McpNearbyDevicesService(host.ReadNearby).Read(new() { Operation = "get_nearby_devices" }, cancelled.Token);
                throw new InvalidOperationException("Cancelled discovery read was accepted.");
            }
            catch (OperationCanceledException) { Assert(reads == 2, "Cancelled discovery read never reads the snapshot"); }
        }

        await using var restarted = await Host.StartAsync(path);
        await using var restored = await restarted.ConnectAsync(proof);
        Assert(restored.Hello.Connection is { IsRemembered: true, AllowNearbyDevicesRead: true, AllowRead: false, AllowPresenceRead: false }
            && (await restored.Call("get_nearby_devices")).NearbyDevices is { DiscoveryStatus: "off", DeviceCount: 0 },
            "Remembered discovery scope restores without starting discovery or broadening consent");
        await restarted.Ui(() => restarted.Service.RevokeAsync(restarted.Item(restored.Hello.Connection!.Id)));
        Assert(!(await restored.Call("get_nearby_devices")).Success, "Revocation immediately blocks discovery readback");
    }
}
