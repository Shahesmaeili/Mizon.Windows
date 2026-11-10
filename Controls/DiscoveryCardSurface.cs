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

// A stationary group owns the motion of one surface. Its child buttons remain
// independent keyboard and pointer targets (music, contributor, and so on).
public sealed partial class DiscoveryCardSurface : ContentControl
{
    public static readonly DependencyProperty HoverBorderBrushProperty = DependencyProperty.Register(
        nameof(HoverBorderBrush), typeof(Brush), typeof(DiscoveryCardSurface), new PropertyMetadata(null, BorderChanged));
    public static readonly DependencyProperty ArtworkElementProperty = DependencyProperty.Register(
        nameof(ArtworkElement), typeof(FrameworkElement), typeof(DiscoveryCardSurface), new PropertyMetadata(null, ArtworkChanged));

    public Brush? HoverBorderBrush { get => (Brush?)GetValue(HoverBorderBrushProperty); set => SetValue(HoverBorderBrushProperty, value); }
    public FrameworkElement? ArtworkElement { get => GetValue(ArtworkElementProperty)?.As<FrameworkElement>(); set => SetValue(ArtworkElementProperty, value); }

    private static readonly UISettings Settings = new();
    private readonly PointerEventHandler _hoverHandler, _exitHandler, _pressHandler, _releaseHandler, _cancelHandler;
    private Border? _surface;
    private FrameworkElement? _artwork;
    private Visual? _visual, _artworkVisual;
    private XamlRoot? _root;
    private Vector3KeyFrameAnimation? _lift, _scale, _zoom;
    private CubicBezierEasingFunction? _easing;
    private SolidColorBrush? _outline;
    private Storyboard? _outlineStoryboard;
    private ColorAnimation? _outlineAnimation;
    private Color? _outlineColor;
    private long _enabledToken, _visibilityToken, _borderToken;
    private bool _attached, _hovered, _pressed;
    private float _lastLift = float.NaN, _lastScale = float.NaN, _lastZoom = float.NaN;

    public DiscoveryCardSurface()
    {
        IsTabStop = false;
        _hoverHandler = PointerChanged;
        _exitHandler = ExitObserved;
        _pressHandler = PressObserved;
        _releaseHandler = ReleaseObserved;
        _cancelHandler = CancelObserved;
        Loaded += SurfaceLoaded;
        Unloaded += SurfaceUnloaded;
        DataContextChanged += (_, _) => ResetInteraction();
    }

    protected override void OnApplyTemplate()
    {
        DisconnectVisuals();
        base.OnApplyTemplate();
        ConnectVisuals();
        ResetInteraction();
    }

