using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// The app owns the shared pin service. Disposing this adapter never shuts down
// the profile-menu service, and it never confirms Windows' pin dialog itself.
internal sealed class McpTaskbarPinService(ITaskbarPinService taskbar) : IMcpTaskbarPinService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed) return new() { Code = "unavailable", Message = "The taskbar connection has closed." };
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        McpResponse response;
        if (request.Operation == "get_taskbar_pin_state")
        {
            var state = await taskbar.RefreshAsync(linked.Token);
            response = new() { Success = true, Code = "taskbar_pin_state", Message = "Current Windows taskbar pin status.", TaskbarPin = Snapshot(state) };
        }
        else if (request.Operation == "request_taskbar_pin")
        {
            var result = await taskbar.RequestPinAsync(linked.Token);
            response = new() { Success = result.Success, Code = result.Code, Message = result.Message, TaskbarPin = Snapshot(result.State) };
        }
        else return new() { Code = "unknown_operation", Message = "This taskbar operation is not supported." };
        linked.Token.ThrowIfCancellationRequested();
        return response;
    }

    private static McpTaskbarPinState Snapshot(TaskbarPinState state) => new()
    {
        IsSupported = state.IsSupported, IsPinned = state.IsPinned,
        IsPinningAllowed = state.IsPinningAllowed, IsBusy = state.IsBusy,
        CanRequest = state.CanRequest, Code = state.Code
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
