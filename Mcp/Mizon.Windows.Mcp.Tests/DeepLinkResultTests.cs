using System.Text.Json;
using System.Text.Json.Nodes;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;

namespace Mizon.Windows.Mcp.Tests;

internal static class DeepLinkResultTests
{
    public static Task Protocol()
    {
        var id = Guid.NewGuid();
        var music = Guid.NewGuid();
        var comment = Guid.NewGuid();
        var result = new AppDeepLinkResult
        {
            RequestId = id, Success = true, Code = "opened", Message = "مقصد باز شد.",
            Data = new() { Destination = "music-comment", Uri = $"mizon://music/{music:D}/comments/{comment:D}",
                CompletionStage = "opened", PublicId = music, CommentPublicId = comment }
        };
        var json = AppDeepLinkResultProtocol.Serialize(result);
        Check.That(AppDeepLinkResultProtocol.TryParse(json, id, out var parsed) && parsed == result,
            "Generated protocol lost Unicode, correlation, public identifiers or completion stage.");
        var completed = result with { Code = "completed", Data = new() { Destination = "settings", Uri = "mizon://settings/language",
            CompletionStage = "completed", SettingsSection = "language" } };
        Check.That(AppDeepLinkResultProtocol.TryParse(AppDeepLinkResultProtocol.Serialize(completed), id, out parsed)
            && parsed == completed, "Completed destination receipt did not round-trip.");
        var failed = result with { Success = false, Code = "not_found", Message = "The destination is unavailable.", Data = null };
        Check.That(AppDeepLinkResultProtocol.TryParse(AppDeepLinkResultProtocol.Serialize(failed), id, out parsed)
            && parsed == failed, "A failed result without extra data did not round-trip.");
        Check.That(!AppDeepLinkResultProtocol.TryParse(json, Guid.NewGuid(), out parsed) && parsed is null
            && !AppDeepLinkResultProtocol.TryParse(json, Guid.Empty, out _), "A mismatched or missing correlation was accepted.");

        foreach (var property in new[] { "version", "requestId", "success", "code", "message" })
        {
            var missing = JsonNode.Parse(json)!.AsObject();
            missing.Remove(property);
            Reject(missing.ToJsonString());
        }
        foreach (var property in new[] { "destination", "uri", "completionStage" })
        {
            var missing = JsonNode.Parse(json)!.AsObject();
            missing["data"]!.AsObject().Remove(property);
            Reject(missing.ToJsonString());
        }
        foreach (var (property, value) in new (string, JsonNode?)[]
        {
            ("version", 2), ("version", "1"), ("requestId", Guid.Empty.ToString()),
            ("success", "true"), ("code", "private\nstack"), ("message", new string('x', 513)),
            ("message", null), ("unexpected", "not allowed")
        })
        {
            var invalid = JsonNode.Parse(json)!.AsObject();
            invalid[property] = value;
            Reject(invalid.ToJsonString());
        }
        foreach (var (property, value) in new (string, JsonNode?)[]
        {
            ("completionStage", "queued"), ("destination", "arbitrary secret"),
            ("publicId", Guid.Empty.ToString()), ("commentPublicId", "not a uuid"),
            ("uri", "file:///C:/private"), ("uri", "https://example.com"), ("uri", "mizon://name:secret@home"),
            ("uri", "mizon://home#secret"), ("settingsSection", "account\nsecret"), ("arbitraryData", new JsonObject())
        })
        {
            var invalid = JsonNode.Parse(json)!.AsObject();
            invalid["data"]![property] = value;
            Reject(invalid.ToJsonString());
        }
        Reject(json.Replace("\"success\":true", "\"success\":false,\"success\":true", StringComparison.Ordinal));
        Reject(json.Replace("\"completionStage\":\"opened\"", "\"completionStage\":\"completed\",\"completionStage\":\"opened\"", StringComparison.Ordinal));
        foreach (var malformed in new[] { "", " ", "null", "[]", "{}", "{", json + "{}", "\ud800" }) Reject(malformed);
        var atLimit = json.PadRight(AppDeepLinkResultProtocol.MaximumJsonBytes);
        Check.That(AppDeepLinkResultProtocol.TryParse(atLimit, id, out _), "A valid JSON response at the byte limit was rejected.");
        Reject(atLimit + " ");
        Reject(json.Replace("مقصد", new string('ض', 9000), StringComparison.Ordinal).PadRight(AppDeepLinkResultProtocol.MaximumJsonBytes + 1));
        var utf8Oversized = "{\"message\":\"" + new string('ض', 9000) + "\"}";
        Check.That(utf8Oversized.Length < AppDeepLinkResultProtocol.MaximumJsonBytes,
            "UTF-8 boundary fixture accidentally exceeded the character limit.");
        Reject(utf8Oversized);
        Check.That(!AppDeepLinkResultProtocol.IsValid(result with { Version = 2 })
            && !AppDeepLinkResultProtocol.IsValid(result with { Message = "private\nstack" }), "Producer validation accepted invalid metadata.");
        return Task.CompletedTask;

        void Reject(string value) => Check.That(!AppDeepLinkResultProtocol.TryParse(value, id, out var rejected)
            && rejected is null, "Malformed, oversized or uncorrelated response was accepted.");
    }

    public static async Task McpReceiptRoundTrip()
    {
        var receipt = new McpNavigationResult
        {
            RequestId = Guid.NewGuid(), Success = true, Code = "opened", Message = "Destination opened.",
            Data = new() { Destination = "client", Uri = "mizon://client/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72",
                PublicId = Guid.Parse("0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72"), CompletionStage = "opened" }
        };
        var response = new McpResponse { Success = true, Code = "opened", Message = receipt.Message, NavigationResult = receipt };
        using var stream = new MemoryStream();
        await McpPipeProtocol.WriteAsync(stream, response, McpJsonContext.Default.McpResponse, default);
        stream.Position = 0;
        var roundTrip = await McpPipeProtocol.ReadAsync(stream, McpJsonContext.Default.McpResponse, default);
        Check.That(roundTrip == response, "Typed navigation result changed across actual generated JSON/framing.");
        var json = JsonSerializer.SerializeToElement(response, McpJsonContext.Default.McpResponse);
        SchemaAssertions.Conforms(json, Mizon.Windows.Mcp.MizonAuthSchemas.Output);
        ProtocolTests.AssertNoSecrets(json);
        var properties = Mizon.Windows.Mcp.MizonAuthSchemas.Output.GetProperty("properties").GetProperty("navigationResult").GetProperty("properties");
        Check.That(properties.GetProperty("requestId").GetProperty("type").GetString() == "string"
            && properties.GetProperty("data").GetProperty("properties").GetProperty("completionStage")
                .GetProperty("enum").EnumerateArray().Select(item => item.GetString()).SequenceEqual(["opened", "completed"]),
            "Discovery does not describe the typed correlation or completion stages.");
    }
}
