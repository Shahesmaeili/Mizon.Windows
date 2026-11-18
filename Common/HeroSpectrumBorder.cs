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
    private readonly CompositionRadialGradientBrush _brush;
    private readonly CompositionColorGradientStop[] _stops;
    private readonly ExpressionAnimation _followPointer;
    private readonly ScalarKeyFrameAnimation _fade;
    private ColorKeyFrameAnimation? _blend;
    private HeroMediaPalette? _palette;
    private bool _visible, _motion, _pointerOver, _shown, _disposed;

    public HeroSpectrumBorder(FrameworkElement host, FrameworkElement pointerTarget, WindowId windowId)
    {
        _host = host;
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _geometry = compositor.CreateRoundedRectangleGeometry();
        _geometry.Offset = new Vector2(Stroke / 2);
        _brush = compositor.CreateRadialGradientBrush();
        _brush.MappingMode = CompositionMappingMode.Absolute;
        _stops =
        [
            compositor.CreateColorGradientStop(0, default),
            compositor.CreateColorGradientStop(.30f, default),
            compositor.CreateColorGradientStop(.67f, default),
            compositor.CreateColorGradientStop(1, default)
        ];
        foreach (var stop in _stops) _brush.ColorStops.Add(stop);
        _shape = compositor.CreateSpriteShape(_geometry);
        _shape.StrokeThickness = Stroke;
        _shape.StrokeBrush = _brush;
        _visual = compositor.CreateShapeVisual();
        _visual.RelativeSizeAdjustment = Vector2.One;
        _visual.IsVisible = false;
        _visual.Opacity = 0;
        _visual.Shapes.Add(_shape);
        _followPointer = compositor.CreateExpressionAnimation("Vector2(hover.Position.X, hover.Position.Y)");
        _followPointer.SetReferenceParameter("hover", ElementCompositionPreview.GetPointerPositionPropertySet(pointerTarget));
        _fade = compositor.CreateScalarKeyFrameAnimation();
        _fade.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        _fade.Duration = TimeSpan.FromMilliseconds(160);
        ElementCompositionPreview.SetElementChildVisual(host, _visual);
        host.SizeChanged += SizeChanged;
        _themeSettings = ObserveWindowTheme(windowId);
        if (_themeSettings is null) host.ActualThemeChanged += ActualThemeChanged;
        UpdateBounds(host.ActualWidth, host.ActualHeight);
    }

    public void SetState(bool visible, bool motion)
    {
        if (_visible == visible && _motion == motion) return;
        if (_motion && !motion && _palette is { } colors) ApplyColors(colors, false);
        _visible = visible;
        _motion = motion;
        if (!visible) _pointerOver = false;
        UpdateVisibility();
    }

    public void UpdatePointer(Vector2 point, bool isOver)
    {
        if (!_visible || _pointerOver == isOver) return;
        _pointerOver = isOver;
        // Keep the light at the exit edge until the opacity fade ends.
        _brush.EllipseCenter = Vector2.Clamp(point, Vector2.Zero, _geometry.Size);
        UpdateVisibility();
    }

    public void SetPalette(HeroMediaPalette? palette)
    {
        if (_palette == palette) return;
        var blend = _palette.HasValue && _shown && _motion;
        _palette = palette;
        if (palette is { } colors) ApplyColors(colors, blend);
        UpdateVisibility();
    }

    private void ApplyColors(HeroMediaPalette colors, bool blend)
    {
        static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
        Span<Color> targets =
        [
            WithAlpha(colors.Highlight, 255), WithAlpha(colors.Accent, 230),
            WithAlpha(colors.Base, 150), WithAlpha(colors.Base, 0)
        ];
        var animation = blend ? _blend ??= CreateBlend() : null;
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

    private ColorKeyFrameAnimation CreateBlend()
    {
        var animation = _brush.Compositor.CreateColorKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(380);
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        return animation;
    }

    private void UpdateVisibility()
    {
        var enabled = _visible && _palette.HasValue && !ReadHighContrast();
        _visual.IsVisible = enabled;
        var show = enabled && _pointerOver;
        if (show != _shown)
        {
            _shown = show;
            if (show) _brush.StartAnimation(nameof(CompositionRadialGradientBrush.EllipseCenter), _followPointer);
            else _brush.StopAnimation(nameof(CompositionRadialGradientBrush.EllipseCenter));
            if (enabled && _motion)
            {
                _fade.InsertKeyFrame(1, show ? 1 : 0);
                _visual.StartAnimation(nameof(Visual.Opacity), _fade);
                return;
            }
        }
        if (!enabled || !_motion)
        {
            _visual.StopAnimation(nameof(Visual.Opacity));
            _visual.Opacity = show ? 1 : 0;
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
        _brush.EllipseRadius = new Vector2(Math.Clamp(Math.Min(size.X, size.Y) * .8f, 130, 250));
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
        _followPointer.Dispose();
        _fade.Dispose();
        _blend?.Dispose();
        _visual.Dispose();
        _shape.Dispose();
        _brush.Dispose();
        _geometry.Dispose();
    }
}
