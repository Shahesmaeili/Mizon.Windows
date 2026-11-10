using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

// Leaves activation, selection, keyboard focus and context menus to the parent list.
public sealed partial class PlaylistItemSurface : Grid
{
    public static readonly DependencyProperty HoverScaleProperty = DependencyProperty.Register(
        nameof(HoverScale), typeof(double), typeof(PlaylistItemSurface), new PropertyMetadata(1.015d, MotionChanged));
    public static readonly DependencyProperty PressedScaleProperty = DependencyProperty.Register(
        nameof(PressedScale), typeof(double), typeof(PlaylistItemSurface), new PropertyMetadata(.985d, MotionChanged));

    private static readonly UISettings Settings = new();
    private InputSystemCursor? _cursor;
    private Vector3KeyFrameAnimation? _motion;
    private CubicBezierEasingFunction? _easing;
    private SelectorItem? _inputHost;
    private readonly PointerEventHandler _releaseHandler, _cancelHandler, _enterHandler, _exitHandler, _moveHandler;
    private bool _attached, _hovered, _pressed;

    public double HoverScale { get => (double)GetValue(HoverScaleProperty); set => SetValue(HoverScaleProperty, value); }
    public double PressedScale { get => (double)GetValue(PressedScaleProperty); set => SetValue(PressedScaleProperty, value); }

    public PlaylistItemSurface()
    {
        _releaseHandler = SurfacePointerReleased;
        _cancelHandler = SurfacePointerCanceled;
        _enterHandler = SurfacePointerEntered;
        _exitHandler = SurfacePointerExited;
        _moveHandler = SurfacePointerMoved;
        Background = new SolidColorBrush(Colors.Transparent);
        Loaded += SurfaceLoaded;
        Unloaded += SurfaceUnloaded;
        DataContextChanged += SurfaceDataContextChanged;
    }

