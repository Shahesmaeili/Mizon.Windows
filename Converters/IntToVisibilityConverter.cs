using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Mizon.Windows.Converters;

public sealed partial class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is null)
            return Visibility.Collapsed;

        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}