using System.Text.Json;
using ModelContextProtocol.Protocol;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp.Tests;

internal static partial class ConnectionLifecycleTests
{
    public static async Task NavigationReceiptDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        var tool = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .Single(item => item.Name == "mizon_open_destination").ProtocolTool;
        var publicId = Guid.NewGuid();
        using var destinationInput = JsonDocument.Parse("\"client\"");
        using var idInput = JsonDocument.Parse("\"" + publicId.ToString("D") + "\"");
        foreach (var success in new[] { true, false })
        {
            var receipt = new McpNavigationResult
            {
                RequestId = Guid.NewGuid(), Success = success, Code = success ? "opened" : "not_found", Message = "Synthetic route result.",
                Data = success ? new() { Destination = "client", Uri = $"mizon://client/{publicId:D}",
                    CompletionStage = "opened", PublicId = publicId } : null
            };
            app.NavigationResponse = new() { Success = success, Code = receipt.Code, Message = receipt.Message, NavigationResult = receipt };
            var result = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = "mizon_open_destination", Arguments = new Dictionary<string, JsonElement>
                {
                    ["destination"] = destinationInput.RootElement.Clone(),
                    ["publicId"] = idInput.RootElement.Clone()
                }
            }, cancellationToken: timeout.Token);
            var json = result.StructuredContent!.Value;
            var output = JsonSerializer.Deserialize(json, McpJsonContext.Default.McpResponse);
            Check.That(output?.NavigationResult == receipt && (result.IsError == true) == !success,
                "Actual MCP SDK/stdio/pipe dispatch changed the invocation receipt or success status.");
            SchemaAssertions.Conforms(json, tool.OutputSchema!.Value);
            Check.That(result.Content.OfType<TextContentBlock>().Single().Text == json.GetRawText(),
                "The text tool result differs from the structured navigation receipt.");
        }
    }

    public static async Task NavigationTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        var catalog = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var tool = catalog.Single(item => item.Name == "mizon_open_destination").ProtocolTool;
        var destinations = tool.InputSchema.GetProperty("properties").GetProperty("destination").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        Check.That(destinations.Length == 16 && !destinations.Contains("welcome")
            && !destinations.Contains("music-play") && !destinations.Contains("music-download"),
            "Navigation discovery exposes undeclared or playback destinations.");
        var id = Guid.NewGuid(); var commentId = Guid.NewGuid();
        foreach (var destination in destinations)
        {
            var body = "{\"destination\":\"" + destination + "\"";
            if (destination is "playlist" or "music-details" or "music-comment" or "client" or "story") body += ",\"publicId\":\"" + id + "\"";
            if (destination == "music-comment") body += ",\"commentPublicId\":\"" + commentId + "\"";
            if (destination == "search") body += ",\"searchQuery\":\"آهنگ فارسی\"";
            if (destination == "settings") body += ",\"settingsSection\":\"ai-connections\"";
            var result = await Send(body + "}");
            Check.That(result.IsError == true && result.StructuredContent?.GetProperty("code").GetString() == "approval_required",
                "Pending navigation unexpectedly reached an authorized operation.");
            Check.That(app.LastRequest is { Operation: "open_destination", Navigation: { } request } && request.Destination == destination,
                "Destination was not preserved across actual MCP SDK and named pipe.");
            if (destination == "music-comment") Check.That(app.LastRequest!.Navigation is { PublicId: var music, CommentPublicId: var comment }
                && music == id && comment == commentId, "Comment navigation IDs were corrupted.");
        }
        var previous = app.LastRequest;
        foreach (var invalid in new[]
        {
            "{}", "{\"destination\":\"music-play\"}", "{\"destination\":\"music-download\"}",
            "{\"destination\":\"welcome\"}", "{\"destination\":\"WELCOME\"}",
            "{\"destination\":\"https://example.com\"}", "{\"destination\":\"settings\",\"settingsSection\":\"approve-all\"}",
            "{\"destination\":\"music-details\"}", "{\"destination\":\"music-details\",\"publicId\":\"00000000-0000-0000-0000-000000000000\"}",
            "{\"destination\":\"music\",\"publicId\":\"" + id + "\"}",
            "{\"destination\":\"music-comment\",\"publicId\":\"" + id + "\"}",
            "{\"destination\":\"search\",\"searchQuery\":\"   \"}",
            "{\"destination\":\"search\",\"searchQuery\":\"hello\\nworld\"}",
            "{\"destination\":\"search\",\"searchQuery\":\"" + new string('x', 121) + "\"}",
            "{\"destination\":\"login\",\"password\":\"synthetic-sensitive-input\"}",
            "{\"destination\":\"music-details\",\"publicId\":\"" + id + "\",\"action\":\"play\"}"
        })
        {
            var result = await Send(invalid);
            Check.That(result.IsError == true && result.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments",
                "Invalid navigation was not rejected by the tool boundary.");
            Check.That(ReferenceEquals(previous, app.LastRequest), "Invalid navigation was forwarded to the app.");
            Check.That(!result.StructuredContent!.Value.GetRawText().Contains("synthetic-sensitive-input", StringComparison.Ordinal),
                "Invalid navigation leaked an undeclared input.");
        }
        Check.That(app.Connections == 1, "Navigation requests unnecessarily recreated the connection.");

        async Task<CallToolResult> Send(string body)
        {
            using var json = JsonDocument.Parse(body);
            return await client.CallToolAsync(new CallToolRequestParams
            {
                Name = "mizon_open_destination",
                Arguments = json.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
            }, cancellationToken: timeout.Token);
        }
    }
}
