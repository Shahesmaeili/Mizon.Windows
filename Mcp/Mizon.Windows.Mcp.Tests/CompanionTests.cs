using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp.Tests;

internal static class CompanionTests
{
    private const string SecretSentinel = "MCP-test-secret-must-not-be-echoed-157269";
    private static readonly string[] ExpectedTools =
    [
        "mizon_get_connection", "mizon_get_auth_state", "mizon_open_auth_page", "mizon_set_auth_form",
        "mizon_submit_auth_form", "mizon_clear_auth_form", "mizon_retry_welcome", "mizon_set_app_language",
        "mizon_auth_gamepad_action",
        "mizon_get_appearance", "mizon_set_appearance", "mizon_get_navigation", "mizon_open_destination",
        "mizon_get_taskbar_pin_state", "mizon_request_taskbar_pin",
        "mizon_get_music_home", "mizon_refresh_music_home", "mizon_music_home_action", "mizon_get_presence_state",
        "mizon_get_window_state", "mizon_set_fullscreen",
        "mizon_get_vpn_state", "mizon_set_vpn_particles", "mizon_open_vpn_settings"
    ];

    public static async Task Run(string executable, IReadOnlyList<string>? serverArguments = null, bool catalogOnly = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var stderr = new ConcurrentQueue<string>();
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = executable,
            Arguments = serverArguments?.ToArray(),
            Name = "Mizon MCP protocol tests",
            StandardErrorLines = stderr.Enqueue
        }), new McpClientOptions
        {
            ClientInfo = new() { Name = "Mizon.Mcp.ProtocolTests", Title = "Unapproved protocol test connection", Version = "1.0.0" }
        }, cancellationToken: timeout.Token);

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Check.That(tools.Count == ExpectedTools.Length, "The tool catalog does not have the expected authentication, appearance and navigation scope.");
        Check.That(tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).SequenceEqual(ExpectedTools.Order(StringComparer.Ordinal)), "Unexpected tools were exposed.");
        foreach (var tool in tools)
        {
            var definition = tool.ProtocolTool;
            Check.That(definition.InputSchema.GetProperty("additionalProperties").ValueKind == JsonValueKind.False, "Tool accepts undeclared top-level arguments.");
            Check.That(definition.OutputSchema.HasValue, "Tool does not publish its output schema.");
            var output = definition.OutputSchema!.Value;
            Check.That(output.GetProperty("type").GetString() == "object", "Tool result schema must be an object.");
            var properties = output.GetProperty("properties");
            foreach (var name in new[] { "success", "code", "message", "state", "appearance", "navigation", "taskbarPin", "musicHome", "presence", "window", "vpn", "connection" })
                Check.That(properties.TryGetProperty(name, out _), "Output schema does not describe all response fields.");
            var state = properties.GetProperty("state").GetProperty("properties");
            Check.That(!state.TryGetProperty("password", out _) && !state.TryGetProperty("accessToken", out _), "Output schema exposes a secret.");
            Check.That(state.GetProperty("hasSession").GetProperty("description").GetString()!.Contains("not proof", StringComparison.Ordinal), "Session presence must not claim server verification.");
            Check.That(definition.Annotations?.ReadOnlyHint == (tool.Name is "mizon_get_connection" or "mizon_get_auth_state" or "mizon_get_appearance" or "mizon_get_navigation" or "mizon_get_taskbar_pin_state" or "mizon_get_music_home" or "mizon_get_presence_state" or "mizon_get_window_state" or "mizon_get_vpn_state"), "A modifying tool is incorrectly marked read-only.");
            if (tool.Name is "mizon_submit_auth_form" or "mizon_retry_welcome" or "mizon_request_taskbar_pin" or "mizon_refresh_music_home" or "mizon_music_home_action" or "mizon_auth_gamepad_action")
                Check.That(definition.Annotations?.IdempotentHint == false, "A non-idempotent operation is marked safe to repeat.");

            // Every tool rejects unexpected fields locally, before reaching the
            // pipe. Even submit/open therefore cannot launch or mutate the app.
            var invalid = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = tool.Name,
                Arguments = new Dictionary<string, JsonElement> { ["unexpected"] = JsonString(SecretSentinel) }
            }, cancellationToken: timeout.Token);
            AssertResult(invalid, output, "invalid_arguments");
        }

        var form = tools.Single(tool => tool.Name == "mizon_set_auth_form").ProtocolTool.InputSchema
            .GetProperty("properties").GetProperty("form");
        Check.That(form.GetProperty("additionalProperties").ValueKind == JsonValueKind.False, "Form accepts undeclared fields.");
        foreach (var field in new[] { "password", "confirmPassword" })
            Check.That(form.GetProperty("properties").GetProperty(field).GetProperty("writeOnly").GetBoolean(), "Credential input is not documented as write-only.");
        var formOutput = tools.Single(tool => tool.Name == "mizon_set_auth_form").ProtocolTool.OutputSchema!.Value;
        foreach (var field in form.GetProperty("properties").EnumerateObject())
        {
            var limit = field.Value.GetProperty("maxLength").GetInt32();
            var expectedLimit = field.Name switch { "username" => 32, "firstName" or "lastName" => 60, _ => 128 };
            Check.That(limit == expectedLimit, "Published form limit does not match the actual UI.");
            using var document = JsonDocument.Parse("{\"" + field.Name + "\":\"" + new string('x', limit + 1) + "\"}");
            var invalid = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = "mizon_set_auth_form",
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["page"] = JsonString("login"), ["form"] = document.RootElement.Clone()
                }
            }, cancellationToken: timeout.Token);
            AssertResult(invalid, formOutput, "invalid_arguments");
        }

        var appearanceTool = tools.Single(tool => tool.Name == "mizon_set_appearance").ProtocolTool;
        var appearanceInputs = appearanceTool.InputSchema.GetProperty("properties");
        Check.That(appearanceInputs.GetProperty("theme").GetProperty("enum").EnumerateArray().Select(value => value.GetString())
            .SequenceEqual(new[] { "system", "light", "dark" }), "Theme enum discovery differs from supported preferences.");
        Check.That(appearanceInputs.GetProperty("backdrop").GetProperty("enum").GetArrayLength() == 5,
            "Material catalog must describe all supported variants.");
        foreach (var arguments in new Dictionary<string, JsonElement>[]
        {
            [], new() { ["theme"] = JsonString("sepia") }, new() { ["backdrop"] = JsonString("glass") },
            new() { ["theme"] = JsonString("Dark") }, new() { ["theme"] = JsonString("1") }
        })
        {
            var invalid = await client.CallToolAsync(new CallToolRequestParams { Name = "mizon_set_appearance", Arguments = arguments }, cancellationToken: timeout.Token);
            AssertResult(invalid, appearanceTool.OutputSchema!.Value, "invalid_arguments");
        }

        // These are the only valid tool calls. They never launch a GUI, submit
        // credentials, grant permission or alter application state.
        foreach (var name in catalogOnly ? Array.Empty<string>() : ["mizon_get_connection", "mizon_get_auth_state", "mizon_get_appearance", "mizon_get_navigation", "mizon_get_music_home", "mizon_get_presence_state"])
        {
            var result = await client.CallToolAsync(new CallToolRequestParams { Name = name }, cancellationToken: timeout.Token);
            var output = tools.Single(tool => tool.Name == name).ProtocolTool.OutputSchema!.Value;
            var json = AssertResult(result, output);
            Check.That(!json.TryGetProperty("state", out var state) || state.ValueKind == JsonValueKind.Null, "An unapproved connection revealed private form state.");
            Check.That(!json.TryGetProperty("appearance", out var appearance) || appearance.ValueKind == JsonValueKind.Null, "An unapproved connection revealed appearance state.");
            Check.That(!json.TryGetProperty("navigation", out var navigation) || navigation.ValueKind == JsonValueKind.Null, "An unapproved connection revealed navigation state.");
            Check.That(!json.TryGetProperty("presence", out var presence) || presence.ValueKind == JsonValueKind.Null, "An unapproved connection revealed online presence.");
            Check.That(!json.TryGetProperty("musicHome", out var musicHome) || musicHome.ValueKind == JsonValueKind.Null, "An unapproved connection revealed music home data.");
            if (json.TryGetProperty("connection", out var connection))
            {
                Check.That(connection.GetProperty("status").GetString() != "approved", "Test connection was unexpectedly approved.");
                Check.That(!connection.GetProperty("allowRead").GetBoolean() && !connection.GetProperty("allowControl").GetBoolean() && !connection.GetProperty("allowSubmit").GetBoolean(), "An unapproved connection contains permissions.");
            }
            if (name is "mizon_get_auth_state" or "mizon_get_appearance" or "mizon_get_navigation" or "mizon_get_music_home" or "mizon_get_presence_state") Check.That(result.IsError == true, "Private state access succeeded before approval.");
        }
        Check.That(!stderr.Any(line => line.Contains(SecretSentinel, StringComparison.Ordinal)), "The companion logged a credential value.");
    }

    private static JsonElement AssertResult(CallToolResult result, JsonElement schema, string? expectedCode = null)
    {
        Check.That(result.StructuredContent.HasValue, "Tool result lacks structured content.");
        var json = result.StructuredContent!.Value;
        ProtocolTests.AssertNoSecrets(json);
        Check.That(!json.GetRawText().Contains(SecretSentinel, StringComparison.Ordinal), "Tool result echoed a credential value.");
        var text = result.Content.OfType<TextContentBlock>().SingleOrDefault();
        Check.That(text?.Text == json.GetRawText(), "Text and structured tool results differ.");
        SchemaAssertions.Conforms(json, schema);
        Check.That((result.IsError == true) != json.GetProperty("success").GetBoolean(), "MCP isError and application success disagree.");
        if (expectedCode is not null)
            Check.That(json.GetProperty("code").GetString() == expectedCode, "Tool returned an unexpected error category.");
        return json;
    }

    private static JsonElement JsonString(string value)
    {
        // Synthetic value has no escape characters. No reflection serializer.
        using var json = JsonDocument.Parse('"' + value + '"');
        return json.RootElement.Clone();
    }
}
