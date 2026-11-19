// Adapted from DevWinUI 10.4.1 (30a7bbb), MIT. See LICENSE.DevWinUI.txt.
using System;
using Microsoft.UI.Xaml.Data;

namespace Mizon.Windows.Controls.TrackDetails;

public partial class WaveProgressBarValueToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null)
        {
            return value;
        }
        var percent = string.Format("{0:F0} %", (double)value);
        return percent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
