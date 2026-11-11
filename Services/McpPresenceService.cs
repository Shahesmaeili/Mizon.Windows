using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Reads the same snapshot as the header/taskbar. It never starts a connection,
// changes window activation, sends a heartbeat or disposes the shared service.
internal sealed class McpPresenceService(IProfilePresenceService presence) : IMcpPresenceService, IDisposable
{
    private bool _disposed;

    public Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return Result(new() { Code = "unavailable", Message = "The presence connection has closed." });
        if (request.Operation != "get_presence_state")
            return Result(new() { Code = "unknown_operation", Message = "This presence operation is not supported." });
        if (request.Form is not null || request.Page is not null || request.Language is not null
            || request.Appearance is not null || request.Navigation is not null || request.MusicHome is not null)
            return Result(new() { Code = "invalid_request", Message = "Presence state does not accept arguments." });
        try
        {
            var state = presence.ReadState();
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return Result(new() { Code = "unavailable", Message = "The presence connection has closed." });
            return Result(new()
            {
                Success = true, Code = "presence_state", Message = "Current Mizon presence and synchronization state.",
                Presence = new()
                {
                    HasSession = state.HasSession, IsWindowActive = state.IsWindowActive,
                    IsConnected = state.IsConnected, IsOnline = state.IsOnline,
                    HasConfirmedState = state.HasConfirmedState
                }
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (ObjectDisposedException)
        {
            return Result(new() { Code = "unavailable", Message = "The shared presence service has closed." });
        }
        catch (Exception)
        {
            return Result(new() { Code = "operation_failed", Message = "Presence state could not be read." });
        }
    }

    private static Task<McpResponse> Result(McpResponse response) => Task.FromResult(response);
    public void Dispose() => _disposed = true;
}
