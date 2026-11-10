using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

// The template's stationary hit target owns input; only its decorative content moves.
public sealed partial class StoryActionButton : Button
{
    public static readonly DependencyProperty RestingOpacityProperty = DependencyProperty.Register(
        nameof(RestingOpacity), typeof(double), typeof(StoryActionButton), new PropertyMetadata(1d, RestingOpacityChanged));

    public double RestingOpacity
    {
        get => (double)GetValue(RestingOpacityProperty);
        set => SetValue(RestingOpacityProperty, value);
    }

    private static readonly UISettings Settings = new();
    private FrameworkElement? _surface, _artwork;
    private Visual? _surfaceVisual, _artworkVisual;
    private Vector3KeyFrameAnimation? _scaleAnimation, _artworkAnimation;
    private ScalarKeyFrameAnimation? _opacityAnimation;
    private CubicBezierEasingFunction? _easing;
    private InputSystemCursor? _cursor;
    private XamlRoot? _root;
    private long _enabledToken, _pressedToken, _contentToken, _pointerOverToken;
    private bool _attached, _hovered, _pressed;
    private float _lastScale = float.NaN, _lastArtworkScale = float.NaN, _lastOpacity = float.NaN;

    public StoryActionButton()
    {
        Loaded += ButtonLoaded;
        Unloaded += ButtonUnloaded;
        DataContextChanged += ButtonDataContextChanged;
        // Subscribed before a page's Click handler: reset before a viewer takes pointer focus.
        Click += ButtonClicked;
    }

    protected override void OnApplyTemplate()
    {
        DisconnectVisuals();
        base.OnApplyTemplate();
        ConnectVisuals();
        ResetInteraction();
    }

    private static void RestingOpacityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((StoryActionButton)sender).Render(false, true);

    private void ButtonLoaded(object sender, RoutedEventArgs args)
    {
        if (_attached || !IsLoaded) return;
        _attached = true;
        _enabledToken = RegisterPropertyChangedCallback(IsEnabledProperty, EnabledChanged);
        _pressedToken = RegisterPropertyChangedCallback(IsPressedProperty, PressedChanged);
        _contentToken = RegisterPropertyChangedCallback(ContentProperty, ContentChanged);
        _pointerOverToken = RegisterPropertyChangedCallback(IsPointerOverProperty, PointerOverChanged);
        Settings.AnimationsEnabledChanged += AnimationsChanged;
        _root = XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        ConnectVisuals();
        UpdateCursor();
        ResetInteraction();
    }

    private void ButtonUnloaded(object sender, RoutedEventArgs args)
    {
        // A cached page can reparent and reload before its old Unloaded is delivered.
        if (!_attached || IsLoaded) return;
        _attached = false;
        UnregisterPropertyChangedCallback(IsEnabledProperty, _enabledToken);
        UnregisterPropertyChangedCallback(IsPressedProperty, _pressedToken);
        UnregisterPropertyChangedCallback(ContentProperty, _contentToken);
        UnregisterPropertyChangedCallback(IsPointerOverProperty, _pointerOverToken);
        Settings.AnimationsEnabledChanged -= AnimationsChanged;
        if (_root is not null) _root.Changed -= RootChanged;
        _root = null;
        ResetInteraction();
        DisconnectVisuals();
        ReleaseAnimations();
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
    }

    public void ResetInteraction(bool animate = false)
    {
        _hovered = _pressed = false;
        // Scroll notifications can arrive every frame. A reset already in flight
        // must finish instead of restarting its animation on every notification.
        Render(animate, !animate);
    }

    private void ButtonClicked(object sender, RoutedEventArgs args) => ResetInteraction();
    private void ButtonDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => ResetInteraction();

    private void EnabledChanged(DependencyObject sender, DependencyProperty property)
    {
        UpdateCursor();
        ResetInteraction();
    }

    private void PressedChanged(DependencyObject sender, DependencyProperty property)
    {
        // The native Button also supplies keyboard/gamepad/automation press behavior.
        _pressed = IsEnabled && IsPressed;
        Render();
    }

    private void PointerOverChanged(DependencyObject sender, DependencyProperty property)
    {
        // This is a fallback for capture/visibility transitions, never the sole exit signal.
        if (!IsPointerOver) ResetInteraction(true);
    }

