using Mizon.Windows.Mcp.Contracts;
using System;
using System.Threading;

namespace Mizon.Windows.Services;

internal interface IMcpMusicDetailsPage
{
    McpMusicDetailsState ReadState();
    bool SetCoverOpen(bool open);
    void SetRepeatEnabled(bool enabled);
}

// Called only by the authorized UI dispatcher for the currently displayed track.
internal sealed class McpMusicDetailsService(Func<IMcpMusicDetailsPage?> getPage)
{
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
            case ("set_music_details", { RepeatEnabled: bool repeat, CoverOpen: null }):
                page.SetRepeatEnabled(repeat);
                break;
            case ("set_music_details", { RepeatEnabled: null, CoverOpen: bool open }):
                if (!page.SetCoverOpen(open))
                    return new() { Code = "cover_unavailable", Message = "This track has no cover ready to preview." };
                break;
            default:
                return Invalid();
        }

        return new() { Success = true, Code = "ok", Message = "Active track details.", MusicDetails = page.ReadState() };
    }

    private static McpResponse Invalid() =>
        new() { Code = "invalid_arguments", Message = "Specify exactly one desired detail setting when changing track details." };
}
