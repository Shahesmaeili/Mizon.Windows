// Derived from DevWinUI Particle, Copyright (c) 2023 Mahdi Hosseini (MIT).
// See README.md and LICENSE.txt for source provenance and the explicit local changes.
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Controls;

[TemplatePart(Name = CanvasPart, Type = typeof(CanvasControl))]
[TemplatePart(Name = CanvasHostPart, Type = typeof(Grid))]
public sealed partial class Particle : Control, IDisposable
{
    private const string CanvasPart = "PART_Canvas";
    private const string CanvasHostPart = "PART_CanvasHost";
    private const float LinkDistance = 120;
    private readonly ParticleScene _scene = new();
    private readonly UISettings _settings = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly List<(UIElement Element, long Token)> _visibilityObservers = new();
    private Grid? _canvasHost;
    private CanvasControl? _canvas;
    private XamlRoot? _root;
    private Color _lineColor = Colors.DarkGray;
    private Color _particleColor = Colors.Gray;
    private Vector2 _pointerPosition;
    private long _pointerDrawCount;
    private int _lifetimeVersion;
    private bool _pointerIn;
    private bool _pointerHandlers;
    private bool _loaded;
    private bool _disposed;
    private bool _framePending;
    private bool _inViewport = true;
    private bool _visibleInTree = true;
    private bool _animationsEnabled = true;

