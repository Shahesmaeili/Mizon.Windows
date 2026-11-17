using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;

namespace Mizon.Windows.Common;

/// <summary>Preserves the independent viewports of a retained page across Frame navigation.</summary>
internal sealed class NavigationScrollPositions(FrameworkElement host, params ScrollViewer[] viewports)
{
    private readonly (double Horizontal, double Vertical)[] _offsets = new (double, double)[viewports.Length];
    private WeakReference<Control>? _focus;
    private bool _restorePending;
    private int _generation;

    internal void Capture()
    {
        if (_restorePending || !host.IsLoaded) return;

        for (var index = 0; index < viewports.Length; index++)
        {
            var view = viewports[index];
            if (view.ViewportWidth > 0 && view.ViewportHeight > 0 && IsVisible(view))
                _offsets[index] = (view.HorizontalOffset, view.VerticalOffset);
        }

        _focus = host.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is Control { FocusState: FocusState.Keyboard } focused
            && Inside(focused, host) ? new(focused) : null;
        _restorePending = true;
    }

    internal void Restore()
    {
        if (!_restorePending) return;
        // Use the retained content's measured extents before the first frame is drawn.
        host.UpdateLayout();
        if (Apply()) return;
        host.LayoutUpdated -= LayoutUpdated;
        host.LayoutUpdated += LayoutUpdated;
    }

    internal void Suspend()
    {
        _generation++;
        host.LayoutUpdated -= LayoutUpdated;
    }

    internal void Reset()
    {
        Suspend();
        Array.Clear(_offsets);
        _focus = null;
        _restorePending = true;
    }

    private void LayoutUpdated(object? sender, object args) => Apply();

    private bool Apply()
    {
        if (!host.IsLoaded || host.ActualWidth <= 0 || host.ActualHeight <= 0) return false;
        foreach (var view in viewports)
            if (IsVisible(view) && view.ActualWidth > 0 && view.ActualHeight > 0
                && (view.ViewportWidth <= 0 || view.ViewportHeight <= 0)) return false;

        host.LayoutUpdated -= LayoutUpdated;
        for (var index = 0; index < viewports.Length; index++)
        {
            var view = viewports[index];
            if (!IsVisible(view) || view.ViewportWidth <= 0 || view.ViewportHeight <= 0) continue;
            var (horizontal, vertical) = _offsets[index];
            horizontal = Math.Clamp(horizontal, 0, view.ScrollableWidth);
            vertical = Math.Clamp(vertical, 0, view.ScrollableHeight);
            if (Math.Abs(horizontal - view.HorizontalOffset) > .5 || Math.Abs(vertical - view.VerticalOffset) > .5)
                view.ChangeView(horizontal, vertical, null, disableAnimation: true);
        }

        _restorePending = false;
        if (_focus is { } focus)
        {
            _focus = null;
            var generation = _generation;
            host.DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _generation || !host.IsLoaded || !focus.TryGetTarget(out var control)
                    || !control.IsLoaded || !control.IsEnabled || !control.IsTabStop || !IsVisible(control)) return;
                foreach (var viewport in viewports)
                {
                    if (!Inside(control, viewport)) continue;
                    var bounds = control.TransformToVisual(viewport)
                        .TransformBounds(new(0, 0, control.ActualWidth, control.ActualHeight));
                    if (bounds.X < 0 || bounds.Y < 0 || bounds.Right > viewport.ActualWidth
                        || bounds.Bottom > viewport.ActualHeight) return;
                }
                control.Focus(FocusState.Keyboard);
            });
        }
        return true;
    }

    private static bool Inside(DependencyObject element, DependencyObject parent)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, parent)) return true;
        return false;
    }

    private bool IsVisible(UIElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
            if (ReferenceEquals(current, host)) return true;
        }
        return false;
    }
}