    private static void BorderChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DiscoveryCardSurface)sender).Render(false);
    private static void ArtworkChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (DiscoveryCardSurface)sender;
        card.DisconnectArtwork(); card.ConnectArtwork(); card.Render(false);
    }

    private void SurfaceLoaded(object sender, RoutedEventArgs args)
    {
        if (_attached || !IsLoaded) return;
        _attached = true;
        AddHandler(PointerEnteredEvent, _hoverHandler, true); AddHandler(PointerMovedEvent, _hoverHandler, true); AddHandler(PointerExitedEvent, _exitHandler, true);
        AddHandler(PointerPressedEvent, _pressHandler, true); AddHandler(PointerReleasedEvent, _releaseHandler, true);
        AddHandler(PointerCanceledEvent, _cancelHandler, true); AddHandler(PointerCaptureLostEvent, _cancelHandler, true);
        _enabledToken = RegisterPropertyChangedCallback(IsEnabledProperty, ResetProperty);
        _visibilityToken = RegisterPropertyChangedCallback(VisibilityProperty, ResetProperty);
        _borderToken = RegisterPropertyChangedCallback(BorderBrushProperty, ResetProperty);
        Settings.AnimationsEnabledChanged += AnimationPreferenceChanged;
        _root = XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        ConnectVisuals(); ResetInteraction();
    }

    private void SurfaceUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_attached || IsLoaded) return;
        _attached = false;
        RemoveHandler(PointerEnteredEvent, _hoverHandler); RemoveHandler(PointerMovedEvent, _hoverHandler); RemoveHandler(PointerExitedEvent, _exitHandler);
        RemoveHandler(PointerPressedEvent, _pressHandler); RemoveHandler(PointerReleasedEvent, _releaseHandler);
        RemoveHandler(PointerCanceledEvent, _cancelHandler); RemoveHandler(PointerCaptureLostEvent, _cancelHandler);
        UnregisterPropertyChangedCallback(IsEnabledProperty, _enabledToken);
        UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityToken);
        UnregisterPropertyChangedCallback(BorderBrushProperty, _borderToken);
        Settings.AnimationsEnabledChanged -= AnimationPreferenceChanged;
        if (_root is not null) _root.Changed -= RootChanged;
        _root = null;
        ResetInteraction(); DisconnectVisuals();
        _lift?.Dispose(); _scale?.Dispose(); _zoom?.Dispose(); _easing?.Dispose();
        _lift = _scale = _zoom = null; _easing = null;
    }

    public void ResetInteraction(bool animate = false) { _hovered = _pressed = false; Render(animate); }
    private void ResetProperty(DependencyObject sender, DependencyProperty property) => ResetInteraction();
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) { if (!sender.IsHostVisible) ResetInteraction(); }
    private void AnimationPreferenceChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() => { if (_attached) Render(false); });

    private void PointerChanged(object sender, PointerRoutedEventArgs args)
    {
        if (!IsEnabled) return;
        var point = args.GetCurrentPoint(this).Position;
        var inside = point.X >= 0 && point.Y >= 0 && point.X < ActualWidth && point.Y < ActualHeight;
        // Child-to-child exits are not exits from the stationary card.
        var over = inside && args.Pointer.PointerDeviceType != PointerDeviceType.Touch;
        if (_hovered == over) return;
        _hovered = over;
        if (!over) _pressed = false;
        Render(true);
    }
    private void PressObserved(object sender, PointerRoutedEventArgs args)
    {
        if (!IsEnabled) return;
        var point = args.GetCurrentPoint(this);
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed) return;
        _pressed = true; Render(true);
    }
    private void ExitObserved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this).Position;
        if (point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight) ResetInteraction(true);
        // An exit caused by opening a popup must never turn hover back on after
        // the owner has explicitly reset the card before showing that popup.
    }
    private void ReleaseObserved(object sender, PointerRoutedEventArgs args) { _pressed = false; Render(true); }
    private void CancelObserved(object sender, PointerRoutedEventArgs args) => ResetInteraction(true);

    private void ConnectVisuals()
    {
        if (_surface is null)
        {
            _surface = GetTemplateChild("CardSurface")?.As<Border>();
            if (_surface is not null)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(_surface, true);
                _visual = ElementCompositionPreview.GetElementVisual(_surface);
                _visual.Properties.InsertVector3("Translation", Vector3.Zero);
                _surface.SizeChanged += VisualSizeChanged;
            }
        }
        ConnectArtwork(); CenterVisuals();
    }
    private void ConnectArtwork()
    {
        if (!_attached || _artwork is not null || ArtworkElement is not { } artwork) return;
        _artwork = artwork; _artworkVisual = ElementCompositionPreview.GetElementVisual(artwork);
        artwork.SizeChanged += VisualSizeChanged; _lastZoom = float.NaN; CenterVisuals();
    }
    private void VisualSizeChanged(object sender, SizeChangedEventArgs args) => CenterVisuals();
    private void CenterVisuals()
    {
        if (_visual is not null && _surface is not null) _visual.CenterPoint = new((float)_surface.ActualWidth / 2, (float)_surface.ActualHeight / 2, 0);
        if (_artworkVisual is not null && _artwork is not null) _artworkVisual.CenterPoint = new((float)_artwork.ActualWidth / 2, (float)_artwork.ActualHeight / 2, 0);
    }

    private void Render(bool animate)
    {
        if (_visual is null || _surface is null) return;
        var motion = _attached && Settings.AnimationsEnabled && IsEnabled;
        var lift = motion && _hovered && !_pressed ? -4f : 0f;
        var scale = motion && _pressed ? .98f : 1f;
        var zoom = motion && _hovered ? 1.035f : 1f;
        UpdateOutline(animate && motion);
        if (animate && motion && _lift is null)
        {
            var compositor = _visual.Compositor;
            _easing = compositor.CreateCubicBezierEasingFunction(new(.2f, .8f), Vector2.One);
            _lift = compositor.CreateVector3KeyFrameAnimation(); _scale = compositor.CreateVector3KeyFrameAnimation(); _zoom = compositor.CreateVector3KeyFrameAnimation();
            foreach (var animation in new[] { _lift, _scale, _zoom }) animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            _lift.Duration = TimeSpan.FromMilliseconds(240); _scale.Duration = TimeSpan.FromMilliseconds(160); _zoom.Duration = TimeSpan.FromMilliseconds(330);
        }
        if (!animate || !motion)
        {
            _visual.Properties.StopAnimation("Translation"); _visual.StopAnimation("Scale"); _artworkVisual?.StopAnimation("Scale");
            _visual.Properties.InsertVector3("Translation", new(0, lift, 0)); _visual.Scale = new(scale, scale, 1);
            if (_artworkVisual is not null) _artworkVisual.Scale = new(zoom, zoom, 1);
        }
        else
        {
            if (_lastLift != lift) { _lift!.InsertKeyFrame(1, new(0, lift, 0), _easing); _visual.Properties.StartAnimation("Translation", _lift); }
            if (_lastScale != scale) { _scale!.InsertKeyFrame(1, new(scale, scale, 1), _easing); _visual.StartAnimation("Scale", _scale); }
            if (_lastZoom != zoom && _artworkVisual is not null) { _zoom!.InsertKeyFrame(1, new(zoom, zoom, 1), _easing); _artworkVisual.StartAnimation("Scale", _zoom); }
        }
        _lastLift = lift; _lastScale = scale; _lastZoom = zoom;
    }

    private void UpdateOutline(bool animate)
    {
        if (_surface is null) return;
        var brush = IsEnabled && (_hovered || _pressed) ? HoverBorderBrush ?? BorderBrush : BorderBrush;
        if (brush is not SolidColorBrush solid) { _outlineStoryboard?.Stop(); _surface.BorderBrush = brush; _outlineColor = null; return; }
        var color = solid.Color;
        if (_hovered && !_pressed && ReferenceEquals(brush, HoverBorderBrush)) color.A = (byte)Math.Round(color.A * .5);
        if (_outlineColor == color && ReferenceEquals(_surface.BorderBrush, _outline))
        {
            if (!animate) { _outlineStoryboard?.Stop(); _outline!.Color = color; }
            return;
        }
        _outline ??= new SolidColorBrush((BorderBrush as SolidColorBrush)?.Color ?? color);
        var previous = _outline.Color; _outlineStoryboard?.Stop(); _outline.Color = previous;
        _surface.BorderBrush = _outline; _outlineColor = color;
        if (!animate) { _outline.Color = color; return; }
        if (_outlineStoryboard is null)
        {
            _outlineAnimation = new ColorAnimation { Duration = TimeSpan.FromMilliseconds(180), EnableDependentAnimation = true };
            Storyboard.SetTarget(_outlineAnimation, _outline); Storyboard.SetTargetProperty(_outlineAnimation, nameof(SolidColorBrush.Color));
            _outlineStoryboard = new Storyboard(); _outlineStoryboard.Children.Add(_outlineAnimation);
        }
        _outlineAnimation!.From = previous; _outlineAnimation.To = color; _outlineStoryboard.Begin();
    }

    private void DisconnectArtwork()
    {
        if (_artwork is not null) _artwork.SizeChanged -= VisualSizeChanged;
        if (_artworkVisual is not null) { _artworkVisual.StopAnimation("Scale"); _artworkVisual.Scale = Vector3.One; }
        _artwork = null; _artworkVisual = null; _lastZoom = float.NaN;
    }
    private void DisconnectVisuals()
    {
        DisconnectArtwork();
        if (_visual is not null) { _visual.Properties.StopAnimation("Translation"); _visual.StopAnimation("Scale"); _visual.Properties.InsertVector3("Translation", Vector3.Zero); _visual.Scale = Vector3.One; }
        _outlineStoryboard?.Stop(); _outlineStoryboard?.Children.Clear();
        if (_surface is not null) { _surface.SizeChanged -= VisualSizeChanged; _surface.BorderBrush = BorderBrush; }
        _outlineStoryboard = null; _outlineAnimation = null; _outline = null; _outlineColor = null;
        _surface = null; _visual = null; _lastLift = _lastScale = _lastZoom = float.NaN;
    }
}
