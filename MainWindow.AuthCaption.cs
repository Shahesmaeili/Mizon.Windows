using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mizon.Windows.Common;
using System;

namespace Mizon.Windows;

public sealed partial class MainWindow
{
    internal void ShowAccountCaptionSelection(AuthCaptionBounds? bounds)
    {
        if (bounds is not { IsValid: true } rect || !IsAvailable
            || RootGrid.XamlRoot is not { RasterizationScale: > 0 } root)
        {
            AccountCaptionFocusLayer.Visibility = Visibility.Collapsed;
            return;
        }

        var scale = root.RasterizationScale;
        const double inset = 3;
        Canvas.SetLeft(AccountCaptionFocusRing, rect.X / scale + inset);
        Canvas.SetTop(AccountCaptionFocusRing, rect.Y / scale + inset);
        AccountCaptionFocusRing.Width = Math.Max(0, rect.Width / scale - inset * 2);
        AccountCaptionFocusRing.Height = Math.Max(0, rect.Height / scale - inset * 2);
        AccountCaptionFocusLayer.Visibility = Visibility.Visible;
    }
}
