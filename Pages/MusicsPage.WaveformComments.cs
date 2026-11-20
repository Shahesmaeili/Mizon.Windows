using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Linq;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage
{
    private readonly UISettings _waveformAnimations = new();
    private Button? _hoveredCommentMarker;
    private Button? _focusedCommentMarker;
    private Button? _raisedCommentMarker;
    private Button? _previewCommentMarker;
    private EqualizerCommentItemViewModel? _previewComment;
    private Guid _commentLinkAfterClose;

    private void WaveformComment_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button marker) ShowWaveformCommentPreview(marker);
    }

    private bool ShowWaveformCommentPreview(Button marker)
    {
        if (!IsMusicDetailsActive || marker.XamlRoot is null || !marker.IsLoaded ||
            marker.DataContext is not EqualizerCommentItemViewModel { PublicId: var commentId } comment || commentId == Guid.Empty)
            return false;
        if (ReferenceEquals(marker, _previewCommentMarker)) return true;
        if (_previewComment is not null) return false;

        _previewComment = comment;
        _previewCommentMarker = marker;
        WaveformCommentPreviewText.Text = comment.Text;
        try
        {
            WaveformCommentPreviewFlyout.ShowAt(marker);
            UpdateWaveformCommentEmphasis();
            return true;
        }
        catch (Exception error)
        {
            ClearWaveformCommentPreview();
            WaveformCommentPreviewText.Text = string.Empty;
            AppLog.Error("The waveform comment preview could not be opened.", error);
            return false;
        }
    }

    private bool ShowWaveformCommentPreview(Guid id)
    {
        var item = ViewModel.EqualizerComments.FirstOrDefault(comment => comment.PublicId == id);
        return item is not null && WaveformCommentMarkers.ContainerFromItem(item) is DependencyObject container &&
            FindWaveformMarker(container) is { } marker && ShowWaveformCommentPreview(marker);
    }

    private static Button? FindWaveformMarker(DependencyObject root)
    {
        if (root is Button button) return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindWaveformMarker(VisualTreeHelper.GetChild(root, index)) is { } marker) return marker;
        return null;
    }

    private bool CloseWaveformCommentPreview()
    {
        _commentLinkAfterClose = Guid.Empty;
        if (_previewComment is null) return false;
        ClearWaveformCommentPreview();
        WaveformCommentPreviewFlyout.Hide();
        return true;
    }

    private void ClearWaveformCommentPreview()
    {
        _previewComment = null;
        _previewCommentMarker = null;
        UpdateWaveformCommentEmphasis();
    }

    private void WaveformCommentPreview_Opened(object sender, object args)
    {
        if (_previewComment is null) return;
        AutomationProperties.SetFullDescription(WaveformCommentOpenButton, _previewComment.Text);
        WaveformCommentOpenButton.Focus(FocusState.Programmatic);
    }

    private async void WaveformCommentPreview_Closed(object sender, object args)
    {
        ClearWaveformCommentPreview();
        WaveformCommentPreviewText.Text = string.Empty;
        AutomationProperties.SetFullDescription(WaveformCommentOpenButton, string.Empty);
        var commentId = _commentLinkAfterClose;
        _commentLinkAfterClose = Guid.Empty;
        if (commentId != Guid.Empty && IsMusicDetailsActive) await OpenCommentAsync(commentId);
    }

    private void WaveformCommentOpen_Click(object sender, RoutedEventArgs args)
    {
        if (_previewComment is not { } comment) return;
        _commentLinkAfterClose = comment.PublicId;
        WaveformCommentPreviewFlyout.Hide();
    }

    private void WaveformCommentPreview_PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.OriginalKey != VirtualKey.GamepadB && args.Key != VirtualKey.Escape) return;
        args.Handled = true;
        if (!args.KeyStatus.WasKeyDown) CloseWaveformCommentPreview();
    }

    private void AttachWaveformMotion()
    {
        _waveformAnimations.AnimationsEnabledChanged += WaveformMotionChanged;
        RefreshWaveformMotion();
    }

    private void WaveformMotionChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_pageActive) return;
            RefreshWaveformMotion();
            if (!_waveformAnimations.AnimationsEnabled) StopTrackLoadMotion();
        });

    private void RefreshWaveformMotion()
    {
        var animate = _waveformAnimations.AnimationsEnabled;
        for (var index = 0; index < ViewModel.Bars.Count; index++)
            SetWaveformBarMotion(WaveformBars.TryGetElement(index), animate);
    }

    private void WaveformBar_Loaded(object sender, RoutedEventArgs args) =>
        SetWaveformBarMotion((FrameworkElement)sender, _waveformAnimations.AnimationsEnabled);

    private void WaveformBar_Unloaded(object sender, RoutedEventArgs args) =>
        SetWaveformBarMotion((FrameworkElement)sender, animate: false);

    private static void SetWaveformBarMotion(UIElement? element, bool animate)
    {
        if (element is not FrameworkElement bar ||
            bar.FindName("TopBar") is not Border top || bar.FindName("BottomBar") is not Border bottom)
            return;

        animate &= bar.IsLoaded;
        top.BackgroundTransition = animate
            ? top.BackgroundTransition ?? new BrushTransition { Duration = TimeSpan.FromMilliseconds(120) }
            : null;
        bottom.BackgroundTransition = animate
            ? bottom.BackgroundTransition ?? new BrushTransition { Duration = TimeSpan.FromMilliseconds(180) }
            : null;
    }

    private void WaveformComment_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (!_pageActive || args.Pointer.PointerDeviceType == PointerDeviceType.Touch || sender is not Button marker)
            return;

        _hoveredCommentMarker = marker;
        UpdateWaveformCommentEmphasis();
    }

    private void WaveformComment_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (!ReferenceEquals(sender, _hoveredCommentMarker))
            return;

        _hoveredCommentMarker = null;
        UpdateWaveformCommentEmphasis();
    }

    private void WaveformComment_GotFocus(object sender, RoutedEventArgs args)
    {
        if (!_pageActive || sender is not Button { FocusState: FocusState.Keyboard or FocusState.Programmatic } marker)
            return;

        _focusedCommentMarker = marker;
        UpdateWaveformCommentEmphasis();
    }

    private void WaveformComment_LostFocus(object sender, RoutedEventArgs args)
    {
        if (!ReferenceEquals(sender, _focusedCommentMarker))
            return;

        _focusedCommentMarker = null;
        UpdateWaveformCommentEmphasis();
    }

    private void WaveformComment_Unloaded(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _previewCommentMarker)) CloseWaveformCommentPreview();
        if (ReferenceEquals(sender, _hoveredCommentMarker))
            _hoveredCommentMarker = null;
        if (ReferenceEquals(sender, _focusedCommentMarker))
            _focusedCommentMarker = null;

        UpdateWaveformCommentEmphasis();
    }

    private void ClearWaveformCommentEmphasis()
    {
        _hoveredCommentMarker = null;
        _focusedCommentMarker = null;
        UpdateWaveformCommentEmphasis();
    }

    private void UpdateWaveformCommentEmphasis()
    {
        var active = _previewCommentMarker ?? _hoveredCommentMarker ?? _focusedCommentMarker;
        if (ReferenceEquals(active, _raisedCommentMarker))
            return;

        var previous = _raisedCommentMarker;
        _raisedCommentMarker = active;
        var animate = _pageActive && _waveformAnimations.AnimationsEnabled;
        if (previous is not null)
            ApplyWaveformCommentEmphasis(previous, false, animate);
        if (active is not null)
            ApplyWaveformCommentEmphasis(active, true, animate);
    }

    private void ApplyWaveformCommentEmphasis(Button marker, bool raised, bool animate)
    {
        if (marker.DataContext is EqualizerCommentItemViewModel item &&
            WaveformCommentMarkers.ContainerFromItem(item) is UIElement container)
            Canvas.SetZIndex(container, raised ? 1 : 0);

        if (animate && marker.IsLoaded)
            marker.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(160) };
        else
            marker.OpacityTransition = null;
        marker.Opacity = raised ? 1 : 0.5;
    }
}
