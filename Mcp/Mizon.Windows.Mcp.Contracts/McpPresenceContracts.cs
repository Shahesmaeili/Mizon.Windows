namespace Mizon.Windows.Mcp.Contracts;

/// <summary>Current-user presence only; never includes account IDs, tokens or other users.</summary>
public sealed record McpPresenceState
{
    public bool HasSession { get; init; }
    public bool IsWindowActive { get; init; }
    public bool IsConnected { get; init; }
    public bool IsOnline { get; init; }
    public bool HasConfirmedState { get; init; }
}
