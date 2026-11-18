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
    private ScalarKeyFrameAnimation? _fade;
    private Vector3KeyFrameAnimation? _drift;

    public void Start(int direction, TimeSpan duration, FrameworkElement? bounds = null)
    {
        Finish();
        if (duration <= TimeSpan.Zero) return;

        var compositor = ElementCompositionPreview.GetElementVisual(layer).Compositor;
        bounds ??= layer;
        layer.CenterPoint = new((float)bounds.ActualWidth * (direction < 0 ? .78f : .22f),
            (float)bounds.ActualHeight / 2, 0);
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.18f, .72f), Vector2.One);
        var fade = _fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = nameof(UIElement.Opacity);
        fade.Duration = duration;
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(.72f, .96f, easing);
        fade.InsertKeyFrame(1, 1);
        var drift = _drift = compositor.CreateVector3KeyFrameAnimation();
        drift.Target = nameof(UIElement.Scale);
        drift.Duration = duration;
        drift.InsertKeyFrame(0, new Vector3(1.055f, 1.055f, 1));
        drift.InsertKeyFrame(1, Vector3.One, easing);
        _batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _batch.Completed += Completed;
        layer.StartAnimation(fade);
        layer.StartAnimation(drift);
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
        if (_fade is not null)
        {
            layer.StopAnimation(_fade);
            _fade.Dispose();
            _fade = null;
        }
        if (_drift is not null)
        {
            layer.StopAnimation(_drift);
            _drift.Dispose();
            _drift = null;
        }
        layer.Opacity = 1;
        layer.Scale = Vector3.One;
        onFinished?.Invoke();
    }

    public void Dispose() => Finish();
}
