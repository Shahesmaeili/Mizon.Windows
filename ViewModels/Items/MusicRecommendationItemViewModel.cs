using Microsoft.UI.Xaml.Media;
using Windows.UI;
using FluentSymbol = FluentIcons.Common.Symbol;

namespace Mizon.Windows.ViewModels.Items;

public sealed class MusicRecommendationItemViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public FluentSymbol IconSymbol { get; init; }
    public SolidColorBrush AccentBrush { get; init; } = new(Color.FromArgb(255, 96, 165, 250));
    public Color AccentColor => AccentBrush.Color;
    public Color TransparentAccentColor => Color.FromArgb(0, AccentColor.R, AccentColor.G, AccentColor.B);
}
