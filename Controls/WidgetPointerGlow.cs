using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

/// <summary>A decorative, cursor-centred light on the existing card corners.</summary>
internal sealed class WidgetPointerGlow : IDisposable
{
    private const float Proximity = 76;
    private static readonly UISettings Settings = new();
    private static readonly AccessibilitySettings Accessibility = new();

    private readonly FrameworkElement _root;
    private readonly Entry[] _entries;
    private readonly PointerEventHandler _moved;
    private readonly PointerEventHandler _exited;
    private Point _pointer;
    private bool _hasPointer;
    private bool _queued;
    private bool _disposed;

    internal WidgetPointerGlow(FrameworkElement root, params (WidgetLoadHost Host, Color Color)[] widgets)
    {
        _root = root;
        _entries = Array.ConvertAll(widgets, widget => new Entry(widget.Host, widget.Color));
        _moved = PointerMoved;
        _exited = PointerExited;
        root.AddHandler(UIElement.PointerMovedEvent, _moved, handledEventsToo: true);
        root.AddHandler(UIElement.PointerEnteredEvent, _moved, handledEventsToo: true);
        root.AddHandler(UIElement.PointerExitedEvent, _exited, handledEventsToo: true);
        root.SizeChanged += RootSizeChanged;
    }

    internal void ViewportChanged()
    {
        if (_disposed || !_hasPointer || _queued) return;
        _queued = _root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Update);
    }

    private void RootSizeChanged(object sender, SizeChangedEventArgs args) => ViewportChanged();

    private void PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        _pointer = args.GetCurrentPoint(_root).Position;
        _hasPointer = true;
        ViewportChanged();
    }

    private void PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        var point = args.GetCurrentPoint(_root).Position;
        if (!ReferenceEquals(args.OriginalSource, _root) && point.X >= 0 && point.Y >= 0
            && point.X < _root.ActualWidth && point.Y < _root.ActualHeight) return;
        _hasPointer = false;
        Update();
    }

    private void Update()
    {
        _queued = false;
        if (_disposed) return;
        var animate = Settings.AnimationsEnabled;
        var show = _hasPointer && !Accessibility.HighContrast;
        foreach (var entry in _entries)
        {
            var host = entry.Host;
            if (!show || !host.CanShowPointerGlow || host.XamlRoot is null || !ReferenceEquals(host.XamlRoot, _root.XamlRoot))
            {
                entry.Light?.SetOpacity(0, animate && host.CanShowPointerGlow);
                continue;
            }

            var point = _root.TransformToVisual(host).TransformPoint(_pointer);
            var width = (float)host.ActualWidth;
            var height = (float)host.ActualHeight;
            var x = (float)point.X;
            var y = (float)point.Y;
            var dx = Math.Max(0, Math.Max(-x, x - width));
            var dy = Math.Max(0, Math.Max(-y, y - height));
            var strength = Math.Clamp(1 - MathF.Sqrt(dx * dx + dy * dy) / Proximity, 0, 1);
            if (strength <= 0)
            {
                entry.Light?.SetOpacity(0, animate);
                continue;
            }

            var light = entry.Light ??= new Light(host, entry.Color);
            light.Show(new(x, y), new(width, height), strength * strength * (3 - 2 * strength), animate);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.RemoveHandler(UIElement.PointerMovedEvent, _moved);
        _root.RemoveHandler(UIElement.PointerEnteredEvent, _moved);
        _root.RemoveHandler(UIElement.PointerExitedEvent, _exited);
        _root.SizeChanged -= RootSizeChanged;
        foreach (var entry in _entries) entry.Light?.Dispose();
    }

    private sealed class Entry(WidgetLoadHost host, Color color)
    {
        internal WidgetLoadHost Host { get; } = host;
        internal Color Color { get; } = color;
        internal Light? Light;
    }

    private sealed class Light : IDisposable
    {
        private const float Stroke = 0.5f;
        private const float Radius = 16;
        private readonly FrameworkElement _host;
        private readonly ShapeVisual _visual;
        private readonly CompositionRoundedRectangleGeometry _geometry;
        private readonly CompositionSpriteShape _stroke;
        private readonly CompositionRadialGradientBrush _brush;
        private readonly CompositionColorGradientStop[] _stops;
        private readonly ScalarKeyFrameAnimation _fade;
        private readonly CubicBezierEasingFunction _easing;
        private Vector2 _point = new(float.NaN);
        private Vector2 _size;
        private float _opacity;

        internal Light(FrameworkElement host, Color color)
        {
            _host = host;
            var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
            _geometry = compositor.CreateRoundedRectangleGeometry();
            _geometry.Offset = new Vector2(Stroke / 2);
            _brush = compositor.CreateRadialGradientBrush();
            _brush.MappingMode = CompositionMappingMode.Absolute;
            _brush.EllipseRadius = new Vector2(175);
            _stops =
            [
                compositor.CreateColorGradientStop(0, color),
                compositor.CreateColorGradientStop(.55f, Color.FromArgb(154, color.R, color.G, color.B)),
                compositor.CreateColorGradientStop(1, Color.FromArgb(0, color.R, color.G, color.B))
            ];
            foreach (var stop in _stops) _brush.ColorStops.Add(stop);
            _stroke = compositor.CreateSpriteShape(_geometry);
            _stroke.StrokeThickness = Stroke;
            _stroke.StrokeBrush = _brush;
            _visual = compositor.CreateShapeVisual();
            _visual.RelativeSizeAdjustment = Vector2.One;
            _visual.Opacity = 0;
            _visual.Shapes.Add(_stroke);
            _easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .75f), Vector2.One);
            _fade = compositor.CreateScalarKeyFrameAnimation();
            _fade.Duration = TimeSpan.FromMilliseconds(140);
            ElementCompositionPreview.SetElementChildVisual(host, _visual);
        }

        internal void Show(Vector2 point, Vector2 size, float opacity, bool animate)
        {
            if (_size != size)
            {
                _size = size;
                _geometry.Size = Vector2.Max(Vector2.Zero, size - new Vector2(Stroke));
                _geometry.CornerRadius = new Vector2(Math.Max(0, Math.Min(Radius, Math.Min(size.X, size.Y) / 2) - Stroke / 2));
            }
            if (_point != point)
            {
                _point = point;
                _brush.EllipseCenter = point;
            }
            SetOpacity(opacity, animate);
        }

        internal void SetOpacity(float opacity, bool animate)
        {
            if (_opacity == opacity || (opacity is > 0 and < 1 && Math.Abs(_opacity - opacity) < .02f)) return;
            _opacity = opacity;
            if (animate)
            {
                _fade.InsertKeyFrame(1, opacity, _easing);
                _visual.StartAnimation(nameof(Visual.Opacity), _fade);
            }
            else
            {
                _visual.StopAnimation(nameof(Visual.Opacity));
                _visual.Opacity = opacity;
            }
        }

        public void Dispose()
        {
            ElementCompositionPreview.SetElementChildVisual(_host, null);
            _visual.StopAnimation(nameof(Visual.Opacity));
            _visual.Shapes.Clear();
            _stroke.StrokeBrush = null;
            _brush.ColorStops.Clear();
            foreach (var stop in _stops) stop.Dispose();
            _fade.Dispose();
            _easing.Dispose();
            _visual.Dispose();
            _stroke.Dispose();
            _brush.Dispose();
            _geometry.Dispose();
        }
    }
}
