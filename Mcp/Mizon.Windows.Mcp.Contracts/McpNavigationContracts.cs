namespace Mizon.Windows.Mcp.Contracts;

// Navigation is limited to the app's declared destinations. No raw URI, command,
// media action or arbitrary page type can be supplied through this contract.
public sealed record McpNavigationRequest
{
    public string Destination { get; init; } = string.Empty;
    public Guid? PublicId { get; init; }
    public Guid? CommentPublicId { get; init; }
    public string? SearchQuery { get; init; }
    public string? SettingsSection { get; init; }
}

public sealed record McpNavigationState
{
    public string Destination { get; init; } = "unavailable";
    public string? Uri { get; init; }
    public string? PendingUri { get; init; }
    public bool HasPendingDestination { get; init; }
    public bool CanGoBack { get; init; }
    public bool HasSession { get; init; }
}
