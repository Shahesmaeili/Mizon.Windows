using Mizon.API;
using Mizon.API.Client.Operations.Musics;
using Mizon.API.Client.Responses.Musics;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class MusicPageViewModel
{
    private int _commentDetailsRequestVersion;
    private Guid? _nextDiscussionComment;
    private CancellationTokenSource? _discussionLoad;
    private CancellationToken TrackCommentToken => _musicLoadCancellationTokenSource?.Token ?? CancellationToken.None;
    internal Guid? DiscussionParentId { get; private set; }
    internal string DiscussionSort { get; private set; } = "newest";
    internal TimeSpan? DiscussionPlaybackPosition => HasCurrentTrackSource && _musicDuration > TimeSpan.Zero
        ? _audioPlayerService.Position : null;
    public bool DiscussionIsThread => DiscussionParentId.HasValue;
    public bool DiscussionHasSelection => DiscussionIsThread || HasFocusedMusicComment;
    public bool DiscussionHasMore { get => field; private set => SetField(ref field, value); }
    public bool DiscussionLoading { get => field; private set => SetField(ref field, value); }
    public bool DiscussionLoadFailed { get => field; private set => SetField(ref field, value); }
    public bool DiscussionEmpty { get => field; private set => SetField(ref field, value); }
    public bool HasFocusedMusicComment
    {
        get => field;
        private set { if (SetField(ref field, value)) OnPropertyChanged(nameof(DiscussionHasSelection)); }
    }

    internal async Task<bool> FocusMusicCommentAsync(Guid musicPublicId, Guid commentPublicId, CancellationToken token)
    {
        if (commentPublicId == Guid.Empty || !IsCurrentMusicLoad(musicPublicId, token)) return false;
        ClearFocusedMusicComment();
        IsAlbumDetailsSelected = false;
        IsContributorDetailsSelected = false;
        IsCommentsDetailsSelected = true;
        return await LoadMusicDiscussionAsync(null, DiscussionSort, append: false, commentPublicId, token);
    }

    internal async Task<bool> LoadMusicDiscussionAsync(Guid? parent, string sort, bool append,
        Guid? focus = null, CancellationToken cancellationToken = default)
    {
        var musicId = _musicPublicId;
        if (musicId == Guid.Empty || (append && (_nextDiscussionComment is null || DiscussionLoading))) return false;
        cancellationToken.ThrowIfCancellationRequested();
        _discussionLoad?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, TrackCommentToken);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        _discussionLoad = linked;
        IsAlbumDetailsSelected = false;
        IsContributorDetailsSelected = false;
        IsCommentsDetailsSelected = true;
        var version = ++_commentDetailsRequestVersion;
        DiscussionLoading = true;
        DiscussionLoadFailed = DiscussionEmpty = false;

        bool IsCurrent() => IsCurrentMusicLoad(musicId, token) && version == _commentDetailsRequestVersion && IsCommentsDetailsSelected;
        try
        {
            MusicCommentItemResponse? selected = null;
            if (focus.HasValue)
            {
                var targeted = await MizonApi.SendRequestAsync(new MusicCommentsOperation(new()
                {
                    MusicPublicId = musicId, MusicCommentPublicId = focus
                }), token);
                if (!IsCurrent() || !targeted.IsSuccess || targeted.Content?.MusicComments.FirstOrDefault() is not { } match)
                    return false;
                selected = match;
                parent = match.ParentMusicCommentPublicId;
            }
            if (!append)
            {
                if (DiscussionParentId != parent || DiscussionSort != sort)
                {
                    _nextDiscussionComment = null;
                    DiscussionHasMore = false;
                    ClearFocusedMusicComment();
                    MusicDetailsComments.Clear();
                }
                DiscussionParentId = parent;
                if (DiscussionSort != sort)
                {
                    DiscussionSort = sort;
                    OnPropertyChanged(nameof(DiscussionSort));
                }
                OnPropertyChanged(nameof(DiscussionIsThread));
                OnPropertyChanged(nameof(DiscussionHasSelection));
            }

            var response = await MizonApi.SendRequestAsync(new MusicCommentsOperation(new()
            {
                MusicPublicId = musicId, ParentMusicCommentPublicId = parent,
                PageSize = 24, After = append ? _nextDiscussionComment : null, Sort = sort
            }), token);
            if (!IsCurrent()) return false;
            if (!response.IsSuccess || response.Content is not { } page)
            {
                DiscussionLoadFailed = true;
                if (selected is null) return false;
                page = new();
            }

            if (!append)
            {
                ClearFocusedMusicComment();
                MusicDetailsComments.Clear();
                if (selected is not null)
                {
                    Add(selected, focused: true);
                    HasFocusedMusicComment = true;
                }
            }
            var loaded = MusicDetailsComments.Select(row => row.PublicId).ToHashSet();
            foreach (var comment in page.MusicComments)
                if (loaded.Add(comment.PublicId)) Add(comment, focused: false);
            _nextDiscussionComment = page.Next;
            DiscussionHasMore = page.Next.HasValue;
            DiscussionEmpty = !DiscussionLoadFailed && MusicDetailsComments.Count == 0;
            return true;

            void Add(MusicCommentItemResponse comment, bool focused)
            {
                var item = CreateCommentItem(comment);
                item.IsActivityTarget = focused;
                MusicDetailsComments.Add(item);
                if (item.ProfileMediaPublicId is Guid mediaId && mediaId != Guid.Empty)
                    _ = LoadMusicDetailsCommentProfileAsync(item, mediaId, TrackCommentToken);
            }
        }
        finally
        {
            if (ReferenceEquals(_discussionLoad, linked)) _discussionLoad = null;
            if (version == _commentDetailsRequestVersion) DiscussionLoading = false;
        }
    }

    internal void CancelDiscussionLoad()
    {
        _commentDetailsRequestVersion++;
        _discussionLoad?.Cancel();
        _discussionLoad = null;
        DiscussionLoading = false;
    }

    internal async Task<bool> VoteOnMusicCommentAsync(MusicDetailsCommentItemViewModel comment, int vote, CancellationToken cancellationToken)
    {
        if (comment.IsVoting || !MusicDetailsComments.Contains(comment) || vote is < -1 or > 1) return false;
        var musicId = _musicPublicId;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, TrackCommentToken);
        comment.IsVoting = true;
        try
        {
            var result = await MizonApi.SendRequestAsync(new MusicCommentVoteOperation(new()
            {
                MusicPublicId = musicId, MusicCommentPublicId = comment.PublicId, Vote = vote
            }), linked.Token);
            if (!result.IsSuccess || result.Content is not { } counts || !IsCurrentMusicLoad(musicId, linked.Token)) return false;
            comment.ApplyVotes(counts.Likes, counts.Dislikes, counts.MyVote);
            return true;
        }
        finally { comment.IsVoting = false; }
    }

    private void ResetMusicDiscussion()
    {
        CancelDiscussionLoad();
        _nextDiscussionComment = null;
        DiscussionParentId = null;
        DiscussionHasMore = DiscussionLoading = DiscussionLoadFailed = DiscussionEmpty = false;
        OnPropertyChanged(nameof(DiscussionIsThread));
        OnPropertyChanged(nameof(DiscussionHasSelection));
    }

    private void ClearFocusedMusicComment()
    {
        HasFocusedMusicComment = false;
        foreach (var item in MusicDetailsComments) item.IsActivityTarget = false;
    }

    private static MusicDetailsCommentItemViewModel CreateCommentItem(MusicCommentItemResponse comment) => new()
    {
        PublicId = comment.PublicId,
        ClientPublicId = comment.ClientPublicId,
        FirstName = comment.FirstName,
        LastName = comment.LastName,
        Username = comment.Username,
        Text = comment.Text,
        Timestamp = comment.Timestamp,
        ProfileMediaPublicId = comment.ProfileMediaPublicId,
        Platform = comment.Platform,
        CreatedAt = comment.CreateDateTime,
        ParentPublicId = comment.ParentMusicCommentPublicId,
        ReplyCount = comment.Replies,
        Tags = comment.Tags,
        Likes = comment.Likes,
        Dislikes = comment.Dislikes,
        MyVote = comment.MyVote
    };
}
