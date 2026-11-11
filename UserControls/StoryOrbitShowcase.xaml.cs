using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.UI.ViewManagement;

namespace Mizon.Windows.UserControls;

public sealed partial class StoryOrbitShowcase : UserControl
{
    private readonly UISettings _settings = new();
    private long _lastWheel;
    private double _wheelTarget;

    public StoryOrbitShowcase() => InitializeComponent();

    private void Showcase_Loaded(object sender, RoutedEventArgs args)
    {
        UpdateLayoutWidth();
    }

    private void Showcase_Unloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded) return;
        _lastWheel = 0;
    }

    private void Showcase_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutWidth();

    private void Artwork_Wheel(object sender, PointerRoutedEventArgs args)
    {
        var properties = args.GetCurrentPoint(ArtworkScroll).Properties;
        if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0 || ArtworkScroll.ScrollableWidth <= 0
            || args.KeyModifiers.HasFlag(global::Windows.System.VirtualKeyModifiers.Control)) return;
        var now = Environment.TickCount64;
        var origin = now - _lastWheel > 350 ? ArtworkScroll.HorizontalOffset : _wheelTarget;
        _lastWheel = now;
        _wheelTarget = Math.Clamp(origin - properties.MouseWheelDelta / 120d * 170, 0, ArtworkScroll.ScrollableWidth);
        if (Math.Abs(ArtworkScroll.HorizontalOffset - _wheelTarget) < .5) return;
        ArtworkScroll.ChangeView(_wheelTarget, null, null, !_settings.AnimationsEnabled);
        args.Handled = true;
    }

    private void UpdateLayoutWidth()
    {
        if (!double.IsFinite(ActualWidth) || ActualWidth <= 0) return;
        // Keep all three readable. A narrow window reveals a horizontal strip
        // instead of shrinking captions or duplicating/reparenting controls.
        var compact = ActualWidth < 660;
        var width = compact ? 850 : Math.Min(1130, ActualWidth);
        if (Cards.Width != width) Cards.Width = width;
        var mode = compact ? ScrollMode.Enabled : ScrollMode.Disabled;
        if (ArtworkScroll.HorizontalScrollMode != mode)
        {
            ArtworkScroll.HorizontalScrollMode = mode;
            ArtworkScroll.HorizontalScrollBarVisibility = compact ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
            if (!compact) ArtworkScroll.ChangeView(0, null, null, true);
        }
    }
}