    private static void MotionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PlaylistItemSurface)sender).Animate();

    private void SurfaceLoaded(object sender, RoutedEventArgs args)
    {
        if (_attached || !IsLoaded) return;
        _attached = true;
        _hovered = _pressed = false;
        SizeChanged += SurfaceSizeChanged;
        PointerPressed += SurfacePointerPressed;
        PointerReleased += SurfacePointerReleased;
        PointerCanceled += SurfacePointerCanceled;
        PointerCaptureLost += SurfacePointerCaptureLost;
        ContextRequested += SurfaceContextRequested;
        AttachInputHost();
        if (_inputHost is null)
        {
            PointerEntered += SurfacePointerEntered;
            PointerExited += SurfacePointerExited;
            PointerMoved += SurfacePointerMoved;
        }
        Settings.AnimationsEnabledChanged += PreferenceChanged;
        ProtectedCursor = _cursor ??= InputSystemCursor.Create(InputSystemCursorShape.Hand);
        Center();
        Animate(false);
    }

    private void SurfaceUnloaded(object sender, RoutedEventArgs args)
    {
        // Reparenting can queue an old Unloaded after the element is attached again.
        if (!_attached || IsLoaded) return;
        _attached = false;
        SizeChanged -= SurfaceSizeChanged;
        PointerEntered -= SurfacePointerEntered;
        PointerExited -= SurfacePointerExited;
        PointerMoved -= SurfacePointerMoved;
        PointerPressed -= SurfacePointerPressed;
        PointerReleased -= SurfacePointerReleased;
        PointerCanceled -= SurfacePointerCanceled;
        PointerCaptureLost -= SurfacePointerCaptureLost;
        ContextRequested -= SurfaceContextRequested;
        if (_inputHost is not null)
        {
            _inputHost.RemoveHandler(PointerEnteredEvent, _enterHandler);
            _inputHost.RemoveHandler(PointerExitedEvent, _exitHandler);
            _inputHost.RemoveHandler(PointerMovedEvent, _moveHandler);
            _inputHost.RemoveHandler(PointerReleasedEvent, _releaseHandler);
            _inputHost.RemoveHandler(PointerCaptureLostEvent, _cancelHandler);
            _inputHost.RemoveHandler(PointerCanceledEvent, _cancelHandler);
            _inputHost = null;
        }
        Settings.AnimationsEnabledChanged -= PreferenceChanged;
        ResetMotion();
        _motion?.Dispose();
        _motion = null;
        _easing?.Dispose();
        _easing = null;
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
    }

    private void SurfaceDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => ResetMotion();
    private void SurfaceSizeChanged(object sender, SizeChangedEventArgs args) => Center();
    private void Center() => CenterPoint = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);

    private void AttachInputHost()
    {
        // ListViewItem/GridViewItem can capture the pointer during activation, so
        // observe their release as well without taking capture away from the list.
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not SelectorItem item) continue;
            _inputHost = item;
            item.AddHandler(PointerEnteredEvent, _enterHandler, true);
            item.AddHandler(PointerExitedEvent, _exitHandler, true);
            item.AddHandler(PointerMovedEvent, _moveHandler, true);
            item.AddHandler(PointerReleasedEvent, _releaseHandler, true);
            item.AddHandler(PointerCaptureLostEvent, _cancelHandler, true);
            item.AddHandler(PointerCanceledEvent, _cancelHandler, true);
            break;
        }
    }

    private void SurfacePointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Touch || _hovered) return;
        _hovered = true;
        Animate();
    }

    private void SurfacePointerExited(object sender, PointerRoutedEventArgs args)
    {
        // Always honor exit. The animated child may still geometrically contain
        // the pointer even after the unscaled list container lost hover.
        _hovered = _pressed = false;
        Animate();
    }

    private void SurfacePointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        Animate();
    }

    private void SurfacePointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_pressed) return;
        var hovered = args.Pointer.PointerDeviceType != PointerDeviceType.Touch && ContainsPointer(args);
        if (!_pressed && _hovered == hovered) return;
        _pressed = false;
        _hovered = hovered;
        Animate();
    }

    private void SurfacePointerCaptureLost(object sender, PointerRoutedEventArgs args) => SurfacePointerCanceled(sender, args);
    private void SurfacePointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
        var hovered = ContainsPointer(args);
        if (_hovered == hovered) return;
        _hovered = hovered;
        if (!hovered) _pressed = false;
        Animate();
    }
    private void SurfaceContextRequested(UIElement sender, ContextRequestedEventArgs args) => ResetMotion();
    private void SurfacePointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        _hovered = _pressed = false;
        Animate();
    }

    private bool ContainsPointer(PointerRoutedEventArgs args)
    {
        FrameworkElement host = _inputHost is null ? this : _inputHost;
        var point = args.GetCurrentPoint(host).Position;
        return point.X >= 0 && point.Y >= 0 && point.X < host.ActualWidth && point.Y < host.ActualHeight;
    }

    private void PreferenceChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_attached) Animate(false);
    });

    private void Animate(bool animate = true)
    {
        var requested = !_attached ? 1 : _pressed ? PressedScale : _hovered ? HoverScale : 1;
        var scale = double.IsFinite(requested) ? (float)Math.Clamp(requested, .94, 1.015) : 1;
        var target = new Vector3(scale, scale, 1);
        if (!animate || !_attached || !Settings.AnimationsEnabled)
        {
            if (_motion is not null) StopAnimation(_motion);
            Scale = target;
            return;
        }

        if (_motion is null)
        {
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            _motion = compositor.CreateVector3KeyFrameAnimation();
            _motion.Target = "Scale";
            _motion.Duration = TimeSpan.FromMilliseconds(180);
            _motion.InsertExpressionKeyFrame(0, "this.StartingValue");
            _easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.18f, .8f), Vector2.One);
        }
        _motion.InsertKeyFrame(1, target, _easing);
        // XAML facade animation keeps native hit testing in sync with the surface.
        StartAnimation(_motion);
    }

    private void ResetMotion()
    {
        _hovered = _pressed = false;
        if (_motion is not null) StopAnimation(_motion);
        Scale = Vector3.One;
    }
}
