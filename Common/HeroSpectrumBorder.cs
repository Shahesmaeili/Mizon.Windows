using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Mizon.Windows.Services;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
using WindowId = Microsoft.UI.WindowId;

namespace Mizon.Windows.Common;

/// <summary>A media-colored stroke that responds to the mouse around the player.</summary>
internal sealed class HeroSpectrumBorder : IDisposable
{
    private const float Stroke = .75f;
    private const float Proximity = 76;
    private readonly FrameworkElement _host;
    private readonly FrameworkElement _pointerRoot;
    private readonly PointerEventHandler _pointerMoved;
    private readonly PointerEventHandler _pointerExited;
    private readonly PointerEventHandler _pointerCanceled;
    private readonly ThemeSettings? _themeSettings;
    private AccessibilitySettings? _accessibility;
    private readonly ContainerVisual _visual;
    private readonly ShapeVisual _stroke;
    private readonly CompositionRoundedRectangleGeometry _geometry;
    private readonly CompositionSpriteShape _shape;
    private readonly CompositionRadialGradientBrush _brush;
    private readonly CompositionColorGradientStop[] _stops;
    private readonly ScalarKeyFrameAnimation _fade;
    private ColorKeyFrameAnimation? _blend;
    private HeroBorderHalo? _halo;
    private HeroMediaPalette? _palette;
    private Point? _pointer;
    private Vector2 _size;
    private float _opacity;
    private bool _visible, _motion, _enabled, _queued, _disposed, _haloUnavailable;

    public HeroSpectrumBorder(FrameworkElement host, FrameworkElement pointerRoot, WindowId windowId)
    {
        _host = host;
        _pointerRoot = pointerRoot;
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
        _visual = compositor.CreateContainerVisual();
        _visual.RelativeSizeAdjustment = Vector2.One;
        _visual.IsVisible = false;
        _visual.Opacity = 0;
        _stroke = compositor.CreateShapeVisual();
        _stroke.RelativeSizeAdjustment = Vector2.One;
        _stroke.Shapes.Add(_shape);
        _visual.Children.InsertAtTop(_stroke);
        _fade = compositor.CreateScalarKeyFrameAnimation();
        _fade.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        _fade.Duration = TimeSpan.FromMilliseconds(125);
        ElementCompositionPreview.SetElementChildVisual(host, _visual);
        host.SizeChanged += SizeChanged;
        _pointerMoved = PointerMoved;
        _pointerExited = PointerExited;
        _pointerCanceled = PointerCanceled;
        pointerRoot.AddHandler(UIElement.PointerEnteredEvent, _pointerMoved, handledEventsToo: true);
        pointerRoot.AddHandler(UIElement.PointerMovedEvent, _pointerMoved, handledEventsToo: true);
        pointerRoot.AddHandler(UIElement.PointerExitedEvent, _pointerExited, handledEventsToo: true);
        pointerRoot.AddHandler(UIElement.PointerCanceledEvent, _pointerCanceled, handledEventsToo: true);
        pointerRoot.SizeChanged += PointerRootSizeChanged;
        _themeSettings = ObserveWindowTheme(windowId);
        if (_themeSettings is null) host.ActualThemeChanged += ActualThemeChanged;
        UpdateBounds(host.ActualWidth, host.ActualHeight);
    }

    public void SetState(bool visible, bool motion)
    {
        if (_visible == visible && _motion == motion) return;
        if (_motion && !motion)
        {
            if (_palette is { } colors) ApplyColors(colors, false);
            _visual.StopAnimation(nameof(Visual.Opacity));
            _visual.Opacity = _opacity;
        }
        _visible = visible;
        _motion = motion;
        UpdateVisibility();
    }

