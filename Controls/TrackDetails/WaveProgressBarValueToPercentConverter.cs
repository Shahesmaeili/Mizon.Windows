// Adapted from DevWinUI 10.4.1 (30a7bbb), MIT. See LICENSE.DevWinUI.txt.
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Mizon.Windows.Controls.TrackDetails;

public partial class WaveProgressBarValueToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is double percent ? $"{percent:F0} %" : DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
