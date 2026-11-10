using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Numerics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT;

namespace Mizon.Windows.Controls;

// A stationary hit target surrounds one moving surface. Its border travels with
// its content, so hovering cannot reveal a second outline at the original position.
public sealed partial class DiscoveryCardButton : Button
{
    public static readonly DependencyProperty HoverBorderBrushProperty = DependencyProperty.Register(
        nameof(HoverBorderBrush), typeof(Brush), typeof(DiscoveryCardButton), new PropertyMetadata(null, BrushChanged));

    public static readonly DependencyProperty ArtworkElementProperty = DependencyProperty.Register(
        nameof(ArtworkElement), typeof(FrameworkElement), typeof(DiscoveryCardButton), new PropertyMetadata(null, ArtworkChanged));

    public FrameworkElement? ArtworkElement
    {
        get => GetValue(ArtworkElementProperty)?.As<FrameworkElement>();
        set => SetValue(ArtworkElementProperty, value);
    }

    public Brush? HoverBorderBrush
    {
        get => (Brush?)GetValue(HoverBorderBrushProperty);
        set => SetValue(HoverBorderBrushProperty, value);
    }

    private static readonly UISettings Settings = new();
    private Border? _surface;
    private Visual? _visual;
    private FrameworkElement? _artwork;
    private Visual? _artworkVisual;
    private Vector3KeyFrameAnimation? _artworkAnimation;
    private float _lastArtworkScale = float.NaN;
    private Vector3KeyFrameAnimation? _liftAnimation, _scaleAnimation;
    private ScalarKeyFrameAnimation? _opacityAnimation;
    private CubicBezierEasingFunction? _easing;
    private SolidColorBrush? _ownedBorderBrush;
    private Storyboard? _borderStoryboard;
    private ColorAnimation? _borderAnimation;
    private Color? _targetBorderColor;
    private InputSystemCursor? _cursor;
    private XamlRoot? _root;
    private long _enabledToken, _pressedToken, _pointerOverToken, _borderToken, _visibilityToken;
    private bool _attached, _hovered, _pressed;
    private float _lastLift = float.NaN, _lastScale = float.NaN, _lastOpacity = float.NaN;

    public DiscoveryCardButton()
    {
        Loaded += ButtonLoaded;
        Unloaded += ButtonUnloaded;
        DataContextChanged += ContextChanged;
        Click += ButtonClicked;
    }

    protected override void OnApplyTemplate()
    {
        DisconnectArtwork();
        DisconnectSurface();
        base.OnApplyTemplate();
        ConnectSurface();
        ConnectArtwork();
        ResetInteraction();
    }

    private static void BrushChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((DiscoveryCardButton)sender).Render(false, true);

