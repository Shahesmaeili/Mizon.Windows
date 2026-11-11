using Mizon.Windows.Mcp.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

internal interface IMcpVpnService
{
    Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken cancellationToken);
}

internal interface IMcpVpnPage
{
    bool IsActive { get; }
    CancellationToken Lifetime { get; }
    McpVpnState GetState();
    void SetParticlesEnabled(bool enabled);
    Task<bool> OpenWindowsSettingsAsync(CancellationToken cancellationToken);
}