    public void ViewportChanged()
    {
        if (_disposed || !_enabled || !_pointer.HasValue || _queued) return;
        _queued = _host.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, UpdatePointerLight);
    }

    private void PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        _pointer = args.GetCurrentPoint(_pointerRoot).Position;
        ViewportChanged();
    }

    private void PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        var point = args.GetCurrentPoint(_pointerRoot).Position;
        // Moving between descendants still leaves the pointer on the page.
        if (!ReferenceEquals(args.OriginalSource, _pointerRoot) && point.X >= 0 && point.Y >= 0 &&
            point.X < _pointerRoot.ActualWidth && point.Y < _pointerRoot.ActualHeight) return;
        _pointer = null;
        SetOpacity(0, _enabled && _motion);
    }

    private void PointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        _pointer = null;
        SetOpacity(0, _enabled && _motion);
    }

    private void UpdatePointerLight()
    {
        _queued = false;
        if (_disposed || !_enabled || _pointer is not { } pointer) return;
        if (_host.XamlRoot is not { } xamlRoot || !ReferenceEquals(_pointerRoot.XamlRoot, xamlRoot))
        {
            SetOpacity(0, animate: false);
            return;
        }

        var point = _pointerRoot.TransformToVisual(_host).TransformPoint(pointer);
        var location = new Vector2((float)point.X, (float)point.Y);
        var opacity = ProximityOpacity(location, _size);
        if (opacity > 0 && _brush.EllipseCenter != location) _brush.EllipseCenter = location;
        if (_opacity != opacity) SetOpacity(opacity, _motion);
    }

    internal static float ProximityOpacity(Vector2 point, Vector2 size)
    {
        if (size.X <= 0 || size.Y <= 0 || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return 0;
        var distance = Vector2.Distance(point, Vector2.Clamp(point, Vector2.Zero, size));
        var strength = Math.Clamp(1 - distance / Proximity, 0, 1);
        return strength * strength * (3 - 2 * strength);
    }

    public void SetPalette(HeroMediaPalette? palette)
    {
        if (_palette == palette) return;
        var blend = _palette.HasValue && _opacity > 0 && _motion;
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
        _enabled = _visible && _palette.HasValue && !ReadHighContrast();
        _visual.IsVisible = _enabled;
        if (_enabled) ViewportChanged();
        else SetOpacity(0, animate: false);
    }

    private void SetOpacity(float opacity, bool animate)
    {
        if (opacity > 0 && _halo is null) ResizeHalo(create: true);
        if (animate)
        {
            if (_opacity == opacity || (opacity is > 0 and < 1 && Math.Abs(_opacity - opacity) < .02f)) return;
            _fade.InsertKeyFrame(1, opacity);
            _visual.StartAnimation(nameof(Visual.Opacity), _fade);
        }
        else
        {
            _visual.StopAnimation(nameof(Visual.Opacity));
            _visual.Opacity = opacity;
        }
        _opacity = opacity;
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
    private void PointerRootSizeChanged(object sender, SizeChangedEventArgs args) => ViewportChanged();

    private void UpdateBounds(double width, double height)
    {
        var size = new Vector2((float)width, (float)height);
        if (_size == size) return;
        _size = size;
        _geometry.Size = Vector2.Max(Vector2.Zero, _size - new Vector2(Stroke));
        var minimum = Math.Min(_geometry.Size.X, _geometry.Size.Y);
        _geometry.CornerRadius = new Vector2(Math.Min(12 - Stroke / 2, minimum / 2));
        _brush.EllipseRadius = new Vector2(Math.Clamp(minimum * .8f, 130, 250));
        ResizeHalo(create: false);
        ViewportChanged();
    }

    private void ResizeHalo(bool create)
    {
        if (_haloUnavailable || (!create && _halo is null)) return;
        if (_size.X <= 0 || _size.Y <= 0)
        {
            if (_halo is not null) _halo.Visual.IsVisible = false;
            return;
        }

        try
        {
            _halo ??= new HeroBorderHalo(_stroke);
            _halo.Resize(_size, Math.Min(12, Math.Min(_size.X, _size.Y) / 2));
            if (_halo.Visual.Parent is null) _visual.Children.InsertAtBottom(_halo.Visual);
            _halo.Visual.IsVisible = true;
        }
        catch (Exception error) when (error is COMException or ArgumentException or NotSupportedException)
        {
            _haloUnavailable = true;
            ReleaseHalo();
            AppLog.Information($"Featured border halo is unavailable ({error.HResult:X8}).");
        }
    }

    private void ReleaseHalo()
    {
        if (_halo is not { } halo) return;
        _halo = null;
        if (halo.Visual.Parent is not null) _visual.Children.Remove(halo.Visual);
        halo.Dispose();
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
        _pointerRoot.SizeChanged -= PointerRootSizeChanged;
        _pointerRoot.RemoveHandler(UIElement.PointerEnteredEvent, _pointerMoved);
        _pointerRoot.RemoveHandler(UIElement.PointerMovedEvent, _pointerMoved);
        _pointerRoot.RemoveHandler(UIElement.PointerExitedEvent, _pointerExited);
        _pointerRoot.RemoveHandler(UIElement.PointerCanceledEvent, _pointerCanceled);
        SetState(false, false);
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        ReleaseHalo();
        _visual.Children.RemoveAll();
        _stroke.Shapes.Clear();
        _shape.StrokeBrush = null;
        _brush.ColorStops.Clear();
        foreach (var stop in _stops) stop.Dispose();
        _fade.Dispose();
        _blend?.Dispose();
        _visual.Dispose();
        _stroke.Dispose();
        _shape.Dispose();
        _brush.Dispose();
        _geometry.Dispose();
    }
}
