namespace Mizon.Windows.Mcp.Contracts;

public sealed record McpNearbyDevicesState(
    string DiscoveryStatus, int DeviceCount, McpNearbyPlatformCount[] PlatformCounts);

public sealed record McpNearbyPlatformCount(string Platform, int Count);
