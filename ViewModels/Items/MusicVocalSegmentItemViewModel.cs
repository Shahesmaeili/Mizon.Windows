using Microsoft.UI.Xaml.Media;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicVocalSegmentItemViewModel : ViewModelBase
{
    public double CanvasLeft { get; init; }
    public double Width { get; init; }
    public SolidColorBrush Color { get; init; } = null!;
}
