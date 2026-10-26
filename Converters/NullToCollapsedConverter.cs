using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Mizon.Windows.Converters;

public sealed partial class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
