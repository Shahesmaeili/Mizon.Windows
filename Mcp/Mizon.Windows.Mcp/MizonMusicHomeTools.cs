using System.Text.Json;
using Mizon.Windows.Mcp.Contracts;

namespace Mizon.Windows.Mcp;

internal sealed partial class MizonAuthTools
{
    private const string MusicHomeRefreshSchema = """
        {"type":"object","properties":{"section":{"type":"string","enum":["all","featured","spotlight","stories","genres","artists","recommended","recent","library","weekly","news","trending","following"]}},"required":["section"],"additionalProperties":false}
        """;

    private const string MusicHomeActionSchema = """
        {"type":"object","properties":{
          "section":{"type":"string","enum":["all","featured","spotlight","stories","artists","recommended","recent","library","weekly","news","trending","following"]},
          "action":{"type":"string","enum":["open","play","pause","resume","previous","next","select","mute","seek","volume","fullscreen","like","follow","copy_link","open_all","show_recent","add_story","dismiss","set_language","request_download","retry","reset_model","profile","open_widget"]},
          "id":{"type":"string","minLength":1,"maxLength":100},
          "index":{"type":"integer","minimum":0,"maximum":255},
          "value":{"type":"number","minimum":0,"maximum":86400},
          "enabled":{"type":"boolean"},
          "language":{"type":"string","enum":["fa-IR","en-US"]}
        },"required":["section","action"],"additionalProperties":false}
        """;

    private static McpRequest ParseMusicHome(string name, IDictionary<string, JsonElement>? args)
    {
        if (name == "mizon_refresh_music_home")
        {
            CheckKeys(args, "section");
            var refresh = new McpRequest
            {
                Operation = "refresh_music_home",
                MusicHome = new() { Section = RequiredString(args, "section", 16) }
            };
            if (!McpMusicHomeContract.IsValid(refresh)) throw new ArgumentException();
            return refresh;
        }

        CheckKeys(args, "section", "action", "id", "index", "value", "enabled", "language");
        var action = RequiredString(args, "action", 24);
        var request = new McpRequest
        {
            Operation = action is "like" or "follow" ? "music_home_submit" : "music_home_action",
            MusicHome = new()
            {
                Section = RequiredString(args, "section", 16), Action = action,
                Id = args!.ContainsKey("id") ? RequiredString(args, "id", 100) : null,
                Index = ReadMusicHomeIndex(args),
                Value = ReadMusicHomeValue(args),
                Enabled = ReadMusicHomeEnabled(args),
                Language = args.ContainsKey("language") ? RequiredString(args, "language", 5) : null
            }
        };
        if (!McpMusicHomeContract.IsValid(request)) throw new ArgumentException();
        return request;
    }

    private static int? ReadMusicHomeIndex(IDictionary<string, JsonElement> args) =>
        !args.TryGetValue("index", out var value) ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number : throw new ArgumentException();

    private static double? ReadMusicHomeValue(IDictionary<string, JsonElement> args) =>
        !args.TryGetValue("value", out var value) ? null
            : value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
                ? number : throw new ArgumentException();

    private static bool? ReadMusicHomeEnabled(IDictionary<string, JsonElement> args) =>
        !args.TryGetValue("enabled", out var value) ? null
            : value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean() : throw new ArgumentException();
}
