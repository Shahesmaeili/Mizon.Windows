using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace Mizon.Windows.Converters;

public sealed partial class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = null!;
    public Brush FalseBrush { get; set; } = null!;
    public Brush NullBrush { get; set; } = null!;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is null)
            return NullBrush;

        if (value is bool b)
            return b ? TrueBrush : FalseBrush;

        return NullBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
