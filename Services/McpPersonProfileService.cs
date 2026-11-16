using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

internal interface IMcpPersonProfilePage
{
    CancellationToken Lifetime { get; }
    McpPersonProfileState ReadState();
    Task<bool> SetFollowingAsync(bool following, CancellationToken token);
    Task<bool> LoadNextTracksAsync(CancellationToken token);
    bool TryOpenTrack(Guid id);
}

// All calls arrive after in-app approval and on the app dispatcher.
internal sealed class McpPersonProfileService(Func<IMcpPersonProfilePage?> getPage) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closed;

    internal async Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_closed || getPage() is not { } page || page.Lifetime.IsCancellationRequested) return Unavailable();
        var args = request.PersonProfile;
        if (request.Form is not null || request.Page is not null || request.Language is not null || request.Navigation is not null
            || request.Appearance is not null || request.MusicHome is not null || request.Vpn is not null || request.FullScreen is not null
            || request.GamepadAction is not null || !IsValid(request.Operation, args))
            return new() { Code = "invalid_arguments", Message = "Use only the arguments declared for this artist profile tool." };

        var state = page.ReadState();
        if (request.Operation == "get_person_profile")
            return new() { Success = true, Code = "ok", Message = "Active artist profile.", PersonProfile = state };
        if (request.Operation == "open_person_track")
        {
            token.ThrowIfCancellationRequested();
            return page.TryOpenTrack(args!.MusicPublicId!.Value)
                ? new() { Success = true, Code = "opened", Message = "Opened track details." }
                : new() { Code = "track_unavailable", Message = "Choose a track already displayed on this artist profile." };
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, page.Lifetime, _lifetime.Token);
        try
        {
            var success = request.Operation == "set_person_following"
                ? await page.SetFollowingAsync(args!.Following!.Value, linked.Token)
                : await page.LoadNextTracksAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (_closed || !ReferenceEquals(page, getPage())) return Unavailable();
            return new()
            {
                Success = success, Code = success ? "ok" : "action_unavailable",
                Message = success ? "Artist profile action completed." : "The artist profile action could not be completed.",
                PersonProfile = page.ReadState()
            };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Unavailable(); }
        catch (ObjectDisposedException) { return Unavailable(); }
    }

    private static bool IsValid(string operation, McpPersonProfileRequest? args) => operation switch
    {
        "get_person_profile" or "load_person_tracks" => args is null,
        "set_person_following" => args is { Following: not null, MusicPublicId: null },
        "open_person_track" => args is { Following: null, MusicPublicId: Guid id } && id != Guid.Empty,
        _ => false
    };

    private static McpResponse Unavailable() => new() { Code = "page_unavailable", Message = "Open an artist profile in Mizon first." };

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
