using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mizon.Windows.ViewModels.Items;
using System;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage
{
    private readonly UISettings _waveformAnimations = new();
    private Button? _hoveredCommentMarker;
    private Button? _focusedCommentMarker;
    private Button? _raisedCommentMarker;

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
        var active = _hoveredCommentMarker ?? _focusedCommentMarker;
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

        if (animate)
            marker.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(160) };
        else
            marker.OpacityTransition = null;
        marker.Opacity = raised ? 1 : 0.5;
    }
}
