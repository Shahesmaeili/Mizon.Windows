using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Mizon.Windows.Controls;

/// <summary>Sizes artwork from the available width without stretching its source pixels.</summary>
public sealed partial class AspectRatioPanel : Panel
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio), typeof(double), typeof(AspectRatioPanel),
        new PropertyMetadata(1d, static (sender, _) => ((AspectRatioPanel)sender).InvalidateMeasure()));

    public double Ratio { get => (double)GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
    private double ValidRatio => double.IsFinite(Ratio) && Ratio > 0 ? Ratio : 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width
            : double.IsFinite(availableSize.Height) ? availableSize.Height * ValidRatio : 256;
        var height = width / ValidRatio;
        if (double.IsFinite(availableSize.Height) && height > availableSize.Height)
        {
            height = availableSize.Height;
            width = height * ValidRatio;
        }
        var size = new Size(Math.Max(0, width), Math.Max(0, height));
        foreach (var child in Children) child.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Min(finalSize.Width, finalSize.Height * ValidRatio);
        var height = width / ValidRatio;
        var bounds = new Rect((finalSize.Width - width) / 2, (finalSize.Height - height) / 2, width, height);
        foreach (var child in Children) child.Arrange(bounds);
        return finalSize;
    }
}
