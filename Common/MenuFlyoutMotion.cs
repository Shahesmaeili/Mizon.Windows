using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.Common;

// Keep the native MenuFlyout and its material, but place that material inside
// the animated presenter. A window-level SystemBackdrop ignores XAML opacity.
internal sealed class MenuFlyoutMotion : IDisposable
{
    private readonly MenuFlyout _flyout;
    private readonly FrameworkElement _anchor;
    private readonly SystemBackdrop? _backdrop;
    private MenuFlyoutPresenter? _surface;
    private Grid? _backdropHost;
    private SystemBackdropElement? _backdropElement;
    private readonly UISettings _settings = new();
    private readonly Compositor _compositor = Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread();
    private readonly bool _nativeAnimations;
    private readonly DispatcherQueueTimer _closeTimeout;
    private ScalarKeyFrameAnimation? _fade;
    private Vector3KeyFrameAnimation? _resize;
    private Vector3 _originalScale, _originalCenter;
    private double _originalOpacity;
    private CompositionScopedBatch? _closeBatch;
    private bool _opening, _opened, _closing, _allowClose, _disposed;
    private int _revision;

    public MenuFlyoutMotion(MenuFlyout flyout, FrameworkElement anchor)
    {
        _flyout = flyout;
        _anchor = anchor;
        _backdrop = flyout.SystemBackdrop;
        _nativeAnimations = flyout.AreOpenCloseAnimationsEnabled;
        // Both the window backdrop and native transitions would otherwise render
        // independently of our presenter. The exact same backdrop is hosted below.
        flyout.SystemBackdrop = null;
        flyout.AreOpenCloseAnimationsEnabled = false;
        _closeTimeout = anchor.DispatcherQueue.CreateTimer();
        _closeTimeout.IsRepeating = false;
        // A paused compositor must never leave a cancelled Closing event stuck open.
        _closeTimeout.Interval = TimeSpan.FromMilliseconds(400);
        _closeTimeout.Tick += CloseTimeout;
        flyout.Opened += Opened;
        flyout.Closing += Closing;
        flyout.Closed += Closed;
        anchor.Loaded += Loaded;
        anchor.Unloaded += Unloaded;
        _settings.AnimationsEnabledChanged += PreferenceChanged;
    }

    public void PrepareOpen()
    {
        if (_disposed) return;
        Reset();
        _opening = true;
        if (_anchor.IsLoaded) PrepareVisual();
    }

