using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Pages;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.UserControls;

public sealed partial class DiscoveryFeatureHero
{
    internal MusicFeatureItemViewModel? McpSelectedItem => _selected;
    internal McpMusicHomeHeroState GetMcpState() => new()
    {
        Id = _selected?.PublicId.ToString("D"), Kind = _selected?.ContentTypeCode ?? string.Empty,
        Index = _index, Count = ViewModel?.Items.Count ?? 0,
        IsPlaying = _selected?.IsVideo == true ? _media?.IsPlaying == true : CanAdvance,
        IsMuted = _media?.IsMuted != false, Volume = _media?.Volume ?? .7,
        IsLoading = _sourceLoading || _media?.IsLoading == true,
        PositionSeconds = _selected?.IsVideo == true ? _media?.Position.TotalSeconds ?? 0 : _imagePosition,
        DurationSeconds = _selected?.IsVideo == true ? _media?.Duration.TotalSeconds ?? 0 : _selected?.DisplaySeconds ?? 0,
        IsFullscreen = HeroVideo.IsFullWindow, CanSeek = PositionSlider.IsEnabled,
        IsLiked = _selected?.LikedByMe == true, CanLike = _selected is { IsLikeBusy: false },
        Actions = _selected is null ? [] : ["open", "play", "pause", "resume", "previous", "next", "seek", "like", "copy_link", "request_download"]
    };

    internal bool McpSelect(int index)
    {
        if (!_loaded || ViewModel?.Items is not { } items || index < 0 || index >= items.Count) return false;
        Select(index, true);
        return true;
    }
    internal void McpPause()
    {
        _playRequested = false;
        _media?.Pause();
        UpdateActive();
        UpdateTransport();
    }

    internal async Task<McpResponse> ExecuteMcpAsync(McpMusicHomeRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_loaded || _selected is not { } item) return MusicPage.Missing();
        switch (request.Action)
        {
            case "previous": SelectRelative(-1, true); break;
            case "next": SelectRelative(1, true); break;
            case "select": if (!McpSelect(request.Index!.Value)) return MusicPage.Missing(); break;
            case "pause": McpPause(); break;
            case "resume":
                if (item.IsVideo && _media?.IsReady != true) return MusicPage.Unavailable("The featured video is not ready.");
                if (!(item.IsVideo ? _media?.WantsToPlay == true : _playRequested)) PlayPause_Click(this, new RoutedEventArgs());
                break;
            case "mute":
                if (!item.IsVideo || _media is not { IsReady: true } media) return MusicPage.Unavailable("Select a ready video before changing its sound.");
                var version = _selectionVersion;
                if (!request.Enabled!.Value)
                {
                    var audio = ((App)Application.Current).Services.GetRequiredService<IAudioPlayerService>();
                    if (audio.AudioPlayerState == AudioPlayerState.Playing) await audio.PauseAsync();
                }
                token.ThrowIfCancellationRequested();
                if (!_loaded || version != _selectionVersion) return MusicPage.Missing();
                media.IsMuted = request.Enabled!.Value;
                UpdateTransport();
                break;
            case "volume":
                if (!item.IsVideo || _media?.IsReady != true) return MusicPage.Unavailable("Select a ready video before changing its volume.");
                VolumeSlider.Value = request.Value!.Value * 100;
                break;
            case "seek":
                if (!PositionSlider.IsEnabled || request.Value > PositionSlider.Maximum) return MusicPage.Unavailable("The requested position is outside this media or seeking is not ready.");
                PositionSlider.Value = request.Value!.Value;
                CommitSeek(); UpdateTransport();
                break;
            case "fullscreen":
                if (request.Enabled == true && (!item.IsVideo || _media?.IsReady != true)) return MusicPage.Unavailable("Only a ready video supports full window playback.");
                if (request.Enabled == true) Fullscreen_Click(this, new RoutedEventArgs());
                else HeroVideo.IsFullWindow = false;
                break;
            case "like":
                if (item.IsLikeBusy) return new() { Code = "busy", Message = "A like request is already in progress." };
                if (item.LikedByMe != request.Enabled && (ViewModel is null || !await ViewModel.ToggleLikeAsync(item, token)))
                    return new() { Code = "operation_failed", Message = "The like could not be saved." };
                break;
            case "copy_link":
                return TryCopyCurrentLink() ? MusicPage.Done("copied") : MusicPage.Unavailable("The clipboard is not available.");
            case "request_download":
                if (_saving) return new() { Code = "busy", Message = "A save request is already in progress." };
                var outcome = await SaveCurrentAsync(token);
                token.ThrowIfCancellationRequested();
                return new() { Success = outcome == "saved", Code = outcome,
                    Message = outcome == "saved" ? "The selected media was saved." : "The save flow did not write a file." };
            case "retry": Retry_Click(this, new RoutedEventArgs()); break;
            default: return MusicPage.Unavailable("This action is not supported by the featured player.");
        }
        token.ThrowIfCancellationRequested();
        return MusicPage.Done("updated");
    }
}
