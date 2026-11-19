using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Mizon.Windows.UserControls;

public sealed partial class DiscoveryFeatureHero
{
    private bool _viewportRefreshQueued;

    internal ScrollViewer? PresentationViewport { get; set; }

    internal void QueuePresentationViewportRefresh()
    {
        if (!_loaded || PresentationViewport is null || _viewportRefreshQueued) return;
        _viewportRefreshQueued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _viewportRefreshQueued = false;
            RefreshPresentationViewport();
        });
    }

    private void RefreshPresentationViewport()
    {
        if (!_loaded || PresentationViewport is not { IsLoaded: true } viewport ||
            XamlRoot is not { } root || !ReferenceEquals(root, viewport.XamlRoot)) return;

        var width = viewport.ViewportWidth;
        var height = viewport.ViewportHeight;
        if (width <= 0 || height <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        // A cached page can reattach with the same effective viewport, so XAML
        // may have no viewport change to report after the old unload.
        var bounds = TransformToVisual(viewport).TransformBounds(new Rect(0, 0, ActualWidth, ActualHeight));
        SetViewportVisibility(IntersectsViewport(bounds, width, height));
    }

    private void Hero_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args) =>
        SetViewportVisibility(IntersectsViewport(args.EffectiveViewport, ActualWidth, ActualHeight));

    private static bool IntersectsViewport(Rect rect, double width, double height) =>
        width > 0 && height > 0 && rect.Width > 0 && rect.Height > 0 &&
        rect.Right > 0 && rect.Bottom > 0 && rect.Left < width && rect.Top < height;

    private void SetViewportVisibility(bool visible)
    {
        if (_inViewport == visible) return;
        _inViewport = visible;
        UpdateActive();
    }
}
