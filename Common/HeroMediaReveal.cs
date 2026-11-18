using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;

namespace Mizon.Windows.Common;

// The old image stays opaque underneath. Only the decoded incoming layer moves,
// leaving the layout, hit targets, and player controls outside the transition.
internal sealed class HeroMediaReveal(FrameworkElement layer, Action? onFinished = null) : IDisposable
{
    private CompositionScopedBatch? _batch;
    private Visual? _visual;
    private Vector3 _originalCenter;

    public void Start(int direction, TimeSpan duration, FrameworkElement? bounds = null)
    {
        Finish();
        if (duration <= TimeSpan.Zero) return;

        // Obtaining the element's visual reserves the transform for Composition.
        // Keep the pivot, scale and opacity on that same visual.
        var visual = _visual = ElementCompositionPreview.GetElementVisual(layer);
        var compositor = visual.Compositor;
        bounds ??= layer;
        _originalCenter = visual.CenterPoint;
        visual.CenterPoint = new((float)bounds.ActualWidth * (direction < 0 ? .78f : .22f),
            (float)bounds.ActualHeight / 2, 0);
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.18f, .72f), Vector2.One);
        using var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = duration;
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(.72f, .96f, easing);
        fade.InsertKeyFrame(1, 1);
        using var drift = compositor.CreateVector3KeyFrameAnimation();
        drift.Duration = duration;
        drift.InsertKeyFrame(0, new Vector3(1.055f, 1.055f, 1));
        drift.InsertKeyFrame(1, Vector3.One, easing);
        _batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _batch.Completed += Completed;
        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation(nameof(Visual.Scale), drift);
        _batch.End();
    }

    private void Completed(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (ReferenceEquals(sender, _batch)) Finish();
    }

    public void Finish()
    {
        if (_batch is null) return;
        _batch.Completed -= Completed;
        _batch.Dispose();
        _batch = null;
        if (_visual is { } visual)
        {
            _visual = null;
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation(nameof(Visual.Scale));
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            visual.CenterPoint = _originalCenter;
        }
        onFinished?.Invoke();
    }

    public void Dispose() => Finish();
}
