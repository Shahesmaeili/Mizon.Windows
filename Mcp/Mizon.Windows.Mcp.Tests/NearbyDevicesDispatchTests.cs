using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp.Tests;

internal static partial class ConnectionLifecycleTests
{
    public static async Task NearbyDevicesTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        const string name = "mizon_get_nearby_devices";
        var tool = tools.Single(item => item.Name == name).ProtocolTool;
        var output = tool.OutputSchema!.Value.GetProperty("properties").GetProperty("nearbyDevices").GetProperty("properties");
        Check.That(tool.Annotations is { ReadOnlyHint: true, IdempotentHint: true }
            && tool.InputSchema.GetProperty("properties").EnumerateObject().Count() == 0
            && output.TryGetProperty("discoveryStatus", out _)
            && output.TryGetProperty("platformCounts", out _), "Discovery advertises a typed, argument-free aggregate read.");
        using var args = JsonDocument.Parse("true");
        var invalid = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = name, Arguments = new Dictionary<string, JsonElement> { ["start"] = args.RootElement.Clone() }
        }, cancellationToken: timeout.Token);
        Check.That(invalid.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments" && app.Connections == 0,
            "Invalid MCP arguments must not create a connection or start discovery.");
        var result = await client.CallToolAsync(new CallToolRequestParams { Name = name }, cancellationToken: timeout.Token);
        Check.That(result.IsError == true && result.StructuredContent?.GetProperty("code").GetString() == "approval_required"
            && app.LastRequest?.Operation == "get_nearby_devices", "Discovery route is gated by in-app consent.");
    }
}
