using Microsoft.UI.Xaml.Controls;

namespace Mizon.Windows.Services;

public class TeachingTipService
{
    private TeachingTip? _teachingTip;

    public void Initialize(TeachingTip teachingTip)
    {
        _teachingTip = teachingTip;
    }


    public void ShowMessage(string title, string subtitle, object? closeButtonContent = null)
    {
        if (_teachingTip is not { } tip) return;

        tip.Title = title;
        tip.Subtitle = subtitle;
        tip.CloseButtonContent = closeButtonContent;
        tip.IsOpen = true;
    }
}
