using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp.Tests;

internal static partial class ConnectionLifecycleTests
{
    public static async Task VpnTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var command = tools.Single(tool => tool.Name == "mizon_set_vpn_particles").ProtocolTool;
        Check.That(command.InputSchema.GetProperty("required")[0].GetString() == "enabled"
            && command.InputSchema.GetProperty("properties").GetProperty("enabled").GetProperty("type").GetString() == "boolean"
            && command.Annotations is { ReadOnlyHint: false, IdempotentHint: true }, "VPN particles must use an explicit typed boolean.");
        Check.That(command.OutputSchema!.Value.GetProperty("properties").GetProperty("vpn").GetProperty("properties")
            .TryGetProperty("networkStatus", out _), "VPN status is missing from discovery.");
        Check.That(tools.Single(tool => tool.Name == "mizon_open_vpn_settings").ProtocolTool.Annotations
            is { IdempotentHint: false, OpenWorldHint: true }, "Settings launch must not be marked safe to replay.");
        foreach (var json in new[] { "{}", "{\"enabled\":null}", "{\"enabled\":\"true\"}", "{\"enabled\":1}", "{\"enabled\":true,\"uri\":\"ms-settings:privacy\"}" })
        {
            using var input = JsonDocument.Parse(json);
            var invalid = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_set_vpn_particles",
                Arguments = input.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) }, cancellationToken: timeout.Token);
            Check.That(invalid.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments" && app.Connections == 0,
                "Invalid VPN input connected to the application.");
        }
        using var extra = JsonDocument.Parse("\"ms-settings:privacy\"");
        var arbitrary = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_open_vpn_settings",
            Arguments = new Dictionary<string, JsonElement> { ["uri"] = extra.RootElement.Clone() } }, cancellationToken: timeout.Token);
        Check.That(arbitrary.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments" && app.Connections == 0,
            "VPN launch cannot accept arbitrary commands or URIs.");
        foreach (var enabled in new[] { true, false })
        {
            using var input = JsonDocument.Parse(enabled ? "true" : "false");
            var result = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_set_vpn_particles",
                Arguments = new Dictionary<string, JsonElement> { ["enabled"] = input.RootElement.Clone() } }, cancellationToken: timeout.Token);
            Check.That(result.StructuredContent?.GetProperty("code").GetString() == "approval_required"
                && app.LastRequest is { Operation: "set_vpn_particles", Vpn: { } vpn } && vpn.Enabled == enabled,
                "VPN desired state changed meaning or bypassed consent through actual SDK dispatch.");
        }
        foreach (var operation in new[] { "get_vpn_state", "open_vpn_settings" })
        {
            var result = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_" + operation }, cancellationToken: timeout.Token);
            Check.That(result.StructuredContent?.GetProperty("code").GetString() == "approval_required"
                && app.LastRequest?.Operation == operation && app.LastRequest.Vpn is null, "VPN argument-free operation bypassed consent.");
        }
    }
}