    private void PrepareVisual()
    {
        if (_surface is null)
        {
            for (var parent = VisualTreeHelper.GetParent(_anchor); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not MenuFlyoutPresenter presenter) continue;
                _surface = presenter;
                _originalScale = presenter.Scale;
                _originalCenter = presenter.CenterPoint;
                _originalOpacity = presenter.Opacity;
                break;
            }
        }
        if (_surface is null) return;
        if (_backdropElement is null)
        {
            _surface.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(_surface) == 0 ||
                VisualTreeHelper.GetChild(_surface, 0) is not Grid { Name: "MenuMotionSurface" } root) return;
            foreach (var child in root.Children)
                if (child is Grid { Name: "MenuBackdropHost" } host) { _backdropHost = host; break; }
            if (_backdropHost is null) return;
            // Create only after the template is attached to its XamlRoot. Creating
            // SystemBackdropElement in a detached template fails on some SDK builds.
            _backdropElement = new SystemBackdropElement
            {
                CornerRadius = _surface.CornerRadius,
                SystemBackdrop = _backdrop,
                IsHitTestVisible = false
            };
            _backdropHost.Children.Add(_backdropElement);
        }
        // MenuFlyoutPresenter already uses XAML composition properties for its
        // shadow. Mutating its handout Visual mixes transform systems and shifts
        // hit-testing away from the buttons. Animate the UIElement itself.
        _surface.CenterPoint = new Vector3((float)_surface.ActualWidth, 0, 0);
        var animate = _settings.AnimationsEnabled;
        _surface.Opacity = animate ? 0 : _originalOpacity;
        _surface.Scale = animate ? _originalScale * new Vector3(.985f, .985f, 1) : _originalScale;
    }

    private void Loaded(object sender, RoutedEventArgs args)
    {
        if (!_opening || _disposed) return;
        PrepareVisual();
        if (_opened) Open();
    }

    private void Opened(object? sender, object args)
    {
        _opened = true;
        if (_anchor.IsLoaded && !_closing)
        {
            if (_backdropElement is null) PrepareVisual();
            Open();
        }
    }

    private void Open()
    {
        if (!_opening || _backdropElement is null || _surface is null) return;
        _opening = false;
        _surface.CenterPoint = new Vector3((float)_surface.ActualWidth, 0, 0);
        if (_settings.AnimationsEnabled) Animate(1, 1, 220);
        else RestoreVisual();
    }

    private void Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
    {
        if (_disposed || _allowClose || !_anchor.IsLoaded || _backdropElement is null || !_settings.AnimationsEnabled) return;
        args.Cancel = true;
        BeginClose();
    }

    private void BeginClose()
    {
        if (_closing || _disposed || _backdropElement is null || _surface is null) return;
        _closing = true;
        _opening = false;
        _surface.IsHitTestVisible = false;
        _closeBatch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _closeBatch.Completed += CloseCompleted;
        Animate(0, .985f, 160);
        _closeBatch.End();
        _closeTimeout.Start();
    }

    private void Animate(float opacity, float scale, int milliseconds)
    {
        if (_surface is null) return;
        using var easing = _compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, .8f), Vector2.One);
        _fade ??= _compositor.CreateScalarKeyFrameAnimation();
        _fade.Target = nameof(UIElement.Opacity);
        _fade.InsertExpressionKeyFrame(0, "this.StartingValue");
        _fade.InsertKeyFrame(1, (float)_originalOpacity * opacity, easing);
        _fade.Duration = TimeSpan.FromMilliseconds(milliseconds);
        _surface.StartAnimation(_fade);
        _resize ??= _compositor.CreateVector3KeyFrameAnimation();
        _resize.Target = nameof(UIElement.Scale);
        _resize.InsertExpressionKeyFrame(0, "this.StartingValue");
        _resize.InsertKeyFrame(1, _originalScale * new Vector3(scale, scale, 1), easing);
        _resize.Duration = _fade.Duration;
        _surface.StartAnimation(_resize);
    }

    private void CloseCompleted(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (!ReferenceEquals(sender, _closeBatch)) return;
        var revision = _revision;
        // Leave the composition callback before re-entering Flyout.Hide/Closing.
        _anchor.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && revision == _revision) FinishClose();
        });
    }

    private void CloseTimeout(DispatcherQueueTimer sender, object args) => FinishClose();

    private void FinishClose()
    {
        if (_disposed || !_closing) return;
        _closing = false;
        ReleaseClose();
        _allowClose = true;
        try { _flyout.Hide(); }
        finally { _allowClose = false; }
    }

    private void Closed(object? sender, object args) => Reset();
    private void Unloaded(object sender, RoutedEventArgs args)
    {
        Reset();
        ReleaseBackdrop();
        ReleaseAnimations();
        _surface = null;
    }

    private void PreferenceChanged(UISettings sender, object args) => _anchor.DispatcherQueue.TryEnqueue(() =>
    {
        if (_disposed) return;
        if (_settings.AnimationsEnabled) return;
        if (_closing) FinishClose();
        else RestoreVisual();
    });

    private void ReleaseClose()
    {
        _closeTimeout.Stop();
        if (_closeBatch is null) return;
        _closeBatch.Completed -= CloseCompleted;
        _closeBatch.Dispose();
        _closeBatch = null;
    }

    private void RestoreVisual()
    {
        if (_surface is null) return;
        if (_fade is not null) _surface.StopAnimation(_fade);
        if (_resize is not null) _surface.StopAnimation(_resize);
        _surface.Opacity = _originalOpacity;
        _surface.Scale = _originalScale;
        _surface.CenterPoint = _originalCenter;
    }

    private void ReleaseAnimations()
    {
        _fade?.Dispose(); _fade = null;
        _resize?.Dispose(); _resize = null;
    }

    private void Reset()
    {
        _revision++;
        _opening = _opened = _closing = false;
        ReleaseClose();
        RestoreVisual();
        if (_surface is not null) _surface.IsHitTestVisible = true;
    }

    private void ReleaseBackdrop()
    {
        if (_backdropElement is not null)
        {
            _backdropElement.SystemBackdrop = null;
            _backdropHost?.Children.Remove(_backdropElement);
            _backdropElement = null;
        }
        _backdropHost = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flyout.Opened -= Opened;
        _flyout.Closing -= Closing;
        _flyout.Closed -= Closed;
        _anchor.Loaded -= Loaded;
        _anchor.Unloaded -= Unloaded;
        _settings.AnimationsEnabledChanged -= PreferenceChanged;
        _closeTimeout.Tick -= CloseTimeout;
        Reset();
        ReleaseBackdrop();
        ReleaseAnimations();
        _surface = null;
        _flyout.SystemBackdrop = _backdrop;
        _flyout.AreOpenCloseAnimationsEnabled = _nativeAnimations;
    }
}
