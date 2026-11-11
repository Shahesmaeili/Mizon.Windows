using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Pages;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

public static class FollowingActivityNavigation
{
    public static async Task OpenAsync(FriendMusicActivityItemViewModel activity, NavigationService navigation,
        Func<FollowingActivityStoryPresenter> storyPresenter, Image? coverSource = null)
    {
        try
        {
            await TryOpenAsync(activity, navigation, storyPresenter, coverSource);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLog.Error("Following activity destination could not be opened.", error); }
    }

    internal static async Task<bool> TryOpenAsync(FriendMusicActivityItemViewModel activity, NavigationService navigation,
        Func<FollowingActivityStoryPresenter> storyPresenter, Image? coverSource = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!activity.CanOpen) return false;
        switch (activity.TargetKind)
        {
            case FollowingActivityTargetKind.Story:
                if (activity.StoryPublicId is not { } storyId || storyId == Guid.Empty) return false;
                var presenter = storyPresenter();
                await presenter.OpenAsync(storyId, coverSource, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return presenter.ActiveViewer is not null && presenter.CurrentStoryPublicId == storyId;
            case FollowingActivityTargetKind.MusicComment:
                if (activity.MusicPublicId is not { } commentMusicId || commentMusicId == Guid.Empty
                    || activity.MusicCommentPublicId is not { } commentId || commentId == Guid.Empty) return false;
                if (navigation.CurrentInternalPage is MusicsPage current && current.MusicPublicId == commentMusicId)
                {
                    await current.OpenCommentAsync(commentId, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return ReferenceEquals(navigation.CurrentInternalPage, current);
                }
                cancellationToken.ThrowIfCancellationRequested();
                navigation.InternalNavigate(typeof(MusicsPage), new()
                {
                    [PageConstants.MUSIC_ID] = commentMusicId,
                    [PageConstants.MUSIC_COMMENT_ID] = commentId
                }, coverSource);
                // Navigation intentionally unloads the source page and cancels its
                // lifetime. Validate the destination without treating that as failure.
                return navigation.CurrentInternalPage is MusicsPage commentPage && commentPage.MusicPublicId == commentMusicId;
            case FollowingActivityTargetKind.Music:
                if (activity.MusicPublicId is not { } musicId || musicId == Guid.Empty) return false;
                cancellationToken.ThrowIfCancellationRequested();
                navigation.InternalNavigate(typeof(MusicsPage), new()
                {
                    [PageConstants.MUSIC_ID] = musicId
                }, coverSource);
                return navigation.CurrentInternalPage is MusicsPage musicPage && musicPage.MusicPublicId == musicId;
            case FollowingActivityTargetKind.MusicPlaylist:
                if (activity.MusicPlaylistPublicId is not { } playlistId || playlistId == Guid.Empty) return false;
                cancellationToken.ThrowIfCancellationRequested();
                navigation.OpenMusicPlaylist(playlistId);
                return navigation.CurrentInternalPage is MusicPlaylistPage playlistPage && playlistPage.PlaylistPublicId == playlistId;
            default:
                return false;
        }
    }

    internal static Image? FindCover(DependencyObject? root)
    {
        if (root is Image { Name: "ActivityCover" or "CardArtwork" } image) return image;
        if (root is null) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindCover(VisualTreeHelper.GetChild(root, index)) is { } cover) return cover;
        return null;
    }
}

public sealed class FollowingActivityRequestedEventArgs(FriendMusicActivityItemViewModel activity, Image? coverSource) : EventArgs
{
    public FriendMusicActivityItemViewModel Activity { get; } = activity;
    public Image? CoverSource { get; } = coverSource;
}