    private static void ArtworkChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var button = (DiscoveryCardButton)sender;
        button.DisconnectArtwork();
        button.ConnectArtwork();
        button.Render(false, true);
    }

    private void ButtonLoaded(object sender, RoutedEventArgs args)
    {
        if (_attached || !IsLoaded) return;
        _attached = true;
        _enabledToken = RegisterPropertyChangedCallback(IsEnabledProperty, EnabledChanged);
        _pressedToken = RegisterPropertyChangedCallback(IsPressedProperty, PressedChanged);
        _pointerOverToken = RegisterPropertyChangedCallback(IsPointerOverProperty, PointerOverChanged);
        _borderToken = RegisterPropertyChangedCallback(BorderBrushProperty, NormalBorderChanged);
        _visibilityToken = RegisterPropertyChangedCallback(VisibilityProperty, VisibilityChanged);
        Settings.AnimationsEnabledChanged += AnimationsChanged;
        _root = XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        ConnectSurface();
        ConnectArtwork();
        UpdateCursor();
        ResetInteraction();
    }

    private void ButtonUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_attached || IsLoaded) return;
        _attached = false;
        UnregisterPropertyChangedCallback(IsEnabledProperty, _enabledToken);
        UnregisterPropertyChangedCallback(IsPressedProperty, _pressedToken);
        UnregisterPropertyChangedCallback(IsPointerOverProperty, _pointerOverToken);
        UnregisterPropertyChangedCallback(BorderBrushProperty, _borderToken);
        UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityToken);
        Settings.AnimationsEnabledChanged -= AnimationsChanged;
        if (_root is not null) _root.Changed -= RootChanged;
        _root = null;
        ResetInteraction();
        DisconnectArtwork();
        DisconnectSurface();
        ReleaseAnimations();
        ProtectedCursor = null;
        _cursor?.Dispose();
        _cursor = null;
    }

    public void ResetInteraction(bool animate = false)
    {
        _hovered = _pressed = false;
        Render(animate, !animate);
    }

    private void ButtonClicked(object sender, RoutedEventArgs args) => ResetInteraction();
    private void ContextChanged(FrameworkElement sender, DataContextChangedEventArgs args) => ResetInteraction();
    private void EnabledChanged(DependencyObject sender, DependencyProperty property) { UpdateCursor(); ResetInteraction(); }
    private void NormalBorderChanged(DependencyObject sender, DependencyProperty property) => Render(false, true);
    private void VisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        if (Visibility != Visibility.Visible) ResetInteraction();
    }
    private void PressedChanged(DependencyObject sender, DependencyProperty property)
    {
        _pressed = IsEnabled && IsPressed;
        Render();
    }
    private void PointerOverChanged(DependencyObject sender, DependencyProperty property)
    {
        if (!IsPointerOver) ResetInteraction(true);
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs args)
    {
        base.OnPointerEntered(args);
        if (!IsEnabled || args.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
        _hovered = true;
        Render();
    }
    protected override void OnPointerExited(PointerRoutedEventArgs args) { base.OnPointerExited(args); ResetInteraction(true); }
    protected override void OnPointerMoved(PointerRoutedEventArgs args)
    {
        base.OnPointerMoved(args);
        if (!IsEnabled || args.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
        var point = args.GetCurrentPoint(this).Position;
        var hovered = point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight;
        if (hovered == _hovered) return;
        _hovered = hovered;
        if (!hovered) _pressed = false;
        Render();
    }
    protected override void OnPointerPressed(PointerRoutedEventArgs args)
    {
        base.OnPointerPressed(args);
        if (!IsEnabled || args.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        Render();
    }
    protected override void OnPointerReleased(PointerRoutedEventArgs args)
    {
        _pressed = false;
        Render();
        // Base can open a popup or start navigation. Click resets before that work.
        base.OnPointerReleased(args);
    }
    protected override void OnPointerCanceled(PointerRoutedEventArgs args) { base.OnPointerCanceled(args); ResetInteraction(true); }
    protected override void OnPointerCaptureLost(PointerRoutedEventArgs args) { base.OnPointerCaptureLost(args); ResetInteraction(true); }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_attached && ReferenceEquals(sender, _root) && !sender.IsHostVisible) ResetInteraction();
    }
    private void AnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (_attached) Render(false, true); });
    private void UpdateCursor() => ProtectedCursor = _attached && IsEnabled
        ? _cursor ??= InputSystemCursor.Create(InputSystemCursorShape.Hand) : null;

    private void ConnectSurface()
    {
        if (_surface is not null) return;
        // NativeAOT can return the template child through its DependencyObject
        // projection. Request the Border interface instead of relying on a cast.
        _surface = GetTemplateChild("CardSurface")?.As<Border>();
        if (_surface is null) return;
        ElementCompositionPreview.SetIsTranslationEnabled(_surface, true);
        _visual = ElementCompositionPreview.GetElementVisual(_surface);
        if (_visual.Properties.TryGetVector3("Translation", out _) != CompositionGetValueStatus.Succeeded)
            _visual.Properties.InsertVector3("Translation", Vector3.Zero);
        _surface.SizeChanged += SurfaceSizeChanged;
        CenterSurface();
        _lastLift = _lastScale = _lastOpacity = float.NaN;
        _targetBorderColor = null;
    }
    private void SurfaceSizeChanged(object sender, SizeChangedEventArgs args) => CenterSurface();
    private void CenterSurface()
    {
        if (_visual is not null && _surface is not null)
            _visual.CenterPoint = new Vector3((float)_surface.ActualWidth / 2, (float)_surface.ActualHeight / 2, 0);
    }

    private void ConnectArtwork()
    {
        if (!_attached || _artwork is not null || ArtworkElement is not { } artwork) return;
        _artwork = artwork;
        _artworkVisual = ElementCompositionPreview.GetElementVisual(artwork);
        _artwork.SizeChanged += ArtworkSizeChanged;
        CenterArtwork();
        _lastArtworkScale = float.NaN;
    }

    private void ArtworkSizeChanged(object sender, SizeChangedEventArgs args) => CenterArtwork();

    private void CenterArtwork()
    {
        if (_artworkVisual is not null && _artwork is not null)
            _artworkVisual.CenterPoint = new Vector3((float)_artwork.ActualWidth / 2, (float)_artwork.ActualHeight / 2, 0);
    }

    private void RenderArtwork(bool animate, bool force, bool motion)
    {
        if (_artworkVisual is null) return;
        var scale = motion && IsEnabled && _hovered ? 1.035f : 1f;
        if (!force && _lastArtworkScale == scale) return;
        _lastArtworkScale = scale;
        if (!animate || !motion)
        {
            _artworkVisual.StopAnimation("Scale");
            _artworkVisual.Scale = new Vector3(scale, scale, 1);
            return;
        }
        EnsureAnimations(_artworkVisual.Compositor);
        if (_artworkAnimation is null)
        {
            _artworkAnimation = _artworkVisual.Compositor.CreateVector3KeyFrameAnimation();
            _artworkAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
            _artworkAnimation.Duration = TimeSpan.FromMilliseconds(330);
        }
        _artworkAnimation.InsertKeyFrame(1, new Vector3(scale, scale, 1), _easing);
        _artworkVisual.StartAnimation("Scale", _artworkAnimation);
    }

    private void DisconnectArtwork()
    {
        if (_artwork is not null) _artwork.SizeChanged -= ArtworkSizeChanged;
        if (_artworkVisual is not null)
        {
            _artworkVisual.StopAnimation("Scale");
            _artworkVisual.Scale = Vector3.One;
        }
        _artworkAnimation?.Dispose();
        _artworkAnimation = null;
        _artworkVisual = null;
        _artwork = null;
        _lastArtworkScale = float.NaN;
    }

    private void Render(bool animate = true, bool force = false)
    {
        if (_visual is null || _surface is null) return;
        var motion = _attached && Settings.AnimationsEnabled;
        var lift = motion && IsEnabled && _hovered && !_pressed ? -4f : 0f;
        var scale = motion && IsEnabled && _pressed ? .98f : 1f;
        var opacity = IsEnabled ? 1f : .4f;
        RenderArtwork(animate, force, motion);
        UpdateBorder(IsEnabled && (_hovered || _pressed) ? HoverBorderBrush ?? BorderBrush : BorderBrush, animate && motion, force);
        if (!force && lift == _lastLift && scale == _lastScale && opacity == _lastOpacity) return;
        _lastLift = lift; _lastScale = scale; _lastOpacity = opacity;
        if (!animate || !motion)
        {
            StopMotion();
            _visual.Properties.InsertVector3("Translation", new Vector3(0, lift, 0));
            _visual.Scale = new Vector3(scale, scale, 1);
            _visual.Opacity = opacity;
            return;
        }
        EnsureAnimations(_visual.Compositor);
        _liftAnimation!.InsertKeyFrame(1, new Vector3(0, lift, 0), _easing);
        _scaleAnimation!.InsertKeyFrame(1, new Vector3(scale, scale, 1), _easing);
        _opacityAnimation!.InsertKeyFrame(1, opacity, _easing);
        _visual.Properties.StartAnimation("Translation", _liftAnimation);
        _visual.StartAnimation("Scale", _scaleAnimation);
        _visual.StartAnimation("Opacity", _opacityAnimation);
    }

    private void UpdateBorder(Brush? brush, bool animate, bool force)
    {
        if (_surface is null) return;
        if (brush is not SolidColorBrush solid)
        {
            _borderStoryboard?.Stop();
            _targetBorderColor = null;
            _surface.BorderBrush = brush;
            return;
        }
        var color = solid.Color;
        // Match the previous hover's half-strength accent without drawing an
        // additional outline. Pressed keeps the full accent on this same border.
        if (IsEnabled && _hovered && !_pressed && ReferenceEquals(brush, HoverBorderBrush))
            color.A = (byte)Math.Round(color.A * .5);
        if (!force && _targetBorderColor == color && ReferenceEquals(_surface.BorderBrush, _ownedBorderBrush)) return;
        _targetBorderColor = color;
        _ownedBorderBrush ??= new SolidColorBrush((BorderBrush as SolidColorBrush)?.Color ?? solid.Color);
        var current = _ownedBorderBrush.Color;
        _borderStoryboard?.Stop();
        _ownedBorderBrush.Color = current;
        _surface.BorderBrush = _ownedBorderBrush;
        if (!animate) { _ownedBorderBrush.Color = color; return; }
        if (_borderStoryboard is null)
        {
            _borderAnimation = new ColorAnimation { Duration = TimeSpan.FromMilliseconds(180), EnableDependentAnimation = true };
            Storyboard.SetTarget(_borderAnimation, _ownedBorderBrush);
            Storyboard.SetTargetProperty(_borderAnimation, nameof(SolidColorBrush.Color));
            _borderStoryboard = new Storyboard();
            _borderStoryboard.Children.Add(_borderAnimation);
        }
        _borderAnimation!.From = current;
        _borderAnimation.To = color;
        _borderStoryboard.Begin();
    }

    private void EnsureAnimations(Compositor compositor)
    {
        if (_liftAnimation is not null) return;
        _easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
        _liftAnimation = compositor.CreateVector3KeyFrameAnimation();
        _scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        _opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        _liftAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
        _scaleAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
        _opacityAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
        _liftAnimation.Duration = TimeSpan.FromMilliseconds(240);
        _scaleAnimation.Duration = _opacityAnimation.Duration = TimeSpan.FromMilliseconds(160);
    }
    private void StopMotion()
    {
        _visual?.Properties.StopAnimation("Translation");
        _visual?.StopAnimation("Scale");
        _visual?.StopAnimation("Opacity");
    }
    private void DisconnectSurface()
    {
        StopMotion();
        _borderStoryboard?.Stop();
        _borderStoryboard?.Children.Clear();
        _borderStoryboard = null;
        _borderAnimation = null;
        _ownedBorderBrush = null;
        _targetBorderColor = null;
        if (_visual is not null)
        {
            _visual.Properties.InsertVector3("Translation", Vector3.Zero);
            _visual.Scale = Vector3.One;
            _visual.Opacity = 1;
        }
        if (_surface is not null)
        {
            _surface.SizeChanged -= SurfaceSizeChanged;
            _surface.BorderBrush = BorderBrush;
        }
        _surface = null;
        _visual = null;
    }
    private void ReleaseAnimations()
    {
        _liftAnimation?.Dispose(); _scaleAnimation?.Dispose(); _opacityAnimation?.Dispose(); _easing?.Dispose();
        _liftAnimation = _scaleAnimation = null;
        _opacityAnimation = null;
        _easing = null;
    }
}
