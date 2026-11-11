using Mizon.Windows.Services;
using System.Collections.Generic;
using System.Globalization;

namespace Mizon.Windows.ViewModels.Items;

/// <summary>Presentation data for the sample artist spotlight, sharing follow state with the artist cards.</summary>
public sealed class ArtistSpotlightItemViewModel : ViewModelBase
{
    public LocalizationService Localization => LocalizationService.Current;

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    public required TopArtistItemViewModel Artist { get; init; }
    public string ModelPath { get; }
    public string ModelAttributionKey { get; init; } = string.Empty;
    public string ModelAttribution => string.IsNullOrEmpty(ModelAttributionKey) ? string.Empty : Localization.GetString(ModelAttributionKey);
    public float ModelYawInDegrees { get; init; }
    public float ModelViewScale { get; init; } = 1;
    public float ModelVerticalFocus { get; init; }
    public int SongCount { get; init; }
    public double Rating { get; init; }
    public string DescriptionKey { get; init; } = string.Empty;
    public string Description => string.IsNullOrEmpty(DescriptionKey) ? string.Empty : Localization.GetString(DescriptionKey);
    public IReadOnlyList<DiscoveryMusicItemViewModel> LatestMusics { get; init; } = [];
    public string SongCountText => SongCount.ToString("N0", CultureInfo.GetCultureInfo(Localization.LanguageTag));
    public string RatingText => Rating.ToString("0.0", CultureInfo.InvariantCulture);

    public ArtistSpotlightItemViewModel(string modelPath)
    {
        ModelPath = modelPath;
    }
}
