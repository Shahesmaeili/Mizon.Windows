using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

// Invoked by McpAccessService on the dispatcher after its independent permission gate.
internal sealed class McpMusicHomeService(Func<IMcpMusicHomePage?> currentPage, Func<bool> hasSession)
    : IMcpMusicHomeService, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private bool _busy;

    public async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed) return Failure("unavailable", "The music home connection has closed.");
        if (!McpMusicHomeContract.IsValid(request))
            return Failure("invalid_request", "Use a documented music home action and its supported parameters.");
        if (!hasSession()) return Failure("requires_sign_in", "Sign in to access the music home page.");
        var page = currentPage();
        if (page is not { IsActive: true })
            return Failure("page_mismatch", "Open the music home destination before using these tools.");
        if (request.Operation == "get_music_home") return State(page, "music_home", "Current music home state.");
        if (request.Operation is not ("refresh_music_home" or "music_home_action" or "music_home_submit"))
            return Failure("unknown_operation", "This music home operation is not supported.");
        if (request.MusicHome is not { } input)
            return Failure("invalid_request", "Provide the documented music home input.");
        if (_busy) return Failure("busy", "Another music home action is still in progress.");
        _busy = true;
        // Explicit playback hands ownership to the music detail/queue. Its own session
        // cancellation still stops superseded/sign-out work; consent/caller cancellation
        // remains linked while unloading the source page must not abort that hand-off.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        using var pageCancellation = page.Lifetime.Register(() =>
        {
            if (input.Action != "play" || !page.IsPlaybackHandoff) linked.Cancel();
        });
        try
        {
            McpResponse response;
            if (request.Operation == "refresh_music_home")
            {
                await page.RefreshAsync(input.Section ?? "all", linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                response = State(page, "refreshed", "Music home refresh completed; inspect each section for errors.");
            }
            else
            {
                response = await page.ExecuteActionAsync(input, linked.Token);
                token.ThrowIfCancellationRequested();
            }
            // Successful navigation intentionally unloads the page. Never retain its old data
            // in the response after navigation/sign-out or a newer page becomes active.
            if (!hasSession()) return Failure("session_changed", "The signed-in session changed.");
            if (ReferenceEquals(page, currentPage()) && page.IsActive)
                return response with { MusicHome = page.GetState() };
            return response with { MusicHome = null };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return Failure(_disposed ? "unavailable" : "page_changed", "The music home operation ended with its page or connection.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Transport replies must not disclose URLs, cached paths, tokens or backend bodies.
            return Failure("operation_failed", "The music home operation could not be completed.");
        }
        finally { _busy = false; }
    }

    private static McpResponse State(IMcpMusicHomePage page, string code, string message) =>
        new() { Success = true, Code = code, Message = message, MusicHome = page.GetState() };
    private static McpResponse Failure(string code, string message) => new() { Code = code, Message = message };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
