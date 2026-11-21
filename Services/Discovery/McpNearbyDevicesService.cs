using System;
using System.Linq;
using System.Threading;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Services.Discovery;

internal sealed class McpNearbyDevicesService(Func<NearbyDiscoveryState> readState)
{
    internal McpResponse Read(McpRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request != new McpRequest { Operation = "get_nearby_devices" })
            return new() { Code = "invalid_request", Message = "Nearby discovery state does not accept arguments." };
        var state = readState();
        var platforms = state.Devices.GroupBy(static device => device.Platform, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new McpNearbyPlatformCount(group.Key, group.Count())).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new()
        {
            Success = true,
            Code = "nearby_devices_state",
            Message = "Current local Mizon discovery status and device counts.",
            NearbyDevices = new(state.Status switch
            {
                NearbyDiscoveryStatus.Starting => "starting",
                NearbyDiscoveryStatus.Searching => "searching",
                NearbyDiscoveryStatus.Unavailable => "unavailable",
                _ => "off"
            }, state.Devices.Count, platforms)
        };
    }
}
