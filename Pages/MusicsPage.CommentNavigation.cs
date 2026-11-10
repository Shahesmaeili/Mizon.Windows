using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Common;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage
{
    private Guid _pendingCommentPublicId;
    private CancellationTokenSource? _commentNavigationCancellation;
    private int _commentNavigationVersion;
    private TaskCompletionSource? _coverNavigationSettled;

    // Also used when an activity points into the music page that is already open.
    public async Task OpenCommentAsync(Guid commentPublicId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commentPublicId == Guid.Empty) return;
        if (!_pageActive)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                _pendingCommentPublicId = commentPublicId;
                return;
            }
            if (!await McpAuthenticationService.WaitForLoadedAsync(this, cancellationToken) || !_pageActive) return;
        }
        CancelCommentNavigation();
        _pendingCommentPublicId = Guid.Empty;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _commentNavigationCancellation = request;
        var token = request.Token;
        var version = _commentNavigationVersion;
        var musicId = MusicPublicId;
        CommentNavigationNotice.IsOpen = false;
        try
        {
            // Frame.Navigated initializes the shared model. The low-priority turn also
            // lets the connected cover start before an API response can scroll the page.
            await NextCommentLayoutTurnAsync(token);
            if (!IsCurrent()) return;
            var found = await ViewModel.FocusMusicCommentAsync(musicId, commentPublicId, token);
            if (!IsCurrent()) return;
            if (_coverNavigationSettled is { Task.IsCompleted: false } cover)
                await cover.Task.WaitAsync(token);
            if (!IsCurrent()) return;
            if (!found)
            {
                ShowCommentNavigationMessage("این نظر دیگر در دسترس نیست یا دریافت آن انجام نشد.");
                return;
            }

            await NextCommentLayoutTurnAsync(token);
            if (!IsCurrent()) return;
            var item = ViewModel.MusicDetailsComments.FirstOrDefault(x => x.PublicId == commentPublicId);
            if (item is null) return;
            var index = ViewModel.MusicDetailsComments.IndexOf(item);
            if (MusicDetailsCommentsRepeater.GetOrCreateElement(index) is not FrameworkElement element) return;
            await NextCommentLayoutTurnAsync(token);
            if (!IsCurrent() || element.DataContext is not MusicDetailsCommentItemViewModel current
                || current.PublicId != commentPublicId) return;

            if (element is Control control) control.Focus(FocusState.Programmatic);
            element.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled,
                VerticalAlignmentRatio = 0.25,
                HorizontalAlignmentRatio = 0.5
            });
            AccessibilityAnnouncements.Announce(element, "نظر موردنظر نمایش داده شد. " + item.AutomationName, "FollowingComment");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Error("Following activity comment destination could not be opened.", exception);
            if (IsCurrent())
            {
                try
                {
                    if (_coverNavigationSettled is { Task.IsCompleted: false } cover)
                        await cover.Task.WaitAsync(token);
                    if (IsCurrent()) ShowCommentNavigationMessage("باز کردن نظر انجام نشد؛ دوباره تلاش کن.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
        }
        finally
        {
            if (ReferenceEquals(_commentNavigationCancellation, request)) _commentNavigationCancellation = null;
        }

        bool IsCurrent() => _pageActive && !token.IsCancellationRequested && version == _commentNavigationVersion
            && MusicPublicId == musicId && ViewModel.CurrentMusicPublicId == musicId;
    }

    private Task NextCommentLayoutTurnAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => completion.TrySetResult()))
            completion.TrySetCanceled();
        return completion.Task.WaitAsync(token);
    }

    private void ShowCommentNavigationMessage(string text)
    {
        CommentNavigationNotice.Message = text;
        CommentNavigationNotice.IsOpen = true;
        CommentNavigationNotice.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled,
            VerticalAlignmentRatio = 0.3
        });
    }

    private void CancelCommentNavigation()
    {
        _commentNavigationVersion++;
        _pendingCommentPublicId = Guid.Empty;
        _commentNavigationCancellation?.Cancel();
        _commentNavigationCancellation?.Dispose();
        _commentNavigationCancellation = null;
    }

    private async void ShowAllMusicComments_Click(object sender, RoutedEventArgs args)
    {
        CancelCommentNavigation();
        CommentNavigationNotice.IsOpen = false;
        try { await ViewModel.SelectCommentsDetailsAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("Music comments could not be loaded.", exception);
            if (_pageActive) ShowCommentNavigationMessage("دریافت نظرات انجام نشد؛ دوباره تلاش کن.");
        }
    }
}
