using System.Collections.Generic;
using System.Globalization;

namespace Mizon.Windows.ViewModels.Items;

/// <summary>Presentation data for the sample artist spotlight, sharing follow state with the artist cards.</summary>
public sealed class ArtistSpotlightItemViewModel : ViewModelBase
{
    public required TopArtistItemViewModel Artist { get; init; }
    public string ModelPath { get; }
    public string ModelAttribution { get; init; } = string.Empty;
    public float ModelYawInDegrees { get; init; }
    public float ModelViewScale { get; init; } = 1;
    public float ModelVerticalFocus { get; init; }
    public int SongCount { get; init; }
    public double Rating { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<DiscoveryMusicItemViewModel> LatestMusics { get; init; } = [];
    public string SongCountText => SongCount.ToString("N0", CultureInfo.CurrentCulture);
    public string RatingText => Rating.ToString("0.0", CultureInfo.InvariantCulture);

    public ArtistSpotlightItemViewModel(string modelPath)
    {
        ModelPath = modelPath;
    }
}
