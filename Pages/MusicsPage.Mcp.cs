using Microsoft.Extensions.DependencyInjection;
using Mizon.API.Client.Requests.Musics;
using Mizon.Windows.Common;
using Mizon.Windows.Mcp.Contracts;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage : IMcpMusicDetailsPage
{
    internal bool IsMusicDetailsActive => _pageActive && MusicPublicId != Guid.Empty && ViewModel.CurrentMusicPublicId == MusicPublicId;

    McpMusicDetailsState IMcpMusicDetailsPage.ReadState() => new()
    {
        MusicPublicId = MusicPublicId,
        Title = ViewModel.Title,
        Duration = ViewModel.Duration,
        Language = Localization.LanguageTag,
        MetadataLoading = ViewModel.IsTrackMetadataLoading,
        MetadataLoadFailed = ViewModel.TrackMetadataLoadFailed,
        WaveformLoading = ViewModel.IsTrackWaveformLoading,
        WaveformCommentsLoading = ViewModel.IsTrackCommentsLoading,
        AudiencePopup = _openAudience,
        ViewsCount = ViewModel.ViewsCount,
        OnlineViewersCount = ViewModel.LiveViewsCount,
        LiveListenerCount = ViewModel.MusicListeners.Count,
        MusicVideoAvailable = ViewModel.HasMusicVideo,
        PlaybackDeviceVolume = ViewModel.SelectedAudioDevice?.OutputVolume?.Percentage,
        PlaybackDeviceSilent = ViewModel.SelectedAudioDevice?.OutputVolume?.IsSilent,
        PlaybackDeviceCount = ViewModel.AudioDevices.Count,
        PlaybackDeviceSelected = ViewModel.SelectedAudioDevice is not null,
        IsPlaying = ViewModel.IsPlaying,
        RepeatEnabled = ViewModel.IsRepeatEnabled,
        MixerAvailable = ViewModel.IsStemMixAvailable,
        MixerEnabled = ViewModel.IsStemMixerEnabled,
        MixerLoading = ViewModel.IsStemMixerLoading,
        MixerActive = ViewModel.IsStemMixerActive,
        MixerBalance = ViewModel.StemMixBalance,
        WaveformCommentCount = ViewModel.EqualizerComments.Count,
        WaveformCommentPublicIds = ViewModel.EqualizerComments.Take(64).Select(comment => comment.PublicId).ToArray(),
        WaveformCommentPreviewPublicId = _previewComment?.PublicId,
        CoverAvailable = !string.IsNullOrWhiteSpace(ViewModel.CoverImagePath),
        CoverOpen = _isCoverPreviewOpen,
        ContributorCount = ViewModel.Contributors.Count,
        ContributorsWithPortrait = ViewModel.Contributors.Count(credit => credit.Portrait is not null)
    };

    bool IMcpMusicDetailsPage.SetCoverOpen(bool open) => SetCoverPreview(open, restoreFocus: true);

    void IMcpMusicDetailsPage.SetRepeatEnabled(bool enabled) => ViewModel.IsRepeatEnabled = enabled;

    bool IMcpMusicDetailsPage.SetAudience(string view) => SetTrackAudience(view);

    bool IMcpMusicDetailsPage.SetWaveformCommentPreview(Guid? commentPublicId)
    {
        if (commentPublicId is Guid id) return ShowWaveformCommentPreview(id);
        CloseWaveformCommentPreview();
        return true;
    }

    McpMusicDiscussionState IMcpMusicDetailsPage.ReadDiscussion() => ReadDiscussion();

    private McpMusicDiscussionState ReadDiscussion(McpDiscussionTag[]? tags = null) => new(
        MusicPublicId, ViewModel.IsCommentsDetailsSelected, ViewModel.DiscussionSort,
        ViewModel.DiscussionParentId, ViewModel.DiscussionHasMore, ViewModel.DiscussionLoading,
        ViewModel.IsCommentsDetailsSelected ? ViewModel.MusicDetailsComments.TakeLast(25).Select(comment => new McpDiscussionComment(
            comment.PublicId, comment.Text.Length <= 2000 ? comment.Text : comment.Text[..2000], comment.FullName,
            comment.Platform, comment.Timestamp is TimeSpan timestamp ? (long)timestamp.TotalMilliseconds : null,
            comment.Likes, comment.Dislikes, comment.MyVote, comment.ReplyCount,
            new AppActivationRequest(MusicPublicId: MusicPublicId, MusicCommentPublicId: comment.PublicId).ToUri(),
            comment.Tags.Select(tag => new McpDiscussionTag(tag.Type, tag.PublicId, tag.Label)).ToArray())).ToArray() : [],
        tags);

    async Task<McpResponse> IMcpMusicDetailsPage.ExecuteDiscussionAsync(McpMusicDiscussionRequest request, CancellationToken token)
    {
        if (!IsMusicDetailsActive) return new() { Code = "page_unavailable", Message = "Open track details in Mizon first." };
        var track = MusicPublicId;
        var comment = ViewModel.IsCommentsDetailsSelected && request.CommentPublicId.HasValue
            ? ViewModel.MusicDetailsComments.FirstOrDefault(item => item.PublicId == request.CommentPublicId) : null;
        switch (request.Action)
        {
            case "tag_search":
                var matches = await SearchDiscussionTagsAsync(request.TagType!, request.Query!.Trim().TrimStart('@'), token);
                if (matches is not null && IsMusicDetailsActive && MusicPublicId == track)
                    return new()
                    {
                        Success = true, Code = "ok", Message = "Matching discussion tags.",
                        MusicDiscussion = ReadDiscussion(matches.Tags.Select(tag => new McpDiscussionTag(tag.Type, tag.PublicId, tag.Label)).ToArray())
                    };
                break;
            case "post" when request.CommentPublicId is null || comment is not null:
                var avatar = ((App)App.Current).Services.GetRequiredService<HomePageViewModel>().ProfileImage;
                var created = await ViewModel.PostMusicDiscussionCommentAsync(track, request.Text!,
                    request.TimestampMilliseconds is int milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : null,
                    avatar, request.CommentPublicId,
                    request.Tags?.Select(tag => new MusicCommentTagRequest(tag.Type, tag.PublicId)).ToList(), token);
                if (created is Guid id)
                {
                    if (IsMusicDetailsActive && MusicPublicId == track) await OpenCommentAsync(id, token);
                    return new() { Success = true, Code = "ok", Message = "Comment posted." };
                }
                break;
            default:
                var applied = request.Action switch
                {
                    "load" => await ViewModel.LoadMusicDiscussionAsync(null, request.Sort ?? ViewModel.DiscussionSort, false, cancellationToken: token),
                    "next" when ViewModel.IsCommentsDetailsSelected => await ViewModel.LoadMusicDiscussionAsync(
                        ViewModel.DiscussionParentId, ViewModel.DiscussionSort, true, cancellationToken: token),
                    "replies" when comment is not null => await ViewModel.LoadMusicDiscussionAsync(
                        comment.PublicId, ViewModel.DiscussionSort, false, cancellationToken: token),
                    "focus" when request.CommentPublicId is Guid target => await OpenCommentAsync(target, token),
                    "vote" when comment is not null => await ViewModel.VoteOnMusicCommentAsync(comment, request.Vote!.Value, token),
                    "play_timestamp" when comment?.Timestamp is TimeSpan timestamp =>
                        await ViewModel.ActivateMusicAsync(track, MusicActivationAction.Play, token, timestamp),
                    _ => false
                };
                if (applied) return new() { Success = true, Code = "ok", Message = "Track discussion updated." };
                break;
        }
        return new() { Code = "discussion_action_unavailable", Message = "The action could not be completed on the active track." };
    }
}
