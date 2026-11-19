using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Mizon.Windows.Controls;

/// <summary>Spreads the compact insight cards across the available width.</summary>
public sealed partial class TrackInsightsPanel : Panel
{
    private const double PreferredCardWidth = 300;
    private const double MinimumColumnWidth = 232;
    private const double FullScreenMinimumColumnWidth = 168;
    private const double CardHeight = 100;
    private const double Spacing = 16;

    private int _columns = 1;

    internal bool PreferSingleRow
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        if (count == 0) return default;

        var width = double.IsFinite(availableSize.Width)
            ? availableSize.Width : PreferredCardWidth * count + Spacing * (count - 1);
        var fourColumnWidth = PreferSingleRow ? FullScreenMinimumColumnWidth : MinimumColumnWidth;
        _columns = count >= 4 && width >= 4 * fourColumnWidth + 3 * Spacing ? 4
            : count >= 2 && width >= 2 * MinimumColumnWidth + Spacing ? 2 : 1;

        var size = new Size(GetCardWidth(width), CardHeight);
        foreach (var card in Children)
            card.Measure(size);

        var rows = (count + _columns - 1) / _columns;
        return new Size(width, rows * CardHeight + (rows - 1) * Spacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cardWidth = GetCardWidth(finalSize.Width);
        for (var index = 0; index < Children.Count; index++)
        {
            var x = index % _columns * (cardWidth + Spacing);
            var y = index / _columns * (CardHeight + Spacing);
            Children[index].Arrange(new Rect(x, y, cardWidth, CardHeight));
        }

        return finalSize;
    }

    private double GetCardWidth(double width) => Math.Max(0, (width - (_columns - 1) * Spacing) / _columns);
}
