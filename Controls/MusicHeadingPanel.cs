using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Mizon.Windows.Controls;

/// <summary>Keeps the heading and its actions together as long as both fit.</summary>
public sealed partial class MusicHeadingPanel : Panel
{
    private const double Gap = 12;
    private bool _inline;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 2) return base.MeasureOverride(availableSize);

        var heading = Children[0];
        var actions = Children[1];
        var naturalSize = new Size(double.PositiveInfinity, availableSize.Height);
        heading.Measure(naturalSize);
        actions.Measure(naturalSize);
        var naturalWidth = heading.DesiredSize.Width + Gap + actions.DesiredSize.Width;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : naturalWidth;
        _inline = naturalWidth <= width;
        if (_inline)
            return new Size(width, Math.Max(heading.DesiredSize.Height, actions.DesiredSize.Height));

        heading.Measure(new Size(width, availableSize.Height));
        actions.Measure(new Size(width, availableSize.Height));
        return new Size(width, heading.DesiredSize.Height + Gap + actions.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 2) return base.ArrangeOverride(finalSize);

        var heading = Children[0];
        var actions = Children[1];
        var width = finalSize.Width;
        var actionWidth = Math.Min(width, actions.DesiredSize.Width);
        var actionHeight = actions.DesiredSize.Height;
        var headingHeight = heading.DesiredSize.Height;
        heading.Arrange(new Rect(0, 0, _inline ? Math.Max(0, width - actionWidth - Gap) : width, headingHeight));
        actions.Arrange(new Rect(width - actionWidth, _inline ? Math.Max(0, (headingHeight - actionHeight) / 2) : headingHeight + Gap,
            actionWidth, actionHeight));
        return finalSize;
    }
}
