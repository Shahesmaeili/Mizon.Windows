using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp.Tests;

internal static partial class ConnectionLifecycleTests
{
    public static async Task WindowTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var command = tools.Single(tool => tool.Name == "mizon_set_fullscreen").ProtocolTool;
        Check.That(command.InputSchema.GetProperty("required")[0].GetString() == "fullScreen"
            && command.InputSchema.GetProperty("properties").GetProperty("fullScreen").GetProperty("type").GetString() == "boolean"
            && command.Annotations is { ReadOnlyHint: false, IdempotentHint: true }, "Fullscreen input is not explicit and idempotent.");
        Check.That(command.OutputSchema!.Value.GetProperty("properties").GetProperty("window").GetProperty("properties")
            .TryGetProperty("controlsVisible", out _), "Window visibility is missing from discovery.");
        foreach (var json in new[] { "{}", "{\"fullScreen\":null}", "{\"fullScreen\":\"true\"}", "{\"fullScreen\":1}", "{\"fullScreen\":true,\"handle\":1}" })
        {
            using var input = JsonDocument.Parse(json);
            var invalid = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_set_fullscreen",
                Arguments = input.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) }, cancellationToken: timeout.Token);
            Check.That(invalid.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments" && app.Connections == 0,
                "Invalid window input connected to the application.");
        }
        foreach (var enabled in new[] { true, false })
        {
            using var input = JsonDocument.Parse(enabled ? "true" : "false");
            var result = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_set_fullscreen",
                Arguments = new Dictionary<string, JsonElement> { ["fullScreen"] = input.RootElement.Clone() } }, cancellationToken: timeout.Token);
            Check.That(result.StructuredContent?.GetProperty("code").GetString() == "approval_required"
                && app.LastRequest?.Operation == "set_fullscreen" && app.LastRequest.FullScreen == enabled,
                "Fullscreen boolean changed meaning or bypassed consent through actual SDK dispatch.");
        }
        var read = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_get_window_state" }, cancellationToken: timeout.Token);
        Check.That(read.StructuredContent?.GetProperty("code").GetString() == "approval_required"
            && app.LastRequest?.Operation == "get_window_state", "Window read bypassed independent approval.");
    }
}
