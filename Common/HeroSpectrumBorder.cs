using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Common;

/// <summary>A media-colored Composition stroke with no per-frame UI work.</summary>
internal sealed class HeroSpectrumBorder : IDisposable
{
    private const float Stroke = .75f;
    private readonly FrameworkElement _host;
    private readonly AccessibilitySettings _accessibility = new();
    private readonly ShapeVisual _visual;
    private readonly CompositionRoundedRectangleGeometry _geometry;
    private readonly CompositionSpriteShape _shape;
    private readonly CompositionLinearGradientBrush _brush;
    private readonly CompositionColorGradientStop[] _stops;
    private Animations? _animations;
    private HeroMediaPalette? _palette;
    private bool _visible, _motion, _rotating, _disposed;

    public HeroSpectrumBorder(FrameworkElement host)
    {
        _host = host;
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _geometry = compositor.CreateRoundedRectangleGeometry();
        _geometry.Offset = new Vector2(Stroke / 2);
        _brush = compositor.CreateLinearGradientBrush();
        _brush.StartPoint = new Vector2(0, .5f);
        _brush.EndPoint = new Vector2(1, .5f);
        _brush.MappingMode = CompositionMappingMode.Relative;
        _brush.CenterPoint = new Vector2(.5f);
        _stops =
        [
            compositor.CreateColorGradientStop(0, default),
            compositor.CreateColorGradientStop(.22f, default),
            compositor.CreateColorGradientStop(.46f, default),
            compositor.CreateColorGradientStop(.68f, default),
            compositor.CreateColorGradientStop(.84f, default),
            compositor.CreateColorGradientStop(1, default)
        ];
        foreach (var stop in _stops) _brush.ColorStops.Add(stop);
        _shape = compositor.CreateSpriteShape(_geometry);
        _shape.StrokeThickness = Stroke;
        _shape.StrokeBrush = _brush;
        _visual = compositor.CreateShapeVisual();
        _visual.RelativeSizeAdjustment = Vector2.One;
        _visual.IsVisible = false;
        _visual.Shapes.Add(_shape);
        ElementCompositionPreview.SetElementChildVisual(host, _visual);
        host.SizeChanged += SizeChanged;
        _accessibility.HighContrastChanged += HighContrastChanged;
        UpdateBounds(host.ActualWidth, host.ActualHeight);
    }

    public void SetState(bool visible, bool motion)
    {
        _visible = visible;
        _motion = motion;
        UpdateVisibility();
    }

    public void SetPalette(HeroMediaPalette? palette)
    {
        if (_palette == palette) return;
        var blend = _palette.HasValue && _visual.IsVisible && _motion;
        _palette = palette;
        if (palette is { } colors) ApplyColors(colors, blend);
        UpdateVisibility();
    }

    private void ApplyColors(HeroMediaPalette colors, bool blend)
    {
        static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
        Span<Color> targets =
        [
            WithAlpha(colors.Base, 104), WithAlpha(colors.Accent, 220), WithAlpha(colors.Highlight, 255),
            WithAlpha(colors.Accent, 135), WithAlpha(colors.Base, 80), WithAlpha(colors.Base, 104)
        ];
        var animation = blend ? Resources.Blend : null;
        for (var i = 0; i < _stops.Length; i++)
        {
            var stop = _stops[i];
            if (animation is not null)
            {
                animation.InsertKeyFrame(1, targets[i]);
                stop.StartAnimation(nameof(CompositionColorGradientStop.Color), animation);
            }
            else
            {
                stop.StopAnimation(nameof(CompositionColorGradientStop.Color));
                stop.Color = targets[i];
            }
        }
    }

    private Animations Resources => _animations ??= new(_brush.Compositor);

    private void UpdateVisibility()
    {
        _visual.IsVisible = _visible && _palette.HasValue && !_accessibility.HighContrast;
        var rotate = _visual.IsVisible && _motion;
        if (rotate == _rotating) return;
        _rotating = rotate;
        if (rotate)
            _brush.StartAnimation(nameof(CompositionGradientBrush.RotationAngle), Resources.Sweep);
        else
        {
            _brush.StopAnimation(nameof(CompositionGradientBrush.RotationAngle));
            _brush.RotationAngle = 0;
            if (_palette is { } colors) ApplyColors(colors, false);
        }
    }

    private void HighContrastChanged(AccessibilitySettings sender, object args) =>
        _host.DispatcherQueue.TryEnqueue(() => { if (!_disposed) UpdateVisibility(); });

    private void SizeChanged(object sender, SizeChangedEventArgs args) => UpdateBounds(args.NewSize.Width, args.NewSize.Height);

    private void UpdateBounds(double width, double height)
    {
        var size = Vector2.Max(Vector2.Zero, new Vector2((float)width, (float)height) - new Vector2(Stroke));
        _geometry.Size = size;
        _geometry.CornerRadius = new Vector2(Math.Min(12 - Stroke / 2, Math.Min(size.X, size.Y) / 2));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accessibility.HighContrastChanged -= HighContrastChanged;
        _host.SizeChanged -= SizeChanged;
        SetState(false, false);
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _visual.Shapes.Clear();
        _shape.StrokeBrush = null;
        _brush.ColorStops.Clear();
        foreach (var stop in _stops) stop.Dispose();
        _animations?.Dispose();
        _visual.Dispose();
        _shape.Dispose();
        _brush.Dispose();
        _geometry.Dispose();
    }

    private sealed class Animations : IDisposable
    {
        public ScalarKeyFrameAnimation Sweep { get; }
        public ColorKeyFrameAnimation Blend { get; }

        public Animations(Compositor compositor)
        {
            using var linear = compositor.CreateLinearEasingFunction();
            Sweep = compositor.CreateScalarKeyFrameAnimation();
            Sweep.Duration = TimeSpan.FromSeconds(7.6);
            Sweep.IterationBehavior = AnimationIterationBehavior.Forever;
            Sweep.InsertKeyFrame(0, 0);
            Sweep.InsertKeyFrame(1, MathF.Tau, linear);
            Blend = compositor.CreateColorKeyFrameAnimation();
            Blend.Duration = TimeSpan.FromMilliseconds(380);
            Blend.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        }

        public void Dispose()
        {
            Sweep.Dispose();
            Blend.Dispose();
        }
    }
}
