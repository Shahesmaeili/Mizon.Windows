using Microsoft.UI.Xaml;

namespace Mizon.Windows.ViewModels.Items;

// Non-selectable presentation for loading, empty and failed searches.
public sealed partial class MainSearchStatusItemViewModel : ViewModelBase
{
    public string Title { get => field; set => SetField(ref field, value); } = string.Empty;
    public string Description { get => field; set => SetField(ref field, value); } = string.Empty;
    public bool IsBusy
    {
        get => field;
        set { if (SetField(ref field, value)) OnPropertyChanged(nameof(BusyVisibility)); }
    }
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public override string ToString() => string.Empty;
}
