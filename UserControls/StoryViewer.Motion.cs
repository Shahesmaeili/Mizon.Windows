using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class StoryViewer
{
    private static readonly UISettings MotionSettings = new();
    private ViewerMotion? _motion;
    private readonly List<ViewerMotion> _retiredMotion = [];

    // Called before the control is attached: its first presented frame belongs
    // to the entrance animation, rather than flashing at full opacity first.
    private void PrepareMotion()
    {
        ReleaseMotion();
        ViewerRoot.Opacity = MotionSettings.AnimationsEnabled ? 0 : 1;
    }

    private Task AnimateOpenAsync(CancellationToken token, bool connected = false) =>
        AnimateViewerAsync(opening: true, connected, token);

    private Task AnimateCloseAsync(CancellationToken token) =>
        AnimateViewerAsync(opening: false, connected: false, token);

    private async Task AnimateViewerAsync(bool opening, bool connected, CancellationToken token)
    {
        var opacity = opening ? 1f : 0f;
        var scale = opening ? Vector3.One : new Vector3(.98f, .98f, 1);
        if (token.IsCancellationRequested || !IsLoaded || !MotionSettings.AnimationsEnabled || XamlRoot?.IsHostVisible == false)
        {
            ReleaseMotion();
            ViewerRoot.Opacity = opacity;
            return;
        }

        var previous = _motion;
        _motion = null;
        // Keep the compositor's current value until the replacement animation
        // starts. Stopping the old animation here would snap a half-open card
        // to its base value before the close animation could take over.
        if (previous is not null)
        {
            DetachMotionEvents(previous);
            previous.Retire();
            _retiredMotion.Add(previous);
        }
        var motion = _motion = new ViewerMotion(ViewerRoot, ViewerCard, XamlRoot);
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(ViewerRoot).Compositor;
            motion.Easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
            motion.Opacity = compositor.CreateScalarKeyFrameAnimation();
            motion.Opacity.Target = nameof(UIElement.Opacity);
            motion.Opacity.Duration = TimeSpan.FromMilliseconds(opening ? 220 : 160);
            if (opening)
            {
                ViewerRoot.Opacity = 1;
                motion.Opacity.InsertKeyFrame(0, 0);
            }
            else motion.Opacity.InsertExpressionKeyFrame(0, "this.StartingValue");
            motion.Opacity.InsertKeyFrame(1, opacity, motion.Easing);

            // ConnectedAnimation owns the geometry of its destination. Keep
            // that destination stationary while the whole overlay fades in.
            if (!opening || !connected)
            {
                CenterMotion();
                motion.Scale = compositor.CreateVector3KeyFrameAnimation();
                motion.Scale.Target = nameof(UIElement.Scale);
                motion.Scale.Duration = motion.Opacity.Duration;
                if (opening) motion.Scale.InsertKeyFrame(0, new Vector3(.96f, .96f, 1));
                else motion.Scale.InsertExpressionKeyFrame(0, "this.StartingValue");
                motion.Scale.InsertKeyFrame(1, scale, motion.Easing);
            }

            MotionSettings.AnimationsEnabledChanged += MotionPreferenceChanged;
            motion.SettingsAttached = true;
            ViewerCard.SizeChanged += MotionCardSizeChanged;
            motion.SizeAttached = true;
            if (motion.Root is not null)
            {
                motion.Root.Changed += MotionRootChanged;
                motion.RootAttached = true;
            }
            motion.Batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            motion.Batch.Completed += motion.Completed;
            try
            {
                ViewerRoot.StartAnimation(motion.Opacity);
                if (motion.Scale is not null) ViewerCard.StartAnimation(motion.Scale);
            }
            finally { motion.Batch.End(); }

            // Minimized/removed windows occasionally omit Completed. A bounded
            // wait also prevents an invisible overlay from keeping input blocked.
            await motion.Completion.Task.WaitAsync(TimeSpan.FromSeconds(1), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (TimeoutException) { }
        catch (Exception exception) { AppLog.Error("Story viewer motion was skipped.", exception); }
        finally
        {
            if (ReferenceEquals(_motion, motion))
            {
                _motion = null;
                DetachMotionEvents(motion);
                motion.Dispose(stopAnimations: true);
                ReleaseRetiredMotion();
                ViewerRoot.Opacity = opacity;
                ViewerCard.Scale = scale;
                ViewerCard.CenterPoint = Vector3.Zero;
            }
            else
            {
                DetachMotionEvents(motion);
                // The replacement owns this retired animation until it stops.
                // XAML can still read its parameters after StartAnimation returns;
                // disposing them here causes native RO_E_CLOSED during rapid close.
            }
        }
    }

    private void MotionPreferenceChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!MotionSettings.AnimationsEnabled) _motion?.Completion.TrySetResult();
    });

    private void MotionRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (ReferenceEquals(_motion?.Root, sender) && !sender.IsHostVisible) _motion?.Completion.TrySetResult();
    }

    private void MotionCardSizeChanged(object sender, SizeChangedEventArgs args) => CenterMotion();

    private void CenterMotion() => ViewerCard.CenterPoint = new Vector3(
        (float)ViewerCard.ActualWidth / 2, (float)ViewerCard.ActualHeight / 2, 0);

    private void DetachMotionEvents(ViewerMotion motion)
    {
        if (motion.SettingsAttached)
        {
            MotionSettings.AnimationsEnabledChanged -= MotionPreferenceChanged;
            motion.SettingsAttached = false;
        }
        if (motion.SizeAttached)
        {
            ViewerCard.SizeChanged -= MotionCardSizeChanged;
            motion.SizeAttached = false;
        }
        if (motion.RootAttached && motion.Root is not null)
        {
            motion.Root.Changed -= MotionRootChanged;
            motion.RootAttached = false;
        }
    }

    private void ReleaseMotion()
    {
        var motion = _motion;
        _motion = null;
        if (motion is not null)
        {
            DetachMotionEvents(motion);
            motion.Dispose(stopAnimations: true);
        }
        ReleaseRetiredMotion();
        ViewerRoot.Opacity = 1;
        ViewerCard.Scale = Vector3.One;
        ViewerCard.CenterPoint = Vector3.Zero;
    }

    private void ReleaseRetiredMotion()
    {
        foreach (var motion in _retiredMotion) motion.Dispose(stopAnimations: true);
        _retiredMotion.Clear();
    }

    private sealed class ViewerMotion(UIElement surface, UIElement card, XamlRoot? root)
    {
        private bool _disposed;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public XamlRoot? Root { get; } = root;
        public bool SettingsAttached { get; set; }
        public bool SizeAttached { get; set; }
        public bool RootAttached { get; set; }
        public ScalarKeyFrameAnimation? Opacity { get; set; }
        public Vector3KeyFrameAnimation? Scale { get; set; }
        public CubicBezierEasingFunction? Easing { get; set; }
        public CompositionScopedBatch? Batch { get; set; }

        public void Completed(object sender, CompositionBatchCompletedEventArgs args) => Completion.TrySetResult();

        public void Retire()
        {
            if (Batch is not null) Batch.Completed -= Completed;
            Completion.TrySetResult();
        }

        public void Dispose(bool stopAnimations)
        {
            if (_disposed) return;
            _disposed = true;
            Completion.TrySetResult();
            if (Batch is not null)
            {
                Batch.Completed -= Completed;
                Batch.Dispose();
                Batch = null;
            }
            if (Opacity is not null)
            {
                if (stopAnimations) surface.StopAnimation(Opacity);
                Opacity.Dispose();
                Opacity = null;
            }
            if (Scale is not null)
            {
                if (stopAnimations) card.StopAnimation(Scale);
                Scale.Dispose();
                Scale = null;
            }
            Easing?.Dispose();
            Easing = null;
            // XAML owns the surface, card, their Visuals and animation controllers.
        }
    }
}