    public Particle()
    {
        DefaultStyleKey = typeof(Particle);
        IsTabStop = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1d / 60);
        Loaded += ControlLoaded;
        Unloaded += ControlUnloaded;
        SizeChanged += ControlSizeChanged;
    }

    protected override void OnApplyTemplate()
    {
        ReleaseCanvas();
        base.OnApplyTemplate();
        if (_disposed) return;
        _canvasHost = GetTemplateChild(CanvasHostPart) as Grid;
        _canvas = GetTemplateChild(CanvasPart) as CanvasControl;
        AttachCanvas();
        if (_loaded) UpdateParticles();
        ApplyPlayback();
    }

    private void ControlLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _loaded) return;
        _loaded = true;
        _lifetimeVersion++;
        _timer.Tick += TimerTick;
        _inViewport = true;
        _animationsEnabled = _settings.AnimationsEnabled;
        _settings.AnimationsEnabledChanged += AnimationsChanged;
        _root = XamlRoot;
        if (_root is not null) _root.Changed += RootChanged;
        EffectiveViewportChanged += ViewportChanged;
        ObserveVisibility();
        EnsureCanvas();
        UpdatePointerHandlers();
        UpdateParticles();
        ApplyPlayback();
    }

    private void ControlUnloaded(object sender, RoutedEventArgs args) => ReleaseLoadedResources();

    private void EnsureCanvas()
    {
        if (_canvas is not null || _canvasHost is null || !_loaded || _disposed) return;
        _canvas = new CanvasControl();
        AttachCanvas();
        _canvasHost.Children.Add(_canvas);
    }

    private void AttachCanvas()
    {
        if (_canvas is null) return;
        _canvas.ClearColor = Colors.Transparent;
        _canvas.Draw += CanvasDraw;
        _canvas.Visibility = Paused ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetAccessibilityView(_canvas, AccessibilityView.Raw);
        _framePending = false;
    }

    private bool CanAnimate => _loaded && !_disposed && _canvas is not null
        && _root?.IsHostVisible == true && _inViewport && _visibleInTree
        && _animationsEnabled && IsAnimationEnabled && !Paused && _scene.Particles.Count > 0;

    private void ApplyPlayback()
    {
        if (_disposed) return;
        if (_canvas is not null)
            _canvas.Visibility = Paused ? Visibility.Collapsed : Visibility.Visible;
        if (CanAnimate)
        {
            if (!_timer.IsRunning)
            {
                // A canvas invalidated just before minimization may not receive
                // its Draw callback. Do not let that old pending frame block resume.
                _framePending = false;
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
            _pointerIn = false;
        }
        Invalidate();
    }

    private void TimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!CanAnimate) { _timer.Stop(); return; }
        Invalidate();
    }

    private void Invalidate()
    {
        if (_disposed || !_loaded || _framePending || _canvas is null || Paused) return;
        _framePending = true;
        _canvas.Invalidate();
    }

    private void ControlSizeChanged(object sender, SizeChangedEventArgs args) => UpdateParticles();

    private void UpdateParticles()
    {
        if (_disposed || !_loaded) return;
        if (_scene.Resize(ActualWidth, ActualHeight, Density)) Invalidate();
        ApplyPlayback();
    }

    private void UpdatePointerHandlers()
    {
        var shouldHandle = _loaded && !_disposed && IsPointerEnable;
        if (shouldHandle == _pointerHandlers) return;
        _pointerHandlers = shouldHandle;
        if (shouldHandle)
        {
            PointerMoved += ControlPointerMoved;
            PointerExited += ControlPointerExited;
            PointerCanceled += ControlPointerExited;
            PointerCaptureLost += ControlPointerExited;
        }
        else
        {
            PointerMoved -= ControlPointerMoved;
            PointerExited -= ControlPointerExited;
            PointerCanceled -= ControlPointerExited;
            PointerCaptureLost -= ControlPointerExited;
            _pointerIn = false;
        }
    }

    private void ControlPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!CanAnimate) return;
        var position = args.GetCurrentPoint(this).Position;
        _pointerPosition = new Vector2((float)position.X, (float)position.Y);
        _pointerDrawCount = 0;
        _pointerIn = true;
    }

    private void ControlPointerExited(object sender, PointerRoutedEventArgs args) => _pointerIn = false;

    private void CanvasDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_disposed || !_loaded || !ReferenceEquals(sender, _canvas)) return;
        _framePending = false;
        var animate = CanAnimate;
        var session = args.DrawingSession;
        var particles = _scene.Particles;
        // CanvasControl draws on the UI thread. No SpinLock, scene copy, LINQ,
        // brush allocation or new timer is needed for each frame.
        for (var i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            var offset = new Vector2(particle.Offset.X, particle.Offset.Y);
            for (var j = i + 1; j < particles.Count; j++)
            {
                var other = particles[j].Offset;
                var otherOffset = new Vector2(other.X, other.Y);
                var rangeSquared = Vector2.DistanceSquared(offset, otherOffset);
                if (rangeSquared < LinkDistance * LinkDistance)
                    session.DrawLine(offset, otherOffset, _lineColor, (LinkDistance - MathF.Sqrt(rangeSquared)) / 80);
            }

            // Retain the upstream link falloff, attraction beyond 80px, idle
            // pointer timeout and depth-based radius, including draw/step order.
            if (animate && _pointerHandlers && _pointerIn)
            {
                var range = particle.GetRange(_pointerPosition);
                if (range < LinkDistance)
                    session.DrawLine(offset, _pointerPosition, _lineColor, (LinkDistance - range) / 80);
                if (range > 80 && range < LinkDistance)
                {
                    var difference = _pointerPosition - offset;
                    var pointerHead = difference / MathF.Max(MathF.Abs(difference.X), MathF.Abs(difference.Y));
                    particle.Offset += new Vector3(pointerHead, 0);
                    offset = new Vector2(particle.Offset.X, particle.Offset.Y);
                }
                if (_pointerDrawCount > 36000) _pointerIn = false;
                _pointerDrawCount++;
            }

            session.FillCircle(offset, particle.Offset.Z / 30, _particleColor);
            if (animate) particle.NextStep();
        }
    }

    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyPlayback();

    private void ViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var visible = args.EffectiveViewport.Width > 0 && args.EffectiveViewport.Height > 0;
        if (_inViewport == visible) return;
        _inViewport = visible;
        ApplyPlayback();
    }

    private void ObserveVisibility()
    {
        // A collapsed ancestor does not always raise EffectiveViewportChanged.
        // Walk once per attachment; never walk the visual tree during a frame.
        for (DependencyObject? current = this; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement element)
                _visibilityObservers.Add((element, element.RegisterPropertyChangedCallback(VisibilityProperty, VisibilityChanged)));
        }
        VisibilityChanged(this, VisibilityProperty);
    }

    private void VisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        _visibleInTree = true;
        foreach (var observer in _visibilityObservers)
        {
            if (observer.Element.Visibility == Visibility.Visible) continue;
            _visibleInTree = false;
            break;
        }
        ApplyPlayback();
    }

    private void AnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        var version = _lifetimeVersion;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || !_loaded || version != _lifetimeVersion) return;
            _animationsEnabled = _settings.AnimationsEnabled;
            ApplyPlayback();
        });
    }

    private void ReleaseCanvas()
    {
        _timer.Stop();
        _framePending = false;
        if (_canvas is not null)
        {
            var canvas = _canvas;
            _canvas = null;
            canvas.Draw -= CanvasDraw;
            _canvasHost?.Children.Remove(canvas);
            canvas.RemoveFromVisualTree();
        }
    }

    private void ReleaseLoadedResources()
    {
        _lifetimeVersion++;
        if (_loaded)
        {
            _loaded = false;
            _timer.Tick -= TimerTick;
            _settings.AnimationsEnabledChanged -= AnimationsChanged;
            EffectiveViewportChanged -= ViewportChanged;
            if (_root is not null) _root.Changed -= RootChanged;
            _root = null;
            foreach (var observer in _visibilityObservers)
                observer.Element.UnregisterPropertyChangedCallback(VisibilityProperty, observer.Token);
            _visibilityObservers.Clear();
        }
        UpdatePointerHandlers();
        ReleaseCanvas();
        _scene.Clear();
        _pointerIn = false;
        _pointerDrawCount = 0;
    }

    /// <summary>Final teardown on the UI thread; normal cached navigation uses Unloaded/Loaded.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseLoadedResources();
        _timer.Tick -= TimerTick;
        Loaded -= ControlLoaded;
        Unloaded -= ControlUnloaded;
        SizeChanged -= ControlSizeChanged;
        _canvasHost = null;
        GC.SuppressFinalize(this);
    }
}
