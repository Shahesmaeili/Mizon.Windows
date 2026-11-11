using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;
using ModelContextProtocol.Protocol;

namespace Mizon.Windows.Mcp.Tests;

internal static partial class ConnectionLifecycleTests
{
    public static async Task MusicHomeTypedDispatch()
    {
        await using var app = new SyntheticApp(TimeSpan.Zero);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await CreateFixtureClientAsync(timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var schema = tools.Single(item => item.Name == "mizon_music_home_action").ProtocolTool.InputSchema;
        Check.That(schema.GetProperty("properties").GetProperty("action").GetProperty("enum").GetArrayLength() == 24,
            "Music home discovery omitted supported actions.");
        var cases = new (string Tool, string Arguments, string Operation)[]
        {
            ("mizon_get_music_home", "{}", "get_music_home"),
            ("mizon_refresh_music_home", """{"section":"all"}""", "refresh_music_home"),
            ("mizon_refresh_music_home", """{"section":"genres"}""", "refresh_music_home"),
            ("mizon_music_home_action", """{"section":"featured","action":"seek","value":91.75}""", "music_home_action"),
            ("mizon_music_home_action", """{"section":"featured","action":"volume","value":0.45}""", "music_home_action"),
            ("mizon_music_home_action", """{"section":"featured","action":"mute","enabled":true}""", "music_home_action"),
            ("mizon_music_home_action", """{"section":"stories","action":"like","enabled":false}""", "music_home_submit"),
            ("mizon_music_home_action", """{"section":"stories","action":"open_widget","id":"5fe68f29-5928-4bd9-9251-c8ca8c640ca5"}""", "music_home_action"),
            ("mizon_music_home_action", """{"section":"spotlight","action":"follow","enabled":true}""", "music_home_submit"),
            ("mizon_music_home_action", """{"section":"recommended","action":"profile","id":"demo-music-1","index":1}""", "music_home_action"),
            ("mizon_music_home_action", """{"section":"library","action":"open","id":"5fe68f29-5928-4bd9-9251-c8ca8c640ca5"}""", "music_home_action"),
            ("mizon_music_home_action", """{"section":"weekly","action":"select","index":2}""", "music_home_action"),
            ("mizon_music_home_action", """{"section":"news","action":"pause"}""", "music_home_action"),
            ("mizon_music_home_action", """{"section":"all","action":"set_language","language":"en-US"}""", "music_home_action")
        };
        foreach (var item in cases)
        {
            var result = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = item.Tool, Arguments = Arguments(item.Arguments)
            }, cancellationToken: timeout.Token);
            Check.That(result.IsError == true && result.StructuredContent?.GetProperty("code").GetString() == "approval_required",
                "Pending music home operation bypassed consent.");
            Check.That(app.LastRequest?.Operation == item.Operation && McpMusicHomeContract.IsValid(app.LastRequest),
                "The SDK tool changed music-home input meaning or the required scope across the pipe.");
            if (item.Arguments.Contains("91.75", StringComparison.Ordinal))
                Check.That(app.LastRequest!.MusicHome!.Value == 91.75, "Fractional seek seconds were lost.");
            if (item.Arguments.Contains("demo-music-1", StringComparison.Ordinal))
                Check.That(app.LastRequest!.MusicHome is { Id: "demo-music-1", Index: 1 }, "Contributor selection lost typed fields.");
        }

        foreach (var json in new[]
        {
            "{}", """{"section":"genres","action":"open","id":"pop"}""",
            """{"section":"featured","action":"seek","value":-1}""",
            """{"section":"featured","action":"seek","value":1e300}""",
            """{"section":"featured","action":"volume","value":1.1}""",
            """{"section":"weekly","action":"select","index":1.5}""",
            """{"section":"weekly","action":"select","index":256}""",
            """{"section":"weekly","action":"select","index":null}""",
            """{"section":"featured","action":"like"}""",
            """{"section":"featured","action":"like","enabled":"true"}""",
            """{"section":"featured","action":"play","enabled":true}""",
            """{"section":"library","action":"open"}""",
            """{"section":"library","action":"open","id":"\u0000"}""",
            """{"section":"recommended","action":"profile","id":"demo","index":2}""",
            """{"section":"all","action":"set_language","language":"de-DE"}""",
            """{"section":"featured","action":"request_download","path":"C:\\private"}""",
            """{"section":"featured","action":"execute","uri":"https://example.test"}""",
            """{"section":"featured","action":"play","approve":true}"""
        })
        {
            var prior = app.LastRequest;
            var invalid = await client.CallToolAsync(new CallToolRequestParams
            {
                Name = "mizon_music_home_action", Arguments = Arguments(json)
            }, cancellationToken: timeout.Token);
            Check.That(invalid.StructuredContent?.GetProperty("code").GetString() == "invalid_arguments"
                && ReferenceEquals(prior, app.LastRequest), "Invalid action reached the app or returned the wrong category.");
        }
        Check.That(app.Connections == 1, "Music home calls recreated the existing consent session.");

        static Dictionary<string, JsonElement> Arguments(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        }
    }
}
