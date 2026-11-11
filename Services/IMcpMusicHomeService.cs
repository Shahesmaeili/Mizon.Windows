using Mizon.Windows.Mcp.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

internal interface IMcpMusicHomeService
{
    Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken token);
}

// Implemented by the active, cached page. Never create an offscreen view model for MCP.
internal interface IMcpMusicHomePage
{
    bool IsActive { get; }
    CancellationToken Lifetime { get; }
    bool IsPlaybackHandoff { get; }
    McpMusicHomeState GetState();
    Task RefreshAsync(string section, CancellationToken token);
    Task<McpResponse> ExecuteActionAsync(McpMusicHomeRequest request, CancellationToken token);
}
