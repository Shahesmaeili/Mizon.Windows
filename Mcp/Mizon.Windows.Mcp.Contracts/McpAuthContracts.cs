using System.Text.Json.Serialization;

namespace Mizon.Windows.Mcp.Contracts;

public sealed record McpRequest
{
    public string Operation { get; init; } = string.Empty;
    public string? ClientName { get; init; }
    public string? ClientTitle { get; init; }
    public string? ClientVersion { get; init; }
    // Pipe handshake only. Never expose this proof through an MCP tool/result/log.
    public string? ClientCredential { get; init; }
    public string? Page { get; init; }
    public string? Language { get; init; }
    public McpAuthForm? Form { get; init; }
    public McpAppearancePreferences? Appearance { get; init; }
    public McpNavigationRequest? Navigation { get; init; }
    public McpMusicHomeRequest? MusicHome { get; init; }
    public bool? FullScreen { get; init; }
}

// Input only. Never include this object in a response, diagnostic or exception log.
public sealed record McpAuthForm
{
    public string? Identifier { get; init; }
    public string? Password { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? ConfirmPassword { get; init; }
}

public sealed record McpResponse
{
    public bool Success { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public McpAuthState? State { get; init; }
    public McpAppearanceState? Appearance { get; init; }
    public McpNavigationState? Navigation { get; init; }
    public McpNavigationResult? NavigationResult { get; init; }
    public McpTaskbarPinState? TaskbarPin { get; init; }
    public McpMusicHomeState? MusicHome { get; init; }
    public McpPresenceState? Presence { get; init; }
    public McpWindowPresentationState? Window { get; init; }
    public McpConnectionStatus? Connection { get; init; }
}

public sealed record McpConnectionStatus
{
    public string Id { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string Status { get; init; } = "pending";
    public bool AllowRead { get; init; }
    public bool AllowControl { get; init; }
    public bool AllowSubmit { get; init; }
    public bool AllowAppearanceRead { get; init; }
    public bool AllowAppearanceControl { get; init; }
    public bool AllowNavigationRead { get; init; }
    public bool AllowNavigationControl { get; init; }
    public bool AllowTaskbarRead { get; init; }
    public bool AllowTaskbarControl { get; init; }
    public bool AllowMusicHomeRead { get; init; }
    public bool AllowMusicHomeControl { get; init; }
    public bool AllowMusicHomeSubmit { get; init; }
    public bool AllowPresenceRead { get; init; }
    public bool AllowWindowRead { get; init; }
    public bool AllowWindowControl { get; init; }
    public bool IsRemembered { get; init; }
    public bool IsConnected { get; init; }
}

public sealed record McpAuthState
{
    public string Page { get; init; } = "unavailable";
    public string Language { get; init; } = "fa-IR";
    public bool HasSession { get; init; }
    public bool IsBusy { get; init; }
    public bool CanSubmit { get; init; }
    public bool ControlsEnabled { get; init; }
    public bool HasError { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Identifier { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public bool HasPassword { get; init; }
    public bool HasConfirmPassword { get; init; }
    public string? WelcomeStage { get; init; }
    public string? StatusTitle { get; init; }
    public string? StatusDetail { get; init; }
    public string? DisplayName { get; init; }
    public bool HasProfileImage { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpRequest))]
[JsonSerializable(typeof(McpResponse))]
public sealed partial class McpJsonContext : JsonSerializerContext;
