using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;

namespace Mizon.Windows.Controls;

/// <summary>Fits settings cards into equal-width columns without resize handlers.</summary>
public sealed partial class SettingsCardPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(SettingsCardPanel),
        new PropertyMetadata(300d, LayoutPropertyChanged));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(SettingsCardPanel),
        new PropertyMetadata(2, LayoutPropertyChanged));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(SettingsCardPanel),
        new PropertyMetadata(14d, LayoutPropertyChanged));

    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan", typeof(int), typeof(SettingsCardPanel),
        new PropertyMetadata(1, static (sender, _) =>
        {
            if (VisualTreeHelper.GetParent(sender) is SettingsCardPanel panel)
                panel.InvalidateMeasure();
        }));

    private int _measuredColumns = 1;

    public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public static int GetColumnSpan(DependencyObject element) => (int)element.GetValue(ColumnSpanProperty);
    public static void SetColumnSpan(DependencyObject element, int value) => element.SetValue(ColumnSpanProperty, value);

    private double ValidMinWidth => double.IsFinite(MinColumnWidth) && MinColumnWidth > 0 ? MinColumnWidth : 300;
    private double ValidSpacing => double.IsFinite(Spacing) && Spacing >= 0 ? Spacing : 14;

    private static void LayoutPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _) =>
        ((SettingsCardPanel)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        // A vertical ScrollViewer supplies a finite width. Use one preferred-width
        // column if another host measures horizontally without a constraint.
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : ValidMinWidth;
        var spacing = ValidSpacing;
        var columns = GetColumnCount(width, spacing);
        _measuredColumns = columns;
        var columnWidth = Math.Max(0, (width - spacing * (columns - 1)) / columns);
        var occupied = 0;
        var rowHeight = 0d;
        var height = 0d;

        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Measure(new Size(0, 0));
                continue;
            }

            var span = Math.Clamp(GetColumnSpan(child), 1, columns);
            if (occupied > 0 && occupied + span > columns)
            {
                height += rowHeight + spacing;
                rowHeight = 0;
                occupied = 0;
            }

            child.Measure(new Size(columnWidth * span + spacing * (span - 1), double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            occupied += span;
        }

        return new Size(width, height + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = double.IsFinite(finalSize.Width) ? Math.Max(0, finalSize.Width) : ValidMinWidth;
        var spacing = ValidSpacing;
        // Keep the rows measured above: changing columns during Arrange would use
        // text heights measured at a different width. Width changes trigger Measure.
        var columns = _measuredColumns;
        var columnWidth = Math.Max(0, (width - spacing * (columns - 1)) / columns);
        var rowStart = 0;
        var occupied = 0;
        var rowHeight = 0d;
        var top = 0d;

        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var span = Math.Clamp(GetColumnSpan(child), 1, columns);
            if (occupied > 0 && occupied + span > columns)
            {
                ArrangeRow(rowStart, index, columns, columnWidth, spacing, top, rowHeight);
                top += rowHeight + spacing;
                rowHeight = 0;
                occupied = 0;
                rowStart = index;
            }

            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            occupied += span;
        }

        if (occupied > 0)
            ArrangeRow(rowStart, Children.Count, columns, columnWidth, spacing, top, rowHeight);

        return finalSize;
    }

    private int GetColumnCount(double width, double spacing)
    {
        var minimum = ValidMinWidth;
        if (width < minimum || MaxColumns <= 1)
            return 1;

        var count = Math.Floor((width + spacing) / (minimum + spacing));
        return double.IsFinite(count) ? (int)Math.Clamp(count, 1, Math.Max(1, MaxColumns)) : 1;
    }

    private void ArrangeRow(int start, int end, int columns, double columnWidth, double spacing, double top, double height)
    {
        var column = 0;
        for (var index = start; index < end; index++)
        {
            var child = Children[index];
            if (child.Visibility == Visibility.Collapsed)
                continue;

            var span = Math.Clamp(GetColumnSpan(child), 1, columns);
            // WinUI mirrors the panel for RTL; reversing coordinates here would
            // mirror a second time. FrameworkElement also applies its own Margin.
            child.Arrange(new Rect(column * (columnWidth + spacing), top,
                columnWidth * span + spacing * (span - 1), height));
            column += span;
        }
    }
}
