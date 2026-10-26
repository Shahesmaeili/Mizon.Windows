using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Mizon.Windows.Converters;

public sealed partial class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is null)
            return Visibility.Collapsed;

        if (value is string s && string.IsNullOrWhiteSpace(s))
            return Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
