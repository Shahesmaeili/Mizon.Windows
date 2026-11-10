using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.Graphics;

namespace Mizon.Windows.Common;

internal static class TitleBarRegions
{
    internal static RectInt32? GetPixelBounds(FrameworkElement element)
    {
        if (!element.IsLoaded || element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return null;

        var bounds = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var scale = element.XamlRoot.RasterizationScale;
        var left = (int)Math.Floor(bounds.Left * scale);
        var top = (int)Math.Floor(bounds.Top * scale);
        var right = (int)Math.Ceiling(bounds.Right * scale);
        var bottom = (int)Math.Ceiling(bounds.Bottom * scale);
        return new RectInt32(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    internal static RectInt32? Intersect(RectInt32 bounds, RectInt32 caption)
    {
        var left = Math.Max(bounds.X, caption.X);
        var top = Math.Max(bounds.Y, caption.Y);
        var right = Math.Min(bounds.X + bounds.Width, caption.X + caption.Width);
        var bottom = Math.Min(bounds.Y + bounds.Height, caption.Y + caption.Height);
        return right > left && bottom > top ? new RectInt32(left, top, right - left, bottom - top) : null;
    }

    internal static RectInt32[] GetInteractiveRects(DependencyObject root, RectInt32 caption)
    {
        List<RectInt32> regions = [];
        AppendInteractiveRects(root, caption, regions);
        return [.. regions];
    }

    private static void AppendInteractiveRects(DependencyObject node, RectInt32 caption, List<RectInt32> regions)
    {
        // Popup descendants live in another input surface, even when their XAML
        // template belongs to a title-bar control. Never register them on the window.
        if (node is Popup) return;
        if (node is FrameworkElement element)
        {
            if (element.Visibility != Visibility.Visible || !element.IsHitTestVisible)
                return;

            var bounds = GetPixelBounds(element);
            var clipped = bounds is RectInt32 value ? Intersect(value, caption) : null;
            if (clipped is null) return;

            if (element is ButtonBase or NavigationViewItemBase or Controls.MainSearchBox
                or AutoSuggestBox or ComboBox or TextBox or PasswordBox or ToggleSwitch)
            {
                regions.Add(clipped.Value);
                // Its template is already covered. Descending would also collect
                // clear buttons, cached items and flyout controls outside the header.
                return;
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < childCount; index++)
            AppendInteractiveRects(VisualTreeHelper.GetChild(node, index), caption, regions);
    }
}