    private void ContentChanged(DependencyObject sender, DependencyProperty property)
    {
        DisconnectVisuals();
        ConnectVisuals();
        ResetInteraction();
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs args)
    {
        base.OnPointerEntered(args);
        if (!IsEnabled || args.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
        _hovered = true;
        Render();
    }

    protected override void OnPointerExited(PointerRoutedEventArgs args)
    {
        base.OnPointerExited(args);
        ResetInteraction(true);
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs args)
    {
        base.OnPointerMoved(args);
        if (!IsEnabled || args.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
        var point = args.GetCurrentPoint(this).Position;
        var hovered = point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight;
        if (_hovered == hovered) return;
        _hovered = hovered;
        if (!hovered) _pressed = false;
        Render();
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs args)
    {
        base.OnPointerPressed(args);
        if (!IsEnabled || args.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        Render();
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs args)
    {
        // Base may raise Click and open the viewer. Do not reapply hover after that call.
        _pressed = false;
        Render();
        base.OnPointerReleased(args);
    }

    protected override void OnPointerCanceled(PointerRoutedEventArgs args)
    {
        base.OnPointerCanceled(args);
        ResetInteraction(true);
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs args)
    {
        base.OnPointerCaptureLost(args);
        ResetInteraction(true);
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_attached && ReferenceEquals(sender, _root) && !sender.IsHostVisible) ResetInteraction();
    }

    private void AnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_attached) Render(false, true);
    });

    private void UpdateCursor()
    {
        if (_attached && IsEnabled)
            ProtectedCursor = _cursor ??= InputSystemCursor.Create(InputSystemCursorShape.Hand);
        else ProtectedCursor = null;
    }

    private void ConnectVisuals()
    {
        if (_surface is not null) return;
        _surface = GetTemplateChild("StoryContent") as FrameworkElement;
        _artwork = (Content as FrameworkElement)?.FindName("CardArtwork") as FrameworkElement;
        if (_surface is not null)
        {
            _surfaceVisual = ElementCompositionPreview.GetElementVisual(_surface);
            _surface.SizeChanged += ContentSizeChanged;
        }
        if (_artwork is not null)
        {
            _artworkVisual = ElementCompositionPreview.GetElementVisual(_artwork);
            _artwork.SizeChanged += ContentSizeChanged;
        }
        CenterVisuals();
        _lastScale = _lastArtworkScale = _lastOpacity = float.NaN;
    }

    private void ContentSizeChanged(object sender, SizeChangedEventArgs args) => CenterVisuals();

    private void CenterVisuals()
    {
        if (_surface is not null && _surfaceVisual is not null)
            _surfaceVisual.CenterPoint = new Vector3((float)_surface.ActualWidth / 2, (float)_surface.ActualHeight / 2, 0);
        if (_artwork is not null && _artworkVisual is not null)
            _artworkVisual.CenterPoint = new Vector3((float)_artwork.ActualWidth / 2, (float)_artwork.ActualHeight / 2, 0);
    }

    private void Render(bool animate = true, bool force = false)
    {
        if (_surfaceVisual is null) return;
        var motion = _attached && Settings.AnimationsEnabled;
        var resting = double.IsFinite(RestingOpacity) ? (float)Math.Clamp(RestingOpacity, 0, 1) : 1f;
        var scale = motion && IsEnabled && _pressed ? .98f : 1f;
        var artworkScale = motion && IsEnabled && _hovered && !_pressed ? 1.035f : 1f;
        var opacity = !IsEnabled ? .4f : _pressed && resting < 1 ? .7f : _hovered ? 1f : resting;
        if (!force && scale == _lastScale && artworkScale == _lastArtworkScale && opacity == _lastOpacity) return;
        _lastScale = scale;
        _lastArtworkScale = artworkScale;
        _lastOpacity = opacity;
        if (!animate || !motion)
        {
            StopAnimations();
            _surfaceVisual.Scale = new Vector3(scale, scale, 1);
            _surfaceVisual.Opacity = opacity;
            if (_artworkVisual is not null) _artworkVisual.Scale = new Vector3(artworkScale, artworkScale, 1);
            return;
        }
        EnsureAnimations(_surfaceVisual.Compositor);
        _scaleAnimation!.InsertKeyFrame(1, new Vector3(scale, scale, 1), _easing);
        _opacityAnimation!.InsertKeyFrame(1, opacity, _easing);
        _surfaceVisual.StartAnimation("Scale", _scaleAnimation);
        _surfaceVisual.StartAnimation("Opacity", _opacityAnimation);
        if (_artworkVisual is not null)
        {
            _artworkAnimation!.InsertKeyFrame(1, new Vector3(artworkScale, artworkScale, 1), _easing);
            _artworkVisual.StartAnimation("Scale", _artworkAnimation);
        }
    }

    private void EnsureAnimations(Compositor compositor)
    {
        if (_scaleAnimation is not null) return;
        _easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
        _scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        _artworkAnimation = compositor.CreateVector3KeyFrameAnimation();
        _opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        _scaleAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
        _artworkAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
        _opacityAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
        _scaleAnimation.Duration = _opacityAnimation.Duration = TimeSpan.FromMilliseconds(160);
        _artworkAnimation.Duration = TimeSpan.FromMilliseconds(220);
    }

    private void StopAnimations()
    {
        _surfaceVisual?.StopAnimation("Scale");
        _surfaceVisual?.StopAnimation("Opacity");
        _artworkVisual?.StopAnimation("Scale");
    }

    private void DisconnectVisuals()
    {
        StopAnimations();
        if (_surfaceVisual is not null)
        {
            _surfaceVisual.Scale = Vector3.One;
            _surfaceVisual.Opacity = 1;
        }
        if (_artworkVisual is not null) _artworkVisual.Scale = Vector3.One;
        if (_surface is not null) _surface.SizeChanged -= ContentSizeChanged;
        if (_artwork is not null) _artwork.SizeChanged -= ContentSizeChanged;
        _surface = _artwork = null;
        // These visuals belong to XAML. Only release our references, never dispose them.
        _surfaceVisual = _artworkVisual = null;
    }

    private void ReleaseAnimations()
    {
        _scaleAnimation?.Dispose();
        _artworkAnimation?.Dispose();
        _opacityAnimation?.Dispose();
        _easing?.Dispose();
        _scaleAnimation = _artworkAnimation = null;
        _opacityAnimation = null;
        _easing = null;
    }
}
