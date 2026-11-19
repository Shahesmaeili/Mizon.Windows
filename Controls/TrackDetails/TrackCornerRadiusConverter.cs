using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Mizon.Windows.Controls.TrackDetails;

public sealed partial class TrackCornerRadiusConverter : IValueConverter
{
    public bool BottomRight { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is CornerRadius radius ? (BottomRight ? radius.BottomRight : radius.TopLeft) : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
