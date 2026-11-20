using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Services;
using System;

namespace Mizon.Windows.Controls;

public sealed partial class FlyoutAcrylicSurface : Grid
{
    private SystemBackdropElement? _acrylic;

    public FlyoutAcrylicSurface()
    {
        Loaded += SurfaceLoaded;
        Unloaded += SurfaceUnloaded;
    }

    private void SurfaceLoaded(object sender, RoutedEventArgs args)
    {
        if (_acrylic is not null || XamlRoot is null || Children.Count != 1 || Children[0] is not Border fallback)
            return;

        try
        {
            if (!DesktopAcrylicController.IsSupported()) return;

            // The header profile menu uses the same in-tree backdrop. Attach
            // it after the presenter is rooted so the popup can supply a target.
            _acrylic = new SystemBackdropElement { CornerRadius = CornerRadius, IsHitTestVisible = false };
            Children.Add(_acrylic);
            _acrylic.SystemBackdrop = new DesktopAcrylicBackdrop();
            fallback.Visibility = Visibility.Collapsed;
        }
        catch (Exception error)
        {
            ReleaseAcrylic();
            AppLog.Error("Track flyout acrylic could not be applied.", error);
        }
    }

    private void SurfaceUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        ReleaseAcrylic();
        if (Children.Count == 1 && Children[0] is Border fallback)
            fallback.Visibility = Visibility.Visible;
    }

    private void ReleaseAcrylic()
    {
        if (_acrylic is not { } acrylic) return;
        _acrylic = null;
        acrylic.SystemBackdrop = null;
        Children.Remove(acrylic);
    }
}
