using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp.Tests;

internal static partial class ConnectionLifecycleTests
{
    public static async Task PresenceTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var presence = tools.Single(tool => tool.Name == "mizon_get_presence_state").ProtocolTool;
        Check.That(presence.Annotations is { ReadOnlyHint: true, IdempotentHint: true }
            && presence.InputSchema.GetProperty("properties").EnumerateObject().Count() == 0,
            "Presence discovery must be argument-free and read-only.");
        Check.That(!tools.Any(tool => tool.Name.Contains("presence", StringComparison.Ordinal) && tool.Name != "mizon_get_presence_state"),
            "Presence exposes an undeclared mutation tool.");
        Check.That(presence.OutputSchema!.Value.GetProperty("properties").GetProperty("presence")
            .GetProperty("properties").TryGetProperty("hasConfirmedState", out _), "Output does not explain synchronization state.");
        foreach (var field in new[] { "online", "isWindowActive", "activate", "userId", "approve" })
        {
            using var input = JsonDocument.Parse("true");
            var invalid = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = "mizon_get_presence_state", Arguments = new Dictionary<string, JsonElement>
                { [field] = input.RootElement.Clone() }
            }, cancellationToken: timeout.Token);
            Check.That(invalid.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments" && app.Connections == 0,
                "Invalid presence arguments connected to or changed the app.");
        }
        var result = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_get_presence_state" }, cancellationToken: timeout.Token);
        Check.That(result.IsError == true && result.StructuredContent?.GetProperty("code").GetString() == "approval_required"
            && app.LastRequest?.Operation == "get_presence_state", "Presence request bypassed approval or changed its meaning at the transport boundary.");
    }
}
