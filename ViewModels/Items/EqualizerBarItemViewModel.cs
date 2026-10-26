using Microsoft.UI.Xaml.Media;

namespace Mizon.Windows.ViewModels.Items;

public class EqualizerBarItemViewModel : ViewModelBase
{
    public int TopHeight
    {
        get => field;
        set => SetField(ref field, value);
    }

    public SolidColorBrush TopColor
    {
        get => field;
        set => SetField(ref field, value);
    } = null!;

    public int BottomHeight
    {
        get => field;
        set => SetField(ref field, value);
    }

    public SolidColorBrush BottomColor
    {
        get => field;
        set => SetField(ref field, value);
    } = null!;


    public bool IsPlayed
    {
        get => field;
        set => SetField(ref field, value);
    }

    public bool IsHover
    {
        get => field;
        set => SetField(ref field, value);
    }

    public bool IsBottomHover
    {
        get => field;
        set => SetField(ref field, value);
    }
}
