using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Pages;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Threading.Tasks;

namespace Mizon.Windows.Services;

public static class FollowingActivityNavigation
{
    public static async Task OpenAsync(FriendMusicActivityItemViewModel activity, NavigationService navigation,
        Func<FollowingActivityStoryPresenter> storyPresenter, Image? coverSource = null)
    {
        if (!activity.CanOpen) return;
        try
        {
            switch (activity.TargetKind)
            {
                case FollowingActivityTargetKind.Story:
                    await storyPresenter().OpenAsync(activity.StoryPublicId!.Value, coverSource);
                    break;
                case FollowingActivityTargetKind.MusicComment:
                    var musicId = activity.MusicPublicId!.Value;
                    var commentId = activity.MusicCommentPublicId!.Value;
                    if (navigation.CurrentInternalPage is MusicsPage current && current.MusicPublicId == musicId)
                        await current.OpenCommentAsync(commentId);
                    else
                        navigation.InternalNavigate(typeof(MusicsPage), new()
                        {
                            [PageConstants.MUSIC_ID] = musicId,
                            [PageConstants.MUSIC_COMMENT_ID] = commentId
                        }, coverSource);
                    break;
                case FollowingActivityTargetKind.Music:
                    navigation.InternalNavigate(typeof(MusicsPage), new()
                    {
                        [PageConstants.MUSIC_ID] = activity.MusicPublicId!.Value
                    }, coverSource);
                    break;
                case FollowingActivityTargetKind.MusicPlaylist:
                    navigation.OpenMusicPlaylist(activity.MusicPlaylistPublicId!.Value);
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLog.Error("Following activity destination could not be opened.", error); }
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
