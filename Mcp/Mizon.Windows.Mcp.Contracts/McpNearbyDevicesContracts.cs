namespace Mizon.Windows.Mcp.Contracts;

public sealed record McpNearbyDevicesState(
    string DiscoveryStatus, int DeviceCount, McpNearbyPlatformCount[] PlatformCounts,
    McpNearbyMediaTransferState? MediaTransfer = null);

public sealed record McpNearbyPlatformCount(string Platform, int Count);

public sealed record McpNearbyMediaTransferState(bool IsAvailable, long ReceivedBytes, long UploadedBytes);
