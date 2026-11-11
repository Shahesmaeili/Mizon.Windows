using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Common;

/// <summary>Owns decorative compositor motion for one loaded collage surface.</summary>
internal sealed class StoryOrbitMotion : IDisposable
{
    private static readonly UISettings Settings = new();
    private readonly FrameworkElement _owner;
    private readonly List<Orbit> _orbits = [];
    private readonly List<(UIElement Element, long Token)> _visibilityObservers = [];
    private XamlRoot? _root;
    private Window? _window;
    private AppWindow? _appWindow;
    private bool _started, _disposed, _inViewport, _visibleInTree;

    public StoryOrbitMotion(FrameworkElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <summary>Add each independently moving surface before Start; dimensions may be explicit or measured.</summary>
    public void Add(FrameworkElement element, Vector3 excursion, double durationSeconds, float rotationDegrees = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(element);
        if (_started) throw new InvalidOperationException("Add orbit surfaces before starting motion.");
        if (!float.IsFinite(excursion.X) || !float.IsFinite(excursion.Y) || !float.IsFinite(excursion.Z))
            throw new ArgumentOutOfRangeException(nameof(excursion));
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!float.IsFinite(rotationDegrees)) throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        foreach (var orbit in _orbits)
            if (ReferenceEquals(orbit.Element, element)) throw new ArgumentException("The surface is already registered.", nameof(element));
        _orbits.Add(new Orbit(element, excursion, durationSeconds, rotationDegrees));
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
            // Retain the event source: Window.AppWindow can be null during close.
            _appWindow = _window.AppWindow;
            if (_appWindow is not null) _appWindow.Changed += WindowChanged;
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
        && _root?.IsHostVisible == true && Settings.AnimationsEnabled
        && (_appWindow is null || (_appWindow.IsVisible
            && _appWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized }));

    private void ApplyMotion()
    {
        if (!_started || _disposed) return;
        try
        {
            if (!Settings.AnimationsEnabled)
            {
                foreach (var orbit in _orbits) orbit.Stop();
                return;
            }
            var active = CanAnimate;
            foreach (var orbit in _orbits)
            {
                if (active) orbit.Start();
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

    private void OwnerUnloaded(object sender, RoutedEventArgs args)
    {
        // Responsive reparenting can queue Unloaded after the element has loaded again.
        if (!_owner.IsLoaded) Dispose();
    }

    private void OwnerSizeChanged(object sender, SizeChangedEventArgs args)
    {
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

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyMotion();
    private void WindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidVisibilityChange) ApplyMotion();
    }
    private void WindowClosed(object sender, WindowEventArgs args) => Dispose();

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
            if (_appWindow is not null) _appWindow.Changed -= WindowChanged;
            foreach (var observer in _visibilityObservers)
                observer.Element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, observer.Token);
            _visibilityObservers.Clear();
        }
        _root = null;
        _window = null;
        _appWindow = null;
        foreach (var orbit in _orbits)
        {
            try { orbit.Stop(); }
            catch (Exception exception) { AppLog.Error("Story collage motion cleanup could not finish.", exception); }
        }
        _orbits.Clear();
        GC.SuppressFinalize(this);
    }

    private sealed class Orbit(FrameworkElement element, Vector3 excursion, double durationSeconds, float rotationDegrees)
    {
        public FrameworkElement Element { get; } = element;
        private Visual? _visual;
        private CompositionPropertySet? _phase;
        private ExpressionAnimation? _translation, _rotation;
        private AnimationController? _horizontal, _vertical, _tilt;
        private Vector3 _restTranslation, _restCenter;
        private float _restRotation;
        private bool _paused;

        public void Start()
        {
            if (_visual is not null) return;
            ElementCompositionPreview.SetIsTranslationEnabled(Element, true);
            _visual = ElementCompositionPreview.GetElementVisual(Element);
            _visual.Properties.TryGetVector3("Translation", out _restTranslation);
            _visual.Properties.InsertVector3("Translation", _restTranslation);
            _restRotation = _visual.RotationAngleInDegrees;
            _restCenter = _visual.CenterPoint;
            Element.SizeChanged += SizeChanged;
            Center();
            var compositor = _visual.Compositor;
            _phase = compositor.CreatePropertySet();
            _horizontal = StartPhase("Horizontal", durationSeconds);
            _vertical = StartPhase("Vertical", durationSeconds * 1.25);
            _translation = compositor.CreateExpressionAnimation(
                "rest + Vector3(Sin(phase.Horizontal) * excursion.X, Sin(phase.Vertical) * excursion.Y, Sin(phase.Horizontal) * excursion.Z)");
            _translation.SetReferenceParameter("phase", _phase);
            _translation.SetVector3Parameter("rest", _restTranslation);
            _translation.SetVector3Parameter("excursion", excursion);
            _visual.Properties.StartAnimation("Translation", _translation);
            if (rotationDegrees != 0)
            {
                _tilt = StartPhase("Tilt", durationSeconds * 1.5);
                _rotation = compositor.CreateExpressionAnimation("rest + Sin(phase.Tilt) * amplitude");
                _rotation.SetReferenceParameter("phase", _phase);
                _rotation.SetScalarParameter("rest", _restRotation);
                _rotation.SetScalarParameter("amplitude", rotationDegrees);
                _visual.StartAnimation("RotationAngleInDegrees", _rotation);
            }
            _paused = false;
        }

        private AnimationController? StartPhase(string property, double seconds)
        {
            _phase!.InsertScalar(property, 0);
            using var animation = _phase.Compositor.CreateScalarKeyFrameAnimation();
            using var linear = _phase.Compositor.CreateLinearEasingFunction();
            animation.InsertKeyFrame(0, 0);
            animation.InsertKeyFrame(1, MathF.Tau, linear);
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
                _phase?.StopAnimation("Horizontal");
                _phase?.StopAnimation("Vertical");
                if (rotationDegrees != 0) _phase?.StopAnimation("Tilt");
                _visual.Properties.InsertVector3("Translation", _restTranslation);
                _visual.RotationAngleInDegrees = _restRotation;
                _visual.CenterPoint = _restCenter;
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
                _translation = _rotation = null;
                _phase = null;
                _paused = false;
                try { translation?.Dispose(); }
                finally
                {
                    try { rotation?.Dispose(); }
                    finally { phase?.Dispose(); }
                }
            }
        }
    }
}
