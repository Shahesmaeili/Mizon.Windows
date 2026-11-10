using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Numerics;

namespace Mizon.Windows.Common;

/// <summary>StoreCarousel lighting with pointer and composition resources owned by one hero.</summary>
internal sealed partial class DiscoveryHeroLighting : IDisposable
{
    private readonly FrameworkElement _root;
    private readonly global::Windows.UI.ViewManagement.UISettings _accessibilitySettings = new();
    private readonly HeroAmbientLight _ambientLight = new();
    private readonly HeroHoverLight _hoverLight = new();
    private readonly PointerEventHandler _pointerMovedHandler;
    private readonly PointerEventHandler _pointerExitedHandler;
    private readonly PointerEventHandler _pointerReleasedHandler;
    private readonly PointerEventHandler _pointerCanceledHandler;
    private bool _disposed;

    public DiscoveryHeroLighting(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        _pointerMovedHandler = PointerMoved;
        _pointerExitedHandler = PointerExited;
        _pointerReleasedHandler = PointerReleased;
        _pointerCanceledHandler = PointerCanceled;

        _root.Lights.Add(_ambientLight);
        _root.Lights.Add(_hoverLight);
        // Buttons can handle their own pointer events; they remain part of the hero's lit area.
        _root.AddHandler(UIElement.PointerEnteredEvent, _pointerMovedHandler, true);
        _root.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, true);
        _root.AddHandler(UIElement.PointerExitedEvent, _pointerExitedHandler, true);
        _root.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, true);
        _root.AddHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler, true);
    }

    private void PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed) return;
        if (!_accessibilitySettings.AnimationsEnabled)
        {
            _hoverLight.Rest(false);
            return;
        }

        var point = args.GetCurrentPoint(_root).Position;
        if (!IsInside(point.X, point.Y))
        {
            _hoverLight.Rest(true);
            return;
        }

        if (args.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            _hoverLight.FollowTouch(new Vector2((float)point.X, (float)point.Y));
        else
            _hoverLight.FollowPointer();
    }

    private void PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed) return;
        var point = args.GetCurrentPoint(_root).Position;
        // Routed exits from images, text and buttons do not mean the pointer left the hero.
        if (IsInside(point.X, point.Y)) return;
        _hoverLight.Rest(_accessibilitySettings.AnimationsEnabled);
    }

    private bool IsInside(double x, double y) =>
        x >= 0 && y >= 0 && x < _root.ActualWidth && y < _root.ActualHeight;

    private void PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed || args.Pointer.PointerDeviceType != PointerDeviceType.Touch) return;
        _hoverLight.Rest(_accessibilitySettings.AnimationsEnabled);
    }

    private void PointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed) return;
        _hoverLight.Rest(_accessibilitySettings.AnimationsEnabled);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.RemoveHandler(UIElement.PointerEnteredEvent, _pointerMovedHandler);
        _root.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
        _root.RemoveHandler(UIElement.PointerExitedEvent, _pointerExitedHandler);
        _root.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
        _root.RemoveHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler);
        _root.Lights.Remove(_hoverLight);
        _root.Lights.Remove(_ambientLight);
        // Removal normally disconnects immediately; release also covers an already unloaded root.
        _hoverLight.Release();
        _ambientLight.Release();
    }

    private sealed partial class HeroAmbientLight : XamlLight
    {
        private const string LightId = "Mizon.DiscoveryHero.Ambient";
        private UIElement? _target;
        private AmbientLight? _light;

        protected override string GetId() => LightId;

        protected override void OnConnected(UIElement newElement)
        {
            Release();
            _light = CompositionTarget.GetCompositorForCurrentThread().CreateAmbientLight();
            // Matches StoreCarousel's AmbLight: the source image keeps its normal luminance.
            _light.Color = Colors.White;
            _light.Intensity = 1;
            CompositionLight = _light;
            _target = newElement;
            AddTargetElement(LightId, newElement);
        }

        protected override void OnDisconnected(UIElement oldElement) => Release();

        public void Release()
        {
            if (_target is not null)
            {
                RemoveTargetElement(LightId, _target);
                _target = null;
            }
            CompositionLight = null;
            _light?.Dispose();
            _light = null;
        }
    }

    private sealed partial class HeroHoverLight : XamlLight
    {
        private const string LightId = "Mizon.DiscoveryHero.Hover";
        private static readonly Vector3 RestingOffset = new(200, 200, 400);
        private UIElement? _target;
        private SpotLight? _light;
        private ExpressionAnimation? _pointerAnimation;
        private Vector3KeyFrameAnimation? _restAnimation;
        private bool _followingPointer;
        private bool _atRest = true;

        protected override string GetId() => LightId;

        protected override void OnConnected(UIElement newElement)
        {
            Release();
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            _light = compositor.CreateSpotLight();
            _light.InnerConeAngleInDegrees = 50;
            _light.InnerConeColor = Colors.FloralWhite;
            _light.OuterConeAngleInDegrees = 20;
            _light.ConstantAttenuation = 1;
            _light.LinearAttenuation = 0.253f;
            _light.QuadraticAttenuation = 0.58f;
            _light.Offset = RestingOffset;
            CompositionLight = _light;

            using var easing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.3f, 0.7f), new Vector2(0.9f, 0.5f));
            _restAnimation = compositor.CreateVector3KeyFrameAnimation();
            _restAnimation.InsertKeyFrame(1, RestingOffset, easing);
            _restAnimation.Duration = TimeSpan.FromMilliseconds(500);
            _pointerAnimation = compositor.CreateExpressionAnimation(
                "Vector3(hover.Position.X, hover.Position.Y, height)");
            _pointerAnimation.SetReferenceParameter("hover",
                ElementCompositionPreview.GetPointerPositionPropertySet(newElement));
            _pointerAnimation.SetScalarParameter("height", 100);

            _target = newElement;
            AddTargetElement(LightId, newElement);
        }

        public void FollowPointer()
        {
            if (_light is null || _pointerAnimation is null || _followingPointer) return;
            _followingPointer = true;
            _atRest = false;
            // The compositor follows the pointer; no rendering callback or repeated animation restart.
            _light.StartAnimation(nameof(SpotLight.Offset), _pointerAnimation);
        }

        public void FollowTouch(Vector2 position)
        {
            if (_light is null) return;
            _followingPointer = false;
            _atRest = false;
            _light.StopAnimation(nameof(SpotLight.Offset));
            _light.Offset = new Vector3(position, 15);
        }

        public void Rest(bool animate)
        {
            if (_light is null) return;
            if (_atRest && animate) return;
            _followingPointer = false;
            _atRest = true;
            if (animate && _restAnimation is not null)
                _light.StartAnimation(nameof(SpotLight.Offset), _restAnimation);
            else
            {
                _light.StopAnimation(nameof(SpotLight.Offset));
                _light.Offset = RestingOffset;
            }
        }

        protected override void OnDisconnected(UIElement oldElement) => Release();

        public void Release()
        {
            if (_target is not null)
            {
                RemoveTargetElement(LightId, _target);
                _target = null;
            }
            _light?.StopAnimation(nameof(SpotLight.Offset));
            CompositionLight = null;
            _light?.Dispose();
            _pointerAnimation?.Dispose();
            _restAnimation?.Dispose();
            _light = null;
            _pointerAnimation = null;
            _restAnimation = null;
            _followingPointer = false;
            _atRest = true;
        }
    }
}
