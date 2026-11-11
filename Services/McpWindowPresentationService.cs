using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// The access service dispatches this adapter to the same UI thread as the native window controls.
internal sealed class McpWindowPresentationService(Func<IWindowPresentationHost?> host)
    : IMcpWindowPresentationService, IDisposable
{
    private bool _disposed;

    public Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Operation is not ("get_window_state" or "set_fullscreen"))
            return Result(new() { Code = "unsupported_operation", Message = "Unsupported window operation." });
        if (request.Form is not null || request.Page is not null || request.Language is not null
            || request.Appearance is not null || request.Navigation is not null || request.MusicHome is not null
            || request.FullScreen.HasValue != (request.Operation == "set_fullscreen"))
            return Result(new() { Code = "invalid_arguments", Message = "Use only the fields declared for this window operation." });
        if (_disposed) return Unavailable();
        try
        {
            var window = host();
            if (window is null || !window.IsAvailable || _disposed) return Unavailable();
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Operation == "set_fullscreen")
                window.SetFullScreen(request.FullScreen!.Value);
            cancellationToken.ThrowIfCancellationRequested();
            if (!window.IsAvailable || _disposed) return Unavailable();
            var state = new McpWindowPresentationState
            {
                IsAvailable = true, FullScreen = window.IsFullScreen,
                ControlsVisible = window.IsFullScreenControlsVisible
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (!window.IsAvailable || _disposed) return Unavailable();
            return Result(new()
            {
                Success = true, Code = request.Operation == "get_window_state" ? "window_state" : "window_updated",
                Message = "Current main-window presentation state.", Window = state
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (ObjectDisposedException) { return Unavailable(); }
        catch (Exception) { return Result(new() { Code = "operation_failed", Message = "The main-window operation could not be completed." }); }
    }

    private static Task<McpResponse> Unavailable() => Result(new()
    {
        Code = "unavailable", Message = "The main window is unavailable.", Window = new() { IsAvailable = false }
    });
    private static Task<McpResponse> Result(McpResponse response) => Task.FromResult(response);
    public void Dispose() => _disposed = true;
}
