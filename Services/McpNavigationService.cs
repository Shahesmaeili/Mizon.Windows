using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

internal sealed class McpNavigationService(IAppDeepLinkNavigation navigation) : IMcpNavigationService, IDisposable
{
    private bool _disposed;

    public async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed) return new() { Code = "unavailable", Message = "The app navigation connection has closed." };
        if (request.Operation == "get_navigation") return Response(true, "navigation", "Current app location.");
        if (request.Operation != "open_destination")
            return new() { Code = "unknown_operation", Message = "This navigation operation is not supported." };
        var input = request.Navigation;
        // URI play/download actions exist for user-invoked shell links, but this
        // permission grants page navigation only, never playback or file writes.
        if (input is null || input.Destination is "music-play" or "music-download"
            || !AppDeepLinkCatalog.TryCreate(input.Destination, input.PublicId, input.CommentPublicId,
                input.SearchQuery, input.SettingsSection, out var destination)
            || destination.MusicAction != MusicActivationAction.Details)
            return new() { Code = "invalid_destination", Message = "Choose a supported destination and only its documented parameters." };
        if (destination.RequiresAuthentication && !navigation.GetState().HasSession)
            return new() { Code = "requires_sign_in", Message = "Sign in before opening this destination. No navigation was queued." };
        try
        {
            var result = await navigation.OpenAsync(destination, token);
            token.ThrowIfCancellationRequested();
            return Response(result.Success, result.Code, result.Message);
        }
        catch (TimeoutException)
        {
            return new() { Code = "page_not_ready", Message = "The app has not finished preparing the requested page." };
        }
    }

    private McpResponse Response(bool success, string code, string message)
    {
        var state = navigation.GetState();
        return new()
        {
            Success = success, Code = code, Message = message,
            Navigation = new()
            {
                Destination = state.Destination, Uri = state.Uri, PendingUri = state.PendingUri,
                HasPendingDestination = state.PendingUri is not null,
                CanGoBack = state.CanGoBack, HasSession = state.HasSession
            }
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (navigation as IDisposable)?.Dispose();
    }
}
