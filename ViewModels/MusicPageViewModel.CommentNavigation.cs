using Mizon.API.Client.Responses.Musics;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

public partial class MusicPageViewModel
{
    private int _commentDetailsRequestVersion;
    public bool HasFocusedMusicComment
    {
        get => field;
        private set => SetField(ref field, value);
    }

    internal async Task<bool> FocusMusicCommentAsync(Guid musicPublicId, Guid commentPublicId, CancellationToken token)
    {
        if (commentPublicId == Guid.Empty || !IsCurrentMusicLoad(musicPublicId, token)) return false;
        ClearFocusedMusicComment();
        IsAlbumDetailsSelected = false;
        IsContributorDetailsSelected = false;
        IsCommentsDetailsSelected = true;
        return await GetMusicDetailsCommentsAsync(musicPublicId, token, commentPublicId);
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
        ProfileMediaPublicId = comment.ProfileMediaPublicId
    };
}
