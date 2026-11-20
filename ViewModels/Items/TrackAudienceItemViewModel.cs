using Microsoft.UI.Xaml.Media;

namespace Mizon.Windows.ViewModels.Items;

public sealed class TrackAudienceItemViewModel : ViewModelBase
{
    public string Name { get => field; set => SetField(ref field, value); } = string.Empty;
    public string Details { get => field; set => SetField(ref field, value); } = string.Empty;
    public ImageSource? Portrait { get => field; set => SetField(ref field, value); }
    internal string? PortraitPath { get; set; }
}
