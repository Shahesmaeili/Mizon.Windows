using Microsoft.UI.Composition;
using Microsoft.UI.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Mizon.Windows.Services;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.ViewManagement;
using WindowId = Microsoft.UI.WindowId;

namespace Mizon.Windows.Common;

/// <summary>A media-colored Composition stroke with no per-frame UI work.</summary>
internal sealed class HeroSpectrumBorder : IDisposable
{
    private const float Stroke = .75f;
    private readonly FrameworkElement _host;
    private readonly ThemeSettings? _themeSettings;
    private AccessibilitySettings? _accessibility;
    private readonly ShapeVisual _visual;
    private readonly CompositionRoundedRectangleGeometry _geometry;
    private readonly CompositionSpriteShape _shape;
    private readonly CompositionLinearGradientBrush _brush;
    private readonly CompositionColorGradientStop[] _stops;
    private Animations? _animations;
    private HeroMediaPalette? _palette;
    private bool _visible, _motion, _rotating, _disposed;

    public HeroSpectrumBorder(FrameworkElement host, WindowId windowId)
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
        _themeSettings = ObserveWindowTheme(windowId);
        if (_themeSettings is null) host.ActualThemeChanged += ActualThemeChanged;
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
        _visual.IsVisible = _visible && _palette.HasValue && !ReadHighContrast();
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

    private ThemeSettings? ObserveWindowTheme(WindowId windowId)
    {
        if (windowId.Value == 0) return null;
        try
        {
            var theme = ThemeSettings.CreateForWindowId(windowId);
            theme.Changed += WindowThemeChanged;
            return theme;
        }
        catch (Exception error) when (error is COMException or ArgumentException)
        {
            // A decorative effect must not prevent the desktop player from loading.
            AppLog.Information($"Featured border theme notifications are unavailable ({error.HResult:X8}).");
            return null;
        }
    }

    private bool ReadHighContrast()
    {
        try { return _themeSettings?.HighContrast ?? (_accessibility ??= new()).HighContrast; }
        catch (COMException) { return true; }
    }

    private void WindowThemeChanged(ThemeSettings sender, object args) =>
        _host.DispatcherQueue.TryEnqueue(() => { if (!_disposed) UpdateVisibility(); });

    private void ActualThemeChanged(FrameworkElement sender, object args) => UpdateVisibility();

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
        if (_themeSettings is not null)
        {
            try { _themeSettings.Changed -= WindowThemeChanged; }
            catch (COMException error)
            {
                AppLog.Information($"Featured border theme notifications could not be detached ({error.HResult:X8}).");
            }
        }
        else _host.ActualThemeChanged -= ActualThemeChanged;
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
