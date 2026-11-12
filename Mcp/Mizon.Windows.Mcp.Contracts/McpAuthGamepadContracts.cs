using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mizon.Windows.Mcp.Contracts;

// These page-local actions intentionally cannot activate controls or submit forms.
// Consequential authentication still uses the separately authorized submit tool.
[JsonConverter(typeof(McpAuthGamepadActionConverter))]
public enum McpAuthGamepadAction
{
    Next, Previous, Up, Down, Left, Right, OpenKeyboard, CloseKeyboard, ShowHelp, HideHelp
}

public sealed class McpAuthGamepadActionConverter()
    : JsonStringEnumConverter<McpAuthGamepadAction>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed record McpAuthGamepadState
{
    public bool IsControllerConnected { get; init; }
    public bool IsNavigationActive { get; init; }
    public string? FocusedFieldId { get; init; }
    public bool IsKeyboardVisible { get; init; }
    public bool IsHelpVisible { get; init; }
}
