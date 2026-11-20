namespace Mizon.Windows.Mcp.Contracts;

/// <summary>Current-user presence and aggregate connection counts, without account identities.</summary>
public sealed record McpPresenceState
{
    public bool HasSession { get; init; }
    public bool IsWindowActive { get; init; }
    public bool IsConnected { get; init; }
    public bool IsOnline { get; init; }
    public bool HasConfirmedState { get; init; }
    public string? TaskbarConnection { get; init; }
    public int? OnlineAccountCount { get; init; }
    public int? OnlineFollowerCount { get; init; }
}
