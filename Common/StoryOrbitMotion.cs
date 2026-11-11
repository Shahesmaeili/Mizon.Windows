using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Common;

/// <summary>Owns decorative compositor motion for one loaded collage surface.</summary>
internal sealed partial class StoryOrbitMotion : IDisposable
{
    private static readonly UISettings Settings = new();
    private readonly FrameworkElement _owner;
    private readonly List<Orbit> _orbits = [];
    private readonly List<(UIElement Element, long Token)> _visibilityObservers = [];
    private XamlRoot? _root;
    private Window? _window;
    private AppWindow? _appWindow;
    private bool _started, _disposed, _inViewport, _visibleInTree;
    private FrameworkElement? _pointerSurface;
    private Vector2 _pointerCenter, _pointerRadius;
    private CompositionPropertySet? _pointerPosition, _pointerState;
    private ExpressionAnimation? _pointerExpression;
    private bool _pointerInside, _windowActive = true;

    public StoryOrbitMotion(FrameworkElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    // Coordinates belong to the stationary input surface, not to moving artwork.
    public void EnablePointerInput(FrameworkElement surface, Vector2 center, Vector2 radius)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        if (_started) throw new InvalidOperationException("Configure pointer input before Start.");
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y)
            || !float.IsFinite(radius.X) || !float.IsFinite(radius.Y) || radius.X <= 0 || radius.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        _pointerSurface = surface;
        _pointerCenter = center;
        _pointerRadius = radius;
    }

    /// <summary>Add each independently moving surface before Start; dimensions may be explicit or measured.</summary>
    public void Add(FrameworkElement element, Vector3 excursion, double durationSeconds, float rotationDegrees = 0,
        float phaseDegrees = 0, OrbitPointerResponse pointer = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(element);
        if (_started) throw new InvalidOperationException("Add orbit surfaces before starting motion.");
        if (!float.IsFinite(excursion.X) || !float.IsFinite(excursion.Y) || !float.IsFinite(excursion.Z))
            throw new ArgumentOutOfRangeException(nameof(excursion));
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!float.IsFinite(rotationDegrees)) throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        if (!float.IsFinite(phaseDegrees)) throw new ArgumentOutOfRangeException(nameof(phaseDegrees));
        if (!float.IsFinite(pointer.Travel.X) || !float.IsFinite(pointer.Travel.Y)
            || !float.IsFinite(pointer.Lift) || !float.IsFinite(pointer.Rotation)
            || !float.IsFinite(pointer.Zoom) || pointer.Zoom < 0
            || (pointer != default && pointer.ResponseMilliseconds <= 0))
            throw new ArgumentOutOfRangeException(nameof(pointer));
        foreach (var orbit in _orbits)
            if (ReferenceEquals(orbit.Element, element)) throw new ArgumentException("The surface is already registered.", nameof(element));
        _orbits.Add(new Orbit(element, excursion, durationSeconds, rotationDegrees, phaseDegrees, pointer));
    }

    /// <summary>Call from the owner's Loaded handler. Dispose on departure; create a new helper on return.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started || !_owner.IsLoaded) return;
        _started = true;
        _owner.Unloaded += OwnerUnloaded;
        _owner.SizeChanged += OwnerSizeChanged;
        _owner.EffectiveViewportChanged += ViewportChanged;
        Settings.AnimationsEnabledChanged += AnimationsChanged;
        _root = _owner.XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        _window = App.MainMizonWindow;
        if (_window is not null)
        {
            _window.Closed += WindowClosed;
            _window.Activated += WindowActivated;
            _windowActive = GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(_window);
            // Retain the event source: Window.AppWindow can be null during close.
            _appWindow = _window.AppWindow;
            if (_appWindow is not null) _appWindow.Changed += WindowChanged;
        }
        if (_pointerSurface is not null)
        {
            _pointerSurface.PointerEntered += PointerEntered;
            // Recovery only: normal moves return at the first bool check.
            // Re-arm if resize/deactivation reset hover without a boundary exit.
            _pointerSurface.PointerMoved += PointerEntered;
            _pointerSurface.PointerExited += PointerLeft;
            _pointerSurface.PointerCanceled += PointerLeft;
            _pointerSurface.PointerCaptureLost += PointerLeft;
        }
        for (DependencyObject? current = _owner; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement element)
                _visibilityObservers.Add((element, element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, VisibilityChanged)));
        }
        _inViewport = IntersectsViewport();
        RefreshVisibility();
    }

    private bool CanAnimate => _started && !_disposed && _owner.IsLoaded && _inViewport && _visibleInTree
        && _root?.IsHostVisible == true && Settings.AnimationsEnabled && _windowActive
        && (_appWindow is null || (_appWindow.IsVisible
            && _appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized }));

    private void ApplyMotion()
    {
        if (!_started || _disposed) return;
        try
        {
            if (!Settings.AnimationsEnabled)
            {
                ResetPointer(immediate: true);
                foreach (var orbit in _orbits) orbit.Stop();
                ReleasePointerSource();
                return;
            }
            var active = CanAnimate;
            if (!active) ResetPointer(immediate: true);
            else EnsurePointerSource();
            foreach (var orbit in _orbits)
            {
                if (active) orbit.Start(_pointerState);
                orbit.SetPaused(!active);
            }
        }
        catch (Exception exception)
        {
            // A decorative effect must not take down the page when a late
            // visibility/settings notification reaches a failing compositor.
            AppLog.Error("Story collage motion is unavailable.", exception);
            Dispose();
        }
    }

    private void EnsurePointerSource()
    {
        if (_pointerSurface is null || _pointerState is not null) return;
        // XAML updates this source on the compositor. Retain the borrowed set;
        // do not dispose it, poll it, or restart animations on PointerMoved.
        _pointerPosition = ElementCompositionPreview.GetPointerPositionPropertySet(_pointerSurface);
        _pointerState = _pointerPosition.Compositor.CreatePropertySet();
        _pointerState.InsertVector2("Position", Vector2.Zero);
        // The native source can report zero when no pointer is tracked. Keep
        // its last direction until the transition captures the displayed pose.
        _pointerExpression = _pointerPosition.Compositor.CreateExpressionAnimation(
            "mouse.Position == Vector3(0, 0, 0) ? this.CurrentValue : Vector2(Clamp((mouse.Position.X - center.X) / radius.X, -1, 1), Clamp((mouse.Position.Y - center.Y) / radius.Y, -1, 1))");
        _pointerExpression.SetReferenceParameter("mouse", _pointerPosition);
        _pointerExpression.SetVector2Parameter("center", _pointerCenter);
        _pointerExpression.SetVector2Parameter("radius", _pointerRadius);
        _pointerState.StartAnimation("Position", _pointerExpression);
    }

    private void PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (_pointerInside || !CanAnimate || _pointerState is null
            || args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        try
        {
            _pointerInside = true;
            foreach (var orbit in _orbits) orbit.SetHovered(true);
        }
        catch (Exception error)
        {
            AppLog.Error("Story collage pointer motion is unavailable.", error);
            Dispose();
        }
    }

    private void PointerLeft(object sender, PointerRoutedEventArgs args)
    {
        if (!_pointerInside || args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        ResetPointer(immediate: !CanAnimate);
    }

    private void ResetPointer(bool immediate)
    {
        _pointerInside = false;
        if (_disposed) return;
        try
        {
            foreach (var orbit in _orbits) orbit.SetHovered(false, immediate);
        }
        catch (Exception error)
        {
            AppLog.Error("Story collage pointer reset could not finish.", error);
            Dispose();
        }
    }

    private void ReleasePointerSource()
    {
        var state = _pointerState;
        var expression = _pointerExpression;
        _pointerState = null;
        _pointerExpression = null;
        _pointerPosition = null; // Borrowed XAML property set, never Close/Dispose.
        try { state?.StopAnimation("Position"); }
        finally
        {
            try { expression?.Dispose(); }
            finally { state?.Dispose(); }
        }
    }

    private void OwnerUnloaded(object sender, RoutedEventArgs args)
    {
        // Responsive reparenting can queue Unloaded after the element has loaded again.
        if (!_owner.IsLoaded) Dispose();
    }

    private void OwnerSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ResetPointer(immediate: true);
        _inViewport = IntersectsViewport();
        ApplyMotion();
    }

    private void ViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var viewport = args.EffectiveViewport;
        _inViewport = viewport.Width > 0 && viewport.Height > 0 && viewport.Right > 0 && viewport.Bottom > 0
            && viewport.Left < _owner.ActualWidth && viewport.Top < _owner.ActualHeight;
        ApplyMotion();
    }

    // Loaded may follow the first effective-viewport notification. Check the current
    // root and scrolling ancestors once so a below-the-fold collage starts paused.
    private bool IntersectsViewport()
    {
        if (!_owner.IsLoaded || !ReferenceEquals(_owner.XamlRoot, _root)
            || _root?.Content is not FrameworkElement root || _owner.ActualWidth <= 0 || _owner.ActualHeight <= 0) return false;
        if (!Intersects(root)) return false;
        for (DependencyObject? current = VisualTreeHelper.GetParent(_owner); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ScrollViewer scroll && !Intersects(scroll)) return false;
        return true;
    }

    private bool Intersects(FrameworkElement viewport)
    {
        var bounds = _owner.TransformToVisual(viewport).TransformBounds(new Rect(0, 0, _owner.ActualWidth, _owner.ActualHeight));
        return bounds.Width > 0 && bounds.Height > 0 && bounds.Right > 0 && bounds.Bottom > 0
            && bounds.Left < viewport.ActualWidth && bounds.Top < viewport.ActualHeight;
    }

    private void VisibilityChanged(DependencyObject sender, DependencyProperty property) => RefreshVisibility();

    private void RefreshVisibility()
    {
        // Collapsing an ancestor does not always raise EffectiveViewportChanged.
        _visibleInTree = true;
        foreach (var observer in _visibilityObservers)
        {
            if (observer.Element.Visibility == Visibility.Visible) continue;
            _visibleInTree = false;
            break;
        }
        ApplyMotion();
    }

    private void AnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        => _owner.DispatcherQueue.TryEnqueue(() =>
        {
            // A settings notification may already be queued when this helper is disposed.
            if (!_disposed) ApplyMotion();
        });

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        // DPI/viewport changes invalidate pointer coordinates even if the
        // fixed design-space canvas itself did not receive SizeChanged.
        ResetPointer(immediate: true);
        ApplyMotion();
    }
    private void WindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidVisibilityChange) ApplyMotion();
    }
    private void WindowClosed(object sender, WindowEventArgs args) => Dispose();
    private void WindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        ApplyMotion();
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_started)
        {
            _started = false;
            _owner.Unloaded -= OwnerUnloaded;
            _owner.SizeChanged -= OwnerSizeChanged;
            _owner.EffectiveViewportChanged -= ViewportChanged;
            Settings.AnimationsEnabledChanged -= AnimationsChanged;
            if (_root is not null) _root.Changed -= RootChanged;
            if (_window is not null) _window.Closed -= WindowClosed;
            if (_window is not null) _window.Activated -= WindowActivated;
            if (_appWindow is not null) _appWindow.Changed -= WindowChanged;
            foreach (var observer in _visibilityObservers)
                observer.Element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, observer.Token);
            _visibilityObservers.Clear();
            if (_pointerSurface is not null)
            {
                _pointerSurface.PointerEntered -= PointerEntered;
                _pointerSurface.PointerMoved -= PointerEntered;
                _pointerSurface.PointerExited -= PointerLeft;
                _pointerSurface.PointerCanceled -= PointerLeft;
                _pointerSurface.PointerCaptureLost -= PointerLeft;
            }
        }
        _pointerInside = false;
        _pointerSurface = null;
        _root = null;
        _window = null;
        _appWindow = null;
        foreach (var orbit in _orbits)
        {
            try { orbit.Stop(); }
            catch (Exception exception) { AppLog.Error("Story collage motion cleanup could not finish.", exception); }
        }
        _orbits.Clear();
        try { ReleasePointerSource(); }
        catch (Exception exception) { AppLog.Error("Story collage pointer source cleanup could not finish.", exception); }
        GC.SuppressFinalize(this);
    }

    private sealed class Orbit(FrameworkElement element, Vector3 excursion, double durationSeconds,
        float rotationDegrees, float phaseDegrees, OrbitPointerResponse pointer)
    {
        public FrameworkElement Element { get; } = element;
        private Visual? _visual;
        private CompositionPropertySet? _phase;
        private ExpressionAnimation? _translation, _rotation, _scale, _pointer;
        private ScalarKeyFrameAnimation? _entryAnimation;
        private Vector3KeyFrameAnimation? _exitAnimation;
        private CubicBezierEasingFunction? _pointerEasing;
        private AnimationController? _horizontal, _vertical, _tilt;
        private Vector3 _restTranslation, _restCenter, _restScale;
        private bool _hovered;
        private float _restRotation;
        private bool _paused;

        public void Start(CompositionPropertySet? pointerInput)
        {
            if (_visual is not null) return;
            ElementCompositionPreview.SetIsTranslationEnabled(Element, true);
            _visual = ElementCompositionPreview.GetElementVisual(Element);
            _visual.Properties.TryGetVector3("Translation", out _restTranslation);
            _visual.Properties.InsertVector3("Translation", _restTranslation);
            _restRotation = _visual.RotationAngleInDegrees;
            _restCenter = _visual.CenterPoint;
            _restScale = _visual.Scale;
            Element.SizeChanged += SizeChanged;
            Center();
            var compositor = _visual.Compositor;
            _phase = compositor.CreatePropertySet();
            _phase.InsertVector3("Pointer", Vector3.Zero);
            _phase.InsertScalar("Blend", 0);
            var seed = phaseDegrees * MathF.PI / 180;
            var origin = new Vector3(MathF.Sin(seed), MathF.Sin(seed + 1.1f), MathF.Sin(seed + 2.3f));
            _phase.InsertScalar("Horizontal", seed);
            _phase.InsertScalar("Vertical", seed + 1.1f);
            _phase.InsertScalar("Tilt", seed + 2.3f);
            if (excursion != Vector3.Zero)
            {
                _horizontal = StartPhase("Horizontal", durationSeconds, seed);
                _vertical = StartPhase("Vertical", durationSeconds * 1.31, seed + 1.1f);
            }
            _translation = compositor.CreateExpressionAnimation(
                "rest + Vector3((Sin(phase.Horizontal) - origin.X) * excursion.X, (Sin(phase.Vertical) - origin.Y) * excursion.Y, (Sin(phase.Horizontal) - origin.X) * excursion.Z) * 0.5 + Vector3(phase.Pointer.X * travel.X, phase.Pointer.Y * travel.Y + phase.Pointer.Z * lift, 0)");
            _translation.SetReferenceParameter("phase", _phase);
            _translation.SetVector3Parameter("rest", _restTranslation);
            _translation.SetVector3Parameter("excursion", excursion);
            // Subtract the initial phase so every layer starts at its authored
            // pose without a jump. Half amplitude keeps all excursions bounded.
            _translation.SetVector3Parameter("origin", origin);
            _translation.SetVector2Parameter("travel", pointer.Travel);
            _translation.SetScalarParameter("lift", pointer.Lift);
            _visual.Properties.StartAnimation("Translation", _translation);
            if (rotationDegrees != 0 || pointer.Rotation != 0)
            {
                if (rotationDegrees != 0) _tilt = StartPhase("Tilt", durationSeconds * 1.73, seed + 2.3f);
                _rotation = compositor.CreateExpressionAnimation("rest + (Sin(phase.Tilt) - origin) * amplitude * 0.5 + phase.Pointer.X * pointerRotation");
                _rotation.SetReferenceParameter("phase", _phase);
                _rotation.SetScalarParameter("rest", _restRotation);
                _rotation.SetScalarParameter("amplitude", rotationDegrees);
                _rotation.SetScalarParameter("origin", origin.Z);
                _rotation.SetScalarParameter("pointerRotation", pointer.Rotation);
                _visual.StartAnimation("RotationAngleInDegrees", _rotation);
            }
            if (pointer != default && pointerInput is not null)
            {
                // Enter from the actual animated pose (including an unfinished
                // exit), then continuously follow the compositor's live input.
                _pointer = compositor.CreateExpressionAnimation(
                    "Lerp(this.StartingValue, Vector3(input.Position.X, input.Position.Y, 1), phase.Blend)");
                _pointer.SetReferenceParameter("input", pointerInput);
                _pointer.SetReferenceParameter("phase", _phase);
                _pointerEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(.18f, .7f), new Vector2(.2f, 1));
                _entryAnimation = compositor.CreateScalarKeyFrameAnimation();
                _entryAnimation.Duration = TimeSpan.FromMilliseconds(pointer.ResponseMilliseconds);
                _entryAnimation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
                _entryAnimation.InsertKeyFrame(0, 0);
                _entryAnimation.InsertKeyFrame(1, 1, _pointerEasing);
                _exitAnimation = compositor.CreateVector3KeyFrameAnimation();
                _exitAnimation.Duration = _entryAnimation.Duration;
                _exitAnimation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
                _exitAnimation.InsertExpressionKeyFrame(0, "this.StartingValue");
                _exitAnimation.InsertKeyFrame(1, Vector3.Zero, _pointerEasing);
                if (pointer.Zoom != 0)
                {
                    _scale = compositor.CreateExpressionAnimation("rest * (1 + phase.Pointer.Z * zoom)");
                    _scale.SetReferenceParameter("phase", _phase);
                    _scale.SetVector3Parameter("rest", _restScale);
                    _scale.SetScalarParameter("zoom", pointer.Zoom);
                    _visual.StartAnimation("Scale", _scale);
                }
            }
            _paused = false;
        }

        public void SetHovered(bool hovered, bool immediate = false)
        {
            if (_phase is null || _pointer is null || _entryAnimation is null || _exitAnimation is null) return;
            if (immediate)
            {
                _phase.StopAnimation("Pointer");
                _phase.StopAnimation("Blend");
                _phase.InsertVector3("Pointer", Vector3.Zero);
                _phase.InsertScalar("Blend", hovered ? 1 : 0);
                if (hovered) _phase.StartAnimation("Pointer", _pointer);
            }
            else
            {
                if (_hovered == hovered) return;
                _phase.StopAnimation("Blend");
                if (hovered)
                {
                    _phase.InsertScalar("Blend", 0);
                    _phase.StartAnimation("Pointer", _pointer);
                    _phase.StartAnimation("Blend", _entryAnimation);
                }
                else
                {
                    // Capture the displayed pose on the compositor, rather
                    // than reading an already-invalid native mouse position.
                    _phase.StartAnimation("Pointer", _exitAnimation);
                }
            }
            _hovered = hovered;
        }

        private AnimationController? StartPhase(string property, double seconds, float seed)
        {
            _phase!.InsertScalar(property, seed);
            using var animation = _phase.Compositor.CreateScalarKeyFrameAnimation();
            using var linear = _phase.Compositor.CreateLinearEasingFunction();
            animation.InsertKeyFrame(0, seed);
            animation.InsertKeyFrame(1, seed + MathF.Tau, linear);
            animation.Duration = TimeSpan.FromSeconds(seconds);
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            _phase.StartAnimation(property, animation);
            return _phase.TryGetAnimationController(property);
        }

        private void SizeChanged(object sender, SizeChangedEventArgs args) => Center();
        private void Center()
        {
            if (_visual is null) return;
            var width = Element.ActualWidth > 0 ? Element.ActualWidth : Element.Width;
            var height = Element.ActualHeight > 0 ? Element.ActualHeight : Element.Height;
            if (double.IsFinite(width) && double.IsFinite(height))
                _visual.CenterPoint = new Vector3((float)width / 2, (float)height / 2, _restCenter.Z);
        }

        public void SetPaused(bool paused)
        {
            if (_visual is null || _paused == paused) return;
            _paused = paused;
            if (paused)
            {
                _horizontal?.Pause();
                _vertical?.Pause();
                _tilt?.Pause();
            }
            else
            {
                _horizontal?.Resume();
                _vertical?.Resume();
                _tilt?.Resume();
            }
        }

        public void Stop()
        {
            if (_visual is null) return;
            Element.SizeChanged -= SizeChanged;
            try
            {
                _visual.Properties.StopAnimation("Translation");
                if (_rotation is not null) _visual.StopAnimation("RotationAngleInDegrees");
                if (_scale is not null) _visual.StopAnimation("Scale");
                _phase?.StopAnimation("Pointer");
                _phase?.StopAnimation("Blend");
                _phase?.StopAnimation("Horizontal");
                _phase?.StopAnimation("Vertical");
                if (rotationDegrees != 0) _phase?.StopAnimation("Tilt");
                _visual.Properties.InsertVector3("Translation", _restTranslation);
                _visual.RotationAngleInDegrees = _restRotation;
                _visual.CenterPoint = _restCenter;
                _visual.Scale = _restScale;
            }
            finally
            {
                // XAML owns the Visual and Composition owns its active controllers.
                // StopAnimation releases the clocks; closing controllers can corrupt a pending native commit.
                _horizontal = _vertical = _tilt = null;
                _visual = null;
                var translation = _translation;
                var rotation = _rotation;
                var phase = _phase;
                var scale = _scale;
                var pointerExpression = _pointer;
                var entryAnimation = _entryAnimation;
                var exitAnimation = _exitAnimation;
                var pointerEasing = _pointerEasing;
                _translation = _rotation = null;
                _scale = null;
                _pointer = null;
                _entryAnimation = null;
                _exitAnimation = null;
                _pointerEasing = null;
                _phase = null;
                _hovered = false;
                _paused = false;
                try { translation?.Dispose(); }
                finally
                {
                    try { rotation?.Dispose(); }
                    finally
                    {
                        try { scale?.Dispose(); }
                        finally
                        {
                            try { pointerExpression?.Dispose(); }
                            finally
                            {
                                try { entryAnimation?.Dispose(); }
                                finally
                                {
                                    try { exitAnimation?.Dispose(); }
                                    finally
                                    {
                                        try { pointerEasing?.Dispose(); }
                                        finally { phase?.Dispose(); }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

internal readonly record struct OrbitPointerResponse(Vector2 Travel, float Lift = 0, float Rotation = 0,
    float Zoom = 0, int ResponseMilliseconds = 220);
