namespace Mizon.Windows.Mcp.Contracts;

/// <summary>Bounded state from the currently attached VPN page; never connection secrets or device identifiers.</summary>
public sealed record McpVpnState
{
    public bool IsAvailable { get; init; }
    public bool IsConfigured { get; init; }
    public bool IsConnected { get; init; }
    public bool IsBusy { get; init; }
    public string NetworkStatus { get; init; } = "unknown";
    public bool ParticlesEnabled { get; init; }
    public bool MotionAllowed { get; init; }
}

public sealed record McpVpnRequest
{
    public bool? Enabled { get; init; }
}
