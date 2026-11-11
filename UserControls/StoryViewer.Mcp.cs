using Microsoft.UI.Xaml;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Pages;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.UserControls;

public sealed partial class StoryViewer
{
    internal bool McpDialogOpen => _clientDialog is not null;
    internal McpMusicHomeStoryState GetMcpState() => new()
    {
        Id = _index < _stories.Count ? _stories[_index].PublicId.ToString("D") : null,
        Index = _index, Count = _stories.Count,
        HasError = ErrorPanel.Visibility == Visibility.Visible,
        ErrorCode = ErrorPanel.Visibility == Visibility.Visible ? "load_failed" : null,
        IsLoading = !_ready && ErrorPanel.Visibility != Visibility.Visible && !_closed && !_closing,
        Actions = _closed || _closing ? [] : ErrorPanel.Visibility == Visibility.Visible ? ["retry", "dismiss"]
            : _ready ? ["pause", "resume", "previous", "next", "select", "like", "profile", "open_widget", "dismiss"] : ["dismiss"],
        IsPlaying = _ready && !_paused && !_closed && !_closing, IsLiked = _detail?.LikedByMe == true,
        CanLike = LikeButton.IsEnabled && _detail is not null && !_closing && !_closed,
        CanPrevious = _index > 0, CanNext = _index + 1 < _stories.Count,
        PositionSeconds = _playback?.Position.TotalSeconds ?? _imageClock.Elapsed.TotalSeconds,
        DurationSeconds = _playback?.Duration.TotalSeconds ?? 6,
        Widgets = _detail?.Widgets.Take(McpMusicHomeContract.MaxItemsPerSection).Select(widget => new McpMusicHomeItemState
        {
            Id = widget.PublicId.ToString("D"), Kind = widget.Music is not null ? "music" : "client",
            Title = widget.Music?.Title ?? $"{widget.Client?.FirstName} {widget.Client?.LastName}".Trim(),
            TargetId = (widget.Music?.MusicPublicId ?? widget.Client?.ClientPublicId)?.ToString("D"),
            TargetKind = widget.Music is not null ? "music" : "profile",
            IsAvailable = true, CanOpen = true, Actions = ["open_widget"]
        }).ToArray() ?? []
    };

    internal async Task<McpResponse> ExecuteMcpAsync(McpMusicHomeRequest input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_closed || _closing) return MusicPage.Unavailable("The story viewer has closed.");
        // Cancellation of a consented request closes this viewer and drains its own media work.
        using var cancellation = token.Register(() => DispatcherQueue.TryEnqueue(Close));
        switch (input.Action)
        {
            case "pause":
            case "resume":
                if (!_ready) return MusicPage.Unavailable("The story is still loading.");
                var paused = input.Action == "pause";
                if (_paused != paused) Pause_Click(this, new RoutedEventArgs());
                break;
            case "previous":
                if (_index <= 0) return MusicPage.Unavailable("This is the first story.");
                await OpenIndexAsync(_index - 1); break;
            case "next": await MoveNextAsync(); break;
            case "select":
                if (input.Index is not { } index || index >= _stories.Count) return MusicPage.Missing();
                await OpenIndexAsync(index); break;
            case "retry": await OpenIndexAsync(_index); break;
            case "like":
                if (!await SetLikedAsync(input.Enabled!.Value, token))
                    return new() { Code = "operation_failed", Message = "The story like could not be saved or is already being updated." };
                break;
            case "profile":
                if (_detail is not { } detail || _viewModel is not { } model) return MusicPage.Unavailable("The story details are not ready.");
                var navigation = model.NavigationService;
                var clientId = detail.CreatorClientPublicId;
                await CloseAsync();
                token.ThrowIfCancellationRequested();
                navigation.InternalNavigate(typeof(ClientProfilePage), new() { [PageConstants.CLIENT_ID] = clientId });
                return MusicPage.Done("opened");
            case "open_widget":
                var widget = _widgets.FirstOrDefault(pair => pair.Widget.PublicId.ToString("D") == input.Id);
                if (widget.Element is null) return MusicPage.Missing();
                if (!await OpenWidgetAsync(widget.Widget, token)) return MusicPage.Unavailable("The story tag could not be opened.");
                return MusicPage.Done("opened");
            default: return MusicPage.Unavailable("This story action is not available.");
        }
        token.ThrowIfCancellationRequested();
        return MusicPage.Done("updated");
    }

    // Both UI and MCP use the same idempotent desired-state operation.
    private async Task<bool> SetLikedAsync(bool liked, CancellationToken token = default)
    {
        if (_closing || _closed || !LikeButton.IsEnabled || _detail is not { } detail
            || _viewModel is not { } model || _request is not { } request) return false;
        if (detail.LikedByMe == liked) return true;
        LikeButton.IsEnabled = false;
        try
        {
            using var lease = request.Retain();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, token);
            var response = await model.SetStoryLikedAsync(detail.PublicId, liked, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(request) || response is null) return false;
            detail.LikedByMe = response.LikedByMe;
            detail.LikesCount = response.LikesCount;
            UpdateLike();
            Common.AccessibilityAnnouncements.Announce(this,
                Text(detail.LikedByMe ? "StoryLiked" : "LikeRemoved"), "story-like");
            return true;
        }
        finally { if (IsCurrent(request)) LikeButton.IsEnabled = true; }
    }
}
