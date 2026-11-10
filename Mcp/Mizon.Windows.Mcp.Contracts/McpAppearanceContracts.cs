using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mizon.Windows.Mcp.Contracts;

[JsonConverter(typeof(McpAppearanceThemeConverter))]
public enum McpAppearanceTheme { System, Light, Dark }
[JsonConverter(typeof(McpAppearanceBackdropConverter))]
public enum McpAppearanceBackdrop { Acrylic, AcrylicBase, AcrylicThin, Mica, MicaAlt }
[JsonConverter(typeof(McpAppearanceFallbackConverter))]
public enum McpAppearanceFallback { None, WindowUnavailable, HighContrast, TransparencyDisabled, Unsupported, Unavailable }

public sealed class McpAppearanceThemeConverter() : JsonStringEnumConverter<McpAppearanceTheme>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);
public sealed class McpAppearanceBackdropConverter() : JsonStringEnumConverter<McpAppearanceBackdrop>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);
public sealed class McpAppearanceFallbackConverter() : JsonStringEnumConverter<McpAppearanceFallback>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed record McpAppearancePreferences
{
    public McpAppearanceTheme? Theme { get; init; }
    public McpAppearanceBackdrop? Backdrop { get; init; }
}

public sealed record McpAppearanceState
{
    public McpAppearanceTheme Theme { get; init; }
    public McpAppearanceTheme EffectiveTheme { get; init; }
    public McpAppearanceBackdrop Backdrop { get; init; }
    public McpAppearanceBackdrop? EffectiveBackdrop { get; init; }
    public McpAppearanceFallback FallbackReason { get; init; }
    public bool IsHighContrast { get; init; }
    public bool IsTransparencyEnabled { get; init; }
    public bool IsMicaSupported { get; init; }
    public bool IsAcrylicSupported { get; init; }
    public bool IsPreferenceSaved { get; init; }
    public bool IsWindowAttached { get; init; }
}
