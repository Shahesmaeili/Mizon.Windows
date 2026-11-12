using System.Text;
using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp.Tests;

internal static class AuthGamepadTests
{
    public static async Task RoundTrip()
    {
        foreach (var action in Enum.GetValues<McpAuthGamepadAction>())
        {
            using var pipe = new MemoryStream();
            await McpPipeProtocol.WriteAsync(pipe, new McpRequest { Operation = "auth_gamepad_action", GamepadAction = action },
                McpJsonContext.Default.McpRequest, CancellationToken.None);
            pipe.Position = 0;
            var request = await McpPipeProtocol.ReadAsync(pipe, McpJsonContext.Default.McpRequest, CancellationToken.None);
            Check.That(request?.GamepadAction == action, "Controller action changed through generated framing.");
        }
        var state = new McpAuthState { Page = "register", HasPassword = true, Gamepad = new()
        {
            IsControllerConnected = true, IsNavigationActive = true, FocusedFieldId = "register-password",
            IsKeyboardVisible = true, IsHelpVisible = false, IsFeedbackEnabled = true
        } };
        var response = new McpResponse { Success = true, Code = "state", State = state };
        using var output = new MemoryStream();
        await McpPipeProtocol.WriteAsync(output, response, McpJsonContext.Default.McpResponse, CancellationToken.None);
        output.Position = 0;
        var restored = await McpPipeProtocol.ReadAsync(output, McpJsonContext.Default.McpResponse, CancellationToken.None);
        Check.That(restored?.State == state, "Controller snapshot did not round-trip with generated metadata.");
        var json = JsonSerializer.SerializeToElement(response, McpJsonContext.Default.McpResponse);
        SchemaAssertions.Conforms(json, MizonAuthSchemas.Output);
        ProtocolTests.AssertNoSecrets(json);
        foreach (var invalid in new[] { "1", "\"submit\"", "\"accept\"", "\"menu\"", "\"execute_shell\"" })
        {
            await Check.Throws<JsonException>(() => Task.FromResult(JsonSerializer.Deserialize(
                Encoding.UTF8.GetBytes("{\"operation\":\"auth_gamepad_action\",\"gamepadAction\":" + invalid + "}"),
                McpJsonContext.Default.McpRequest)));
        }
    }
}

internal static partial class ConnectionLifecycleTests
{
    public static async Task AuthGamepadTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var tool = tools.Single(item => item.Name == "mizon_auth_gamepad_action").ProtocolTool;
        var values = new[] { "next", "previous", "up", "down", "left", "right", "openKeyboard", "closeKeyboard", "showHelp", "hideHelp", "enableFeedback", "disableFeedback" };
        Check.That(tool.InputSchema.GetProperty("properties").GetProperty("action").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()).SequenceEqual(values), "Controller discovery differs from bounded actions.");
        Check.That(tool.Annotations is { ReadOnlyHint: false, IdempotentHint: false, OpenWorldHint: false },
            "Relative controller actions must not be advertised as retry-safe.");
        var state = tool.OutputSchema!.Value.GetProperty("properties").GetProperty("state").GetProperty("properties")
            .GetProperty("gamepad").GetProperty("properties");
        Check.That(state.GetProperty("focusedFieldId").GetProperty("description").GetString()!.Contains("never its entered text", StringComparison.Ordinal)
            && state.GetProperty("isFeedbackEnabled").GetProperty("description").GetString()!.Contains("does not start vibration", StringComparison.Ordinal)
            && state.EnumerateObject().Count() == 6, "Controller read-state is not bounded and self-described.");
        foreach (var input in new[] { "{}", "{\"action\":1}", "{\"action\":null}", "{\"action\":\"accept\"}",
            "{\"action\":\"submit\"}", "{\"action\":\"menu\"}", "{\"action\":\"next\",\"page\":\"home\"}",
            "{\"action\":\"next\",\"key\":\"A\"}", "{\"action\":\"enableFeedback\",\"duration\":1000}",
            "{\"action\":\"enableFeedback\",\"motor\":1}", "{\"action\":\"toggleFeedback\"}" })
        {
            using var arguments = JsonDocument.Parse(input);
            var rejected = await client.CallToolAsync(new CallToolRequestParams { Name = tool.Name,
                Arguments = arguments.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) }, cancellationToken: timeout.Token);
            Check.That(rejected.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments" && app.Connections == 0,
                "Unsupported controller input reached the app or created consent.");
        }
        var enums = Enum.GetValues<McpAuthGamepadAction>();
        for (var index = 0; index < values.Length; index++)
        {
            using var input = JsonDocument.Parse('"' + values[index] + '"');
            var result = await client.CallToolAsync(new CallToolRequestParams { Name = tool.Name,
                Arguments = new Dictionary<string, JsonElement> { ["action"] = input.RootElement.Clone() } }, cancellationToken: timeout.Token);
            Check.That(result.StructuredContent?.GetProperty("code").GetString() == "approval_required"
                && app.LastRequest is { Operation: "auth_gamepad_action" } && app.LastRequest.GamepadAction == enums[index],
                "Controller action changed through SDK dispatch or bypassed pending consent.");
        }
    }
}
