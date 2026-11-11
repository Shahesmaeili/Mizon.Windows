using Mizon.Windows.Mcp.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

internal interface IMcpWindowPresentationService
{
    Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken cancellationToken);
}
