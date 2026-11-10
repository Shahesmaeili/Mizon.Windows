namespace Mizon.Windows.Mcp.Contracts;

public sealed record McpTaskbarPinState
{
    public bool IsSupported { get; init; }
    public bool IsPinned { get; init; }
    public bool IsPinningAllowed { get; init; }
    public bool IsBusy { get; init; }
    public bool CanRequest { get; init; }
    public string Code { get; init; } = "unavailable";
}
