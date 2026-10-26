using Microsoft.UI.Xaml.Controls;

namespace Mizon.Windows.Services;

public class TeachingTipService
{
    private TeachingTip _teachingTip;

    public void Initialize(TeachingTip teachingTip)
    {
        _teachingTip = teachingTip;
    }


    public void ShowMessage(string title, string subtitle, object? closeButtonContent = null)
    {
        _teachingTip.Title = title;
        _teachingTip.Subtitle = subtitle;
        _teachingTip.IsOpen = true;
        _teachingTip.CloseButtonContent = closeButtonContent;
    }
}
