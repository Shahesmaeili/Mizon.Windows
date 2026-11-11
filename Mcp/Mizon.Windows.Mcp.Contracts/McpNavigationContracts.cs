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

// The completed invocation's receipt, not a retained current-page snapshot.
// Returned only with navigation read permission; it never authorizes media actions.
public sealed record McpNavigationResult
{
    public int Version { get; init; } = 1;
    public Guid RequestId { get; init; }
    public bool Success { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public McpNavigationResultData? Data { get; init; }
}

public sealed record McpNavigationResultData
{
    public string Destination { get; init; } = string.Empty;
    public string Uri { get; init; } = string.Empty;
    public string CompletionStage { get; init; } = "opened";
    public Guid? PublicId { get; init; }
    public Guid? CommentPublicId { get; init; }
    public string? SettingsSection { get; init; }
}
