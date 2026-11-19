using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Mizon.Windows.Controls;

/// <summary>Packs the original 300 by 100 insight cards in reading order.</summary>
public sealed partial class TrackInsightsPanel : Panel
{
    private const double CardWidth = 300;
    private const double CardHeight = 100;
    private const double Spacing = 16;

    private double _cardWidth;
    private double _cardHeight;
    private int _columns = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width)
            ? availableSize.Width : CardWidth * Children.Count + Spacing * Math.Max(0, Children.Count - 1);
        _cardWidth = Math.Min(CardWidth, width);
        _cardHeight = _cardWidth * (CardHeight / CardWidth);
        _columns = Math.Max(1, (int)Math.Floor((width + Spacing) / (CardWidth + Spacing)));

        var size = new Size(_cardWidth, _cardHeight);
        foreach (var card in Children)
            card.Measure(size);

        var rows = (Children.Count + _columns - 1) / _columns;
        return new Size(width, rows * _cardHeight + Math.Max(0, rows - 1) * Spacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var index = 0; index < Children.Count; index++)
        {
            var x = index % _columns * (_cardWidth + Spacing);
            var y = index / _columns * (_cardHeight + Spacing);
            Children[index].Arrange(new Rect(x, y, _cardWidth, _cardHeight));
        }

        return finalSize;
    }
}
