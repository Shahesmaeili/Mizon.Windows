using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Services;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Mizon.Windows.Common;

// Login and Welcome share the main window. Only the current page may own its
// caption passthrough region, including while cached pages are unloading.
internal sealed class AuthHeaderLanguageRegion : IDisposable
{
    private static AuthHeaderLanguageRegion? _owner;
    private readonly FrameworkElement _page;
    private readonly FrameworkElement _host;
    private readonly RowDefinition _captionInset;
    private readonly AppWindow? _window;
    private readonly Action<bool>? _placementChanged;
    private InputNonClientPointerSource? _pointer;
    private RectInt32? _registeredRegion;
    private bool? _inCaption;

    internal AuthHeaderLanguageRegion(FrameworkElement page, FrameworkElement host, RowDefinition captionInset,
        AppWindow? window, Action<bool>? placementChanged = null)
    {
        _owner?.Dispose();
        _owner = this;
        _page = page;
        _host = host;
        _captionInset = captionInset;
        _window = window;
        _placementChanged = placementChanged;

        try
        {
            if (window is not null) _pointer = InputNonClientPointerSource.GetForWindowId(window.Id);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        {
            AppLog.Information($"Account title bar input is unavailable ({error.GetType().Name}).");
        }
        _host.LayoutUpdated += LayoutUpdated;
        Refresh();
    }

    internal static double GetCaptionHeight(AppWindow? window, XamlRoot? root)
    {
        if (window?.Presenter.Kind == AppWindowPresenterKind.FullScreen) return 0;
        return window?.TitleBar is { Height: > 0 } titleBar && root is { RasterizationScale: > 0 }
            ? Math.Ceiling(titleBar.Height / root.RasterizationScale) : 48;
    }

    private void LayoutUpdated(object? sender, object args) => Refresh();

    private void Refresh()
    {
        if (!_page.IsLoaded || !ReferenceEquals(_owner, this)) return;
        try
        {
            Update();
        }
        catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException)
        {
            AppLog.Information($"Account title bar input could not update ({error.GetType().Name}).");
            ClearRegion();
            Place(inCaption: false, _captionInset.Height.Value, leftInset: 0);
        }
    }

    private void Update()
    {
        var scale = _page.XamlRoot?.RasterizationScale ?? 1;
        var captionHeight = GetCaptionHeight(_window, _page.XamlRoot);
        var left = _window?.TitleBar.LeftInset ?? 0;
        var right = _window?.TitleBar.RightInset ?? 0;
        var width = _page.ActualWidth;
        var inCaption = _pointer is not null && captionHeight >= _host.Height
            && width - (left + right) / scale >= _host.Width + 24;
        Place(inCaption, captionHeight, left / scale);

        if (_pointer is not { } pointer) return;
        RectInt32? region = null;
        if (inCaption && TitleBarRegions.GetPixelBounds(_host) is { } bounds)
        {
            var caption = new RectInt32(left, 0, Math.Max(0, (int)(width * scale) - left - right), _window!.TitleBar.Height);
            region = TitleBarRegions.Intersect(bounds, caption);
        }
        if (Nullable.Equals(_registeredRegion, region)) return;
        if (region is { } value) pointer.SetRegionRects(NonClientRegionKind.Passthrough, [value]);
        else pointer.ClearRegionRects(NonClientRegionKind.Passthrough);
        _registeredRegion = region;
    }

    private void Place(bool inCaption, double captionHeight, double leftInset)
    {
        if (_captionInset.Height.Value != captionHeight)
            _captionInset.Height = new GridLength(captionHeight);
        var margin = inCaption ? new Thickness(leftInset + 12, 0, 0, 0) : new Thickness(12, 6, 0, 0);
        if (_host.Margin != margin) _host.Margin = margin;
        if (_inCaption == inCaption) return;

        Grid.SetRow(_host, inCaption ? 0 : 1);
        _host.VerticalAlignment = inCaption ? VerticalAlignment.Center : VerticalAlignment.Top;
        _inCaption = inCaption;
        _placementChanged?.Invoke(inCaption);
    }

    private void ClearRegion()
    {
        try
        {
            if (_registeredRegion.HasValue) _pointer?.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException) { }
        _registeredRegion = null;
        _pointer = null;
    }

    public void Dispose()
    {
        _host.LayoutUpdated -= LayoutUpdated;
        if (!ReferenceEquals(_owner, this)) return;
        _owner = null;
        ClearRegion();
    }
}
