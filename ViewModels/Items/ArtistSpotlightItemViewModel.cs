using Mizon.Windows.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mizon.Windows.ViewModels.Items;

public sealed class ArtistSpotlightItemViewModel
{
    private static LocalizationService Localization => LocalizationService.Current;

    public required TopArtistItemViewModel Artist { get; init; }
    public Guid? ModelMediaId { get; init; }
    public string? ModelAttribution { get; init; }
    public float ModelYawInDegrees { get; init; }
    public float ModelViewScale { get; init; } = 1;
    public float ModelVerticalFocus { get; init; }
    public int SongCount { get; init; }
    public int[] DailyPlays { get; init; } = [];
    public IReadOnlyList<DiscoveryMusicItemViewModel> LatestMusics { get; init; } = [];
    public string SongCountText => Format(SongCount);
    public string WeeklyPlaysText => Format(DailyPlays.Sum(x => (long)x));

    private static string Format(long value) => value.ToString("N0", CultureInfo.GetCultureInfo(Localization.LanguageTag));
}
