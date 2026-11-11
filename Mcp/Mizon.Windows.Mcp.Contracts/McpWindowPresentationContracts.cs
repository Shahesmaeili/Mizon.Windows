namespace Mizon.Windows.Mcp.Contracts;

/// <summary>Main application window only; never exposes position, handles, titles or other windows.</summary>
public sealed record McpWindowPresentationState
{
    public bool IsAvailable { get; init; }
    public bool FullScreen { get; init; }
    public bool ControlsVisible { get; init; }
}
