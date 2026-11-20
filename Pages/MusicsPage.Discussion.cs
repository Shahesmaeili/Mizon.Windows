using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mizon.API;
using Mizon.API.Client.Operations.Musics;
using Mizon.API.Client.Requests.Musics;
using Mizon.API.Client.Responses.Musics;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage
{
    private ObservableCollection<MusicCommentTagItem> DiscussionTagResults { get; } = [];
    private ObservableCollection<MusicCommentTagItem> DiscussionSelectedTags { get; } = [];
    private CancellationTokenSource? _discussionTagSearch;
    private MusicDetailsCommentItemViewModel? _discussionReply;
    private TimeSpan? _discussionPosition;
    private string? _discussionStatus;
    private bool _postingDiscussion;

    private async void NewDiscussionComment_Click(object sender, RoutedEventArgs args) =>
        await StartDiscussionComposerAsync(null, null);

    private async void ReplyToDiscussionComment_Click(object sender, RoutedEventArgs args)
    {
        if (CommentFrom(sender) is { } comment) await StartDiscussionComposerAsync(comment, null);
    }

    private async Task StartDiscussionComposerAsync(MusicDetailsCommentItemViewModel? reply, TimeSpan? selectedPosition)
    {
        if (!IsMusicDetailsActive || _postingDiscussion) return;
        if (!ViewModel.IsCommentsDetailsSelected)
        {
            var track = MusicPublicId;
            await RunDiscussionActionAsync(async () => { await ViewModel.SelectCommentsDetailsAsync(); return true; });
            if (!IsMusicDetailsActive || MusicPublicId != track) return;
        }
        DiscussionKeyboard.Close(false);
        _discussionReply = reply;
        _discussionPosition = selectedPosition ?? ViewModel.DiscussionPlaybackPosition;
        DiscussionTimestamp.IsEnabled = _discussionPosition.HasValue;
        DiscussionTimestamp.IsChecked = selectedPosition.HasValue;
        DiscussionComposer.Visibility = Visibility.Visible;
        SetDiscussionComposerStatus(null);
        ApplyDiscussionComposerLanguage();
        DiscussionInput.Focus(FocusState.Programmatic);
        DiscussionComposer.StartBringIntoView();
    }

    private void ApplyDiscussionComposerLanguage()
    {
        if (DiscussionComposer.Visibility != Visibility.Visible) return;
        DiscussionComposerTitle.Text = _discussionReply is { } reply
            ? Localization.Format("MusicDetails/CommentReplyTo", reply.FullName) : Text("CommentNew");
        DiscussionTimestampText.Text = _discussionPosition?.ToString(@"h\:mm\:ss") ?? string.Empty;
        DiscussionComposerStatus.Text = _discussionStatus is null ? string.Empty : Text(_discussionStatus);
    }

    private void DiscussionInput_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is not TextBox input || args.OriginalKey != VirtualKey.GamepadA) return;
        args.Handled = true;
        if (!args.KeyStatus.WasKeyDown) DiscussionKeyboard.Open(input);
    }

    private void DiscussionInput_Changed(object sender, TextChangedEventArgs args) => UpdateDiscussionSubmit();

    private void UpdateDiscussionSubmit()
    {
        if (DiscussionSubmit is not null)
            DiscussionSubmit.IsEnabled = !_postingDiscussion && !string.IsNullOrWhiteSpace(DiscussionInput.Text);
    }

    private async void SubmitDiscussionComment_Click(object sender, RoutedEventArgs args)
    {
        if (!IsMusicDetailsActive || _postingDiscussion || string.IsNullOrWhiteSpace(DiscussionInput.Text)) return;
        var track = MusicPublicId;
        _postingDiscussion = true;
        DiscussionSubmit.IsEnabled = false;
        DiscussionKeyboard.Close(false);
        SetDiscussionComposerStatus("CommentPosting");
        try
        {
            var avatar = ((App)App.Current).Services.GetRequiredService<HomePageViewModel>().ProfileImage;
            var tags = DiscussionSelectedTags.Select(tag => new MusicCommentTagRequest(tag.Type, tag.PublicId)).ToList();
            var created = await ViewModel.PostMusicDiscussionCommentAsync(track, DiscussionInput.Text,
                DiscussionTimestamp.IsChecked == true ? _discussionPosition : null,
                avatar, _discussionReply?.PublicId, tags, CancellationToken.None);
            if (!IsMusicDetailsActive || MusicPublicId != track) return;
            if (created is not Guid id) { SetDiscussionComposerStatus("CommentPostFailed"); return; }
            ResetDiscussionComposer();
            await OpenCommentAsync(id);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            AppLog.Error("Track discussion comment could not be posted.", error);
            if (IsMusicDetailsActive && MusicPublicId == track) SetDiscussionComposerStatus("CommentPostFailed");
        }
        finally
        {
            _postingDiscussion = false;
            if (IsMusicDetailsActive && MusicPublicId == track) UpdateDiscussionSubmit();
        }
    }

    private void CancelDiscussionComment_Click(object sender, RoutedEventArgs args) => CloseDiscussionComposer();

    private bool CloseDiscussionComposer()
    {
        if (DiscussionKeyboard.IsOpen) { DiscussionKeyboard.Close(); return true; }
        if (DiscussionComposer.Visibility != Visibility.Visible) return false;
        if (!_postingDiscussion)
        {
            ResetDiscussionComposer();
            DiscussionSortPicker.Focus(FocusState.Programmatic);
        }
        return true;
    }

    private void ResetDiscussionComposer()
    {
        _discussionTagSearch?.Cancel();
        _discussionTagSearch = null;
        DiscussionTagSearchButton.IsEnabled = true;
        DiscussionKeyboard.Close(false);
        DiscussionComposer.Visibility = Visibility.Collapsed;
        _discussionReply = null;
        _discussionPosition = null;
        DiscussionInput.Text = DiscussionTagQuery.Text = string.Empty;
        DiscussionTagResults.Clear();
        DiscussionSelectedTags.Clear();
        SetDiscussionComposerStatus(null);
    }

    private async void SearchDiscussionTags_Click(object sender, RoutedEventArgs args)
    {
        if (!IsMusicDetailsActive || DiscussionTagType.SelectedItem is not ComboBoxItem { Tag: string type }) return;
        var query = DiscussionTagQuery.Text.Trim().TrimStart('@');
        if (query.Length < 2) { SetDiscussionComposerStatus("CommentTagShort"); return; }
        _discussionTagSearch?.Cancel();
        using var request = new CancellationTokenSource();
        _discussionTagSearch = request;
        DiscussionTagSearchButton.IsEnabled = false;
        DiscussionTagResults.Clear();
        SetDiscussionComposerStatus(null);
        try
        {
            var response = await SearchDiscussionTagsAsync(type, query, request.Token);
            if (request.IsCancellationRequested || !IsMusicDetailsActive || !ReferenceEquals(_discussionTagSearch, request)) return;
            if (response is not { } matches) { SetDiscussionComposerStatus("CommentsRetry"); return; }
            foreach (var tag in matches.Tags) DiscussionTagResults.Add(tag);
            if (matches.Tags.Count == 0) SetDiscussionComposerStatus("CommentTagNone");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            AppLog.Error("Track discussion tag search failed.", error);
            if (!request.IsCancellationRequested) SetDiscussionComposerStatus("CommentsRetry");
        }
        finally
        {
            if (ReferenceEquals(_discussionTagSearch, request))
            {
                _discussionTagSearch = null;
                DiscussionTagSearchButton.IsEnabled = true;
            }
        }
    }

    private static async Task<MusicCommentTagSearchResponse?> SearchDiscussionTagsAsync(string type, string query, CancellationToken token)
    {
        var api = ((App)App.Current).Services.GetRequiredService<MizonApi>();
        var response = await api.SendRequestAsync(new MusicCommentTagSearchOperation(new() { Type = type, Query = query }), token);
        return response.IsSuccess ? response.Content : null;
    }

    private void AddDiscussionTag_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: MusicCommentTagItem tag } ||
            DiscussionSelectedTags.Any(existing => existing.Type == tag.Type && existing.PublicId == tag.PublicId))
            return;
        if (DiscussionSelectedTags.Count == 8) { SetDiscussionComposerStatus("CommentTagLimit"); return; }
        DiscussionSelectedTags.Add(tag);
    }

    private void RemoveDiscussionTag_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: MusicCommentTagItem tag }) DiscussionSelectedTags.Remove(tag);
    }

    private async void DiscussionSort_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!IsMusicDetailsActive || !ViewModel.IsCommentsDetailsSelected ||
            DiscussionSortPicker.SelectedItem is not ComboBoxItem { Tag: string sort } || sort == ViewModel.DiscussionSort) return;
        CancelCommentNavigation();
        await RunDiscussionActionAsync(() => ViewModel.LoadMusicDiscussionAsync(ViewModel.DiscussionParentId, sort, append: false));
    }

    private async void OpenDiscussionReplies_Click(object sender, RoutedEventArgs args)
    {
        if (CommentFrom(sender) is not { } parent) return;
        CancelCommentNavigation();
        await RunDiscussionActionAsync(() => ViewModel.LoadMusicDiscussionAsync(parent.PublicId, ViewModel.DiscussionSort, append: false));
        if (IsMusicDetailsActive) DiscussionSortPicker.Focus(FocusState.Programmatic);
    }

    private void SyncDiscussionSort()
    {
        var selection = DiscussionSortPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, ViewModel.DiscussionSort));
        if (selection is not null) DiscussionSortPicker.SelectedItem = selection;
    }

    private async void LoadMoreDiscussion_Click(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.DiscussionLoading)
            await RunDiscussionActionAsync(() => ViewModel.LoadMusicDiscussionAsync(ViewModel.DiscussionParentId, ViewModel.DiscussionSort, append: true));
    }

    private async void RetryDiscussion_Click(object sender, RoutedEventArgs args) =>
        await RunDiscussionActionAsync(() => ViewModel.LoadMusicDiscussionAsync(ViewModel.DiscussionParentId, ViewModel.DiscussionSort, append: false));

    private async void DiscussionVote_Click(object sender, RoutedEventArgs args)
    {
        if (CommentFrom(sender) is not { } comment || sender is not FrameworkElement button) return;
        var vote = button.Tag is "like" ? 1 : -1;
        if (vote == comment.MyVote) vote = 0;
        await RunDiscussionActionAsync(() => ViewModel.VoteOnMusicCommentAsync(comment, vote, CancellationToken.None));
        comment.ApplyVotes(comment.Likes, comment.Dislikes, comment.MyVote);
    }

    private async void PlayDiscussionTimestamp_Click(object sender, RoutedEventArgs args)
    {
        if (CommentFrom(sender)?.Timestamp is not TimeSpan timestamp) return;
        await RunDiscussionActionAsync(() => ViewModel.ActivateMusicAsync(MusicPublicId, MusicActivationAction.Play,
            CancellationToken.None, timestamp));
    }

    private void DiscussionTranslation_Click(object sender, RoutedEventArgs args) => ShowDiscussionStatus("CommentTranslationSoon");

    private void CopyDiscussionLink_Click(object sender, RoutedEventArgs args)
    {
        if (CommentFrom(sender) is not { } comment) return;
        var uri = new AppActivationRequest(MusicPublicId: MusicPublicId, MusicCommentPublicId: comment.PublicId).ToUri();
        if (uri is null) return;
        var package = new DataPackage();
        package.SetText(uri);
        Clipboard.SetContent(package);
        ShowDiscussionStatus("CommentLinkCopied");
    }

    private async void OpenDiscussionTag_Click(object sender, RoutedEventArgs args)
    {
        if (!IsMusicDetailsActive || sender is not FrameworkElement { Tag: MusicCommentTagItem tag } ||
            ViewModel.NavigationService.CurrentMainPage is not HomePage home) return;
        AppActivationRequest? destination = tag.Type switch
        {
            "person" => new(PersonPublicId: tag.PublicId),
            "client" => new(ClientPublicId: tag.PublicId),
            "music" => new(MusicPublicId: tag.PublicId),
            "music-video" when tag.NavigationPublicId.HasValue => new(MusicPublicId: tag.NavigationPublicId, MusicAction: MusicActivationAction.Video),
            _ => null
        };
        if (destination is not null) await RunDiscussionActionAsync(() => home.OpenDestinationAsync(destination));
    }

    private async Task RunDiscussionActionAsync(Func<Task<bool>> operation)
    {
        if (!IsMusicDetailsActive) return;
        var track = MusicPublicId;
        try
        {
            if (!await operation() && IsMusicDetailsActive && MusicPublicId == track) ShowDiscussionStatus("CommentsRetry");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            AppLog.Error("Track discussion operation failed.", error);
            if (IsMusicDetailsActive && MusicPublicId == track) ShowDiscussionStatus("CommentsRetry");
        }
    }

    private void SetDiscussionComposerStatus(string? key)
    {
        _discussionStatus = key;
        DiscussionComposerStatus.Text = key is null ? string.Empty : Text(key);
    }

    private void ShowDiscussionStatus(string key)
    {
        _commentNoticeKey = key;
        CommentNavigationNotice.Message = Text(key);
        CommentNavigationNotice.IsOpen = true;
    }

    private static MusicDetailsCommentItemViewModel? CommentFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as MusicDetailsCommentItemViewModel;
}
