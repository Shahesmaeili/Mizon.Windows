using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using Mizon.Windows.Services;
using System;
using System.Diagnostics;

namespace Mizon.Windows.Pages;

public sealed partial class MusicsPage
{
    private static readonly TimeSpan WaveformRevealDuration = TimeSpan.FromMilliseconds(390);
    private static readonly TimeSpan CommentRevealDuration = TimeSpan.FromMilliseconds(320);
    private Storyboard? _trackMetadataTransition;
    private (Visual Visual, InsetClip Clip)? _trackWaveformReveal;
    private (Visual Visual, InsetClip Clip)? _trackCommentReveal;
    private long _trackWaveformRevealStart;
    private bool _trackMetadataPending = true;
    private bool _trackWaveformPending = true;
    private bool _trackCommentsPending = true;

    private bool CanAnimateTrackLoad =>
        _pageActive && XamlRoot?.IsHostVisible == true && _waveformAnimations.AnimationsEnabled;

    private void RefreshTrackLoadingPresentation()
    {
        if (!_pageActive) return;

        var trackIsCurrent = IsMusicDetailsActive;
        var metadataPending = !trackIsCurrent || ViewModel.IsTrackMetadataLoading;
        var metadataFailed = trackIsCurrent && !metadataPending && ViewModel.TrackMetadataLoadFailed;
        if (metadataPending || metadataFailed)
        {
            StopTrackMetadataTransition();
            TrackMetadataSkeleton.Visibility = metadataPending ? Visibility.Visible : Visibility.Collapsed;
            TrackMetadataContent.Visibility = Visibility.Collapsed;
            TrackMetadataError.Visibility = metadataFailed ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            TrackMetadataError.Visibility = Visibility.Collapsed;
            TrackMetadataContent.Visibility = Visibility.Visible;
            if (_trackMetadataPending)
            {
                if (CanAnimateTrackLoad && TrackMetadataContent.IsLoaded)
                    StartTrackMetadataTransition();
                else
                    TrackMetadataSkeleton.Visibility = Visibility.Collapsed;
            }
        }
        _trackMetadataPending = metadataPending;

        var waveformPending = !trackIsCurrent || ViewModel.IsTrackWaveformLoading;
        var commentsPending = !trackIsCurrent || ViewModel.IsTrackCommentsLoading;
        TrackWaveformSkeleton.Visibility = waveformPending ? Visibility.Visible : Visibility.Collapsed;
        WaveformScroller.IsTabStop = !waveformPending && ViewModel.Bars.Count > 0;
        if (waveformPending && !_trackWaveformPending)
            StopTrackTimelineTransitions();
        var revealBars = _trackWaveformPending && !waveformPending && ViewModel.Bars.Count > 0;
        var revealComments = _trackCommentsPending && !commentsPending && ViewModel.EqualizerComments.Count > 0;
        _trackWaveformPending = waveformPending;
        _trackCommentsPending = commentsPending;
        if (CanAnimateTrackLoad && (revealBars || revealComments))
            QueueTrackTimelineReveal(revealBars, revealComments);
    }

    private void StartTrackMetadataTransition()
    {
        StopTrackMetadataTransition();
        _trackMetadataTransition = new Storyboard();
        AddOpacityAnimation(TrackMetadataContent, 0, 1);
        AddOpacityAnimation(TrackMetadataSkeleton, 1, 0);
        _trackMetadataTransition.Completed += TrackMetadataTransition_Completed;
        _trackMetadataTransition.Begin();

        void AddOpacityAnimation(UIElement target, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(180))
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, nameof(Opacity));
            _trackMetadataTransition.Children.Add(animation);
        }
    }

    private void TrackMetadataTransition_Completed(object? sender, object args)
    {
        if (!ReferenceEquals(sender, _trackMetadataTransition)) return;
        TrackMetadataSkeleton.Visibility = Visibility.Collapsed;
        StopTrackMetadataTransition();
    }

    private void StopTrackMetadataTransition()
    {
        if (_trackMetadataTransition is not { } transition) return;
        _trackMetadataTransition = null;
        transition.Completed -= TrackMetadataTransition_Completed;
        transition.Stop();
    }

    private void QueueTrackTimelineReveal(bool bars, bool comments)
    {
        var trackId = MusicPublicId;
        if (bars) TrackWaveformLayer.Opacity = 0;
        if (comments) TrackCommentLayer.Opacity = 0;
        if (DispatcherQueue.TryEnqueue(() =>
        {
            if (MusicPublicId != trackId || !IsMusicDetailsActive) return;
            if (!CanAnimateTrackLoad)
            {
                StopTrackTimelineTransitions();
                return;
            }
            if (bars && !ViewModel.IsTrackWaveformLoading && TrackWaveformLayer.IsLoaded)
            {
                StartTrackTimelineSweep(TrackWaveformLayer, ref _trackWaveformReveal, WaveformRevealDuration, TimeSpan.Zero);
                _trackWaveformRevealStart = Stopwatch.GetTimestamp();
            }
            if (comments && !ViewModel.IsTrackCommentsLoading && TrackCommentLayer.IsLoaded)
            {
                var delay = _trackWaveformRevealStart == 0
                    ? TimeSpan.Zero
                    : WaveformRevealDuration - Stopwatch.GetElapsedTime(_trackWaveformRevealStart);
                StartTrackTimelineSweep(TrackCommentLayer, ref _trackCommentReveal, CommentRevealDuration, delay);
            }
            if (bars) TrackWaveformLayer.Opacity = 1;
            if (comments) TrackCommentLayer.Opacity = 1;
        })) return;

        StopTrackTimelineTransitions();
    }

    private void StartTrackTimelineSweep(FrameworkElement target, ref (Visual Visual, InsetClip Clip)? reveal,
        TimeSpan duration, TimeSpan delay)
    {
        if (reveal is null)
        {
            var layer = ElementCompositionPreview.GetElementVisual(target);
            reveal = (layer, layer.Compositor.CreateInsetClip());
        }
        var (visual, clip) = reveal.Value;
        visual.Clip = clip;
        var width = MusicWaveformTimeline.Width;
        var start = (float)Math.Clamp(width - WaveformScroller.HorizontalOffset, 0, width);
        var visibleEnd = (float)Math.Max(0, start - WaveformScroller.ViewportWidth);
        using var sweep = visual.Compositor.CreateScalarKeyFrameAnimation();
        sweep.Duration = duration;
        sweep.DelayTime = delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        sweep.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        sweep.InsertKeyFrame(0, start);
        sweep.InsertKeyFrame(0.88f, visibleEnd);
        sweep.InsertKeyFrame(1, 0f);
        clip.StartAnimation(nameof(InsetClip.RightInset), sweep);
    }

    private void StopTrackTimelineTransitions()
    {
        ReleaseClip(ref _trackWaveformReveal);
        ReleaseClip(ref _trackCommentReveal);
        TrackWaveformLayer.Opacity = 1;
        TrackCommentLayer.Opacity = 1;
        _trackWaveformRevealStart = 0;

        static void ReleaseClip(ref (Visual Visual, InsetClip Clip)? reveal)
        {
            if (reveal is not { } resources) return;
            var (visual, clip) = resources;
            clip.StopAnimation(nameof(InsetClip.RightInset));
            visual.Clip = null;
            clip.Dispose();
            reveal = null;
        }
    }

    private void StopTrackLoadMotion()
    {
        StopTrackMetadataTransition();
        StopTrackTimelineTransitions();
        if (!_trackMetadataPending)
            TrackMetadataSkeleton.Visibility = Visibility.Collapsed;
    }
}
