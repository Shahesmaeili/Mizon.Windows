using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

internal interface IMcpMusicDetailsPage
{
    McpMusicDetailsState ReadState();
    bool SetCoverOpen(bool open);
    void SetRepeatEnabled(bool enabled);
    bool SetAudience(string view);
    bool SetWaveformCommentPreview(Guid? commentPublicId);
    McpMusicDiscussionState? ReadDiscussion() => null;
    Task<McpResponse> ExecuteDiscussionAsync(McpMusicDiscussionRequest request, CancellationToken token) =>
        Task.FromResult(new McpResponse { Code = "discussion_unavailable", Message = "The active track discussion is unavailable." });
}

// Called only by the authorized UI dispatcher for the currently displayed track.
internal sealed class McpMusicDetailsService(Func<IMcpMusicDetailsPage?> getPage)
{
    internal async Task<McpResponse> ExecuteDiscussionAsync(McpRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Form is not null || request.Page is not null || request.Language is not null || request.Navigation is not null ||
            request.Appearance is not null || request.MusicHome is not null || request.Vpn is not null || request.FullScreen is not null ||
            request.GamepadAction is not null || request.PersonProfile is not null || request.MusicDetails is not null)
            return new() { Code = "invalid_arguments", Message = "Specify only one track discussion action." };
        if (request.Operation == "get_music_discussion" && request.MusicDiscussion is null)
        {
            return getPage()?.ReadDiscussion() is { } state
                ? new() { Success = true, Code = "ok", Message = "Active track discussion.", MusicDiscussion = state }
                : new() { Code = "page_unavailable", Message = "Open track details in Mizon first." };
        }
        if (request.Operation != "manage_music_discussion" || request.MusicDiscussion is not { } command || !command.IsValid())
            return new() { Code = "invalid_arguments", Message = "Specify one supported track discussion action." };

        if (getPage() is not { } page)
            return new() { Code = "page_unavailable", Message = "Open track details in Mizon first." };

        var result = await page.ExecuteDiscussionAsync(command, token);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(page, getPage()))
            return result with { MusicDiscussion = null };
        return result with { MusicDiscussion = result.MusicDiscussion ?? page.ReadDiscussion() };
    }

    internal McpResponse Execute(McpRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (getPage() is not { } page)
            return new() { Code = "page_unavailable", Message = "Open track details in Mizon first." };

        if (request.Form is not null || request.Page is not null || request.Language is not null || request.Navigation is not null ||
            request.Appearance is not null || request.MusicHome is not null || request.Vpn is not null || request.FullScreen is not null ||
            request.GamepadAction is not null || request.PersonProfile is not null)
            return Invalid();

        switch (request.Operation, request.MusicDetails)
        {
            case ("get_music_details", null):
                break;
            case ("set_music_details", { RepeatEnabled: bool repeat, CoverOpen: null, Audience: null, WaveformComment: null }):
                page.SetRepeatEnabled(repeat);
                break;
            case ("set_music_details", { RepeatEnabled: null, CoverOpen: bool open, Audience: null, WaveformComment: null }):
                if (!page.SetCoverOpen(open))
                    return new() { Code = "cover_unavailable", Message = "This track has no cover ready to preview." };
                break;
            case ("set_music_details", { RepeatEnabled: null, CoverOpen: null, Audience: "live" or "views" or "closed", WaveformComment: null } details):
                if (!page.SetAudience(details.Audience))
                    return new() { Code = "audience_unavailable", Message = "The current track’s audience panel is unavailable." };
                break;
            case ("set_music_details", { RepeatEnabled: null, CoverOpen: null, Audience: null, WaveformComment: { } marker }):
                Guid? comment = null;
                if (marker != "closed")
                {
                    if (!Guid.TryParseExact(marker, "D", out var id) || id == Guid.Empty) return Invalid();
                    comment = id;
                }
                if (!page.SetWaveformCommentPreview(comment))
                    return new() { Code = "waveform_comment_unavailable", Message = "This comment marker is unavailable on the active track." };
                break;
            default:
                return Invalid();
        }

        return new() { Success = true, Code = "ok", Message = "Active track details.", MusicDetails = page.ReadState() };
    }

    private static McpResponse Invalid() =>
        new() { Code = "invalid_arguments", Message = "Specify exactly one desired detail setting when changing track details." };
}
