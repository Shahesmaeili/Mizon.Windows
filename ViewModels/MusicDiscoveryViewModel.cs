using Mizon.Windows.Services;
using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using Mizon.Windows.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Mizon.Windows.ViewModels;

/// <summary>Local presentation data for the home redesign. No API or playback state is changed.</summary>
public sealed partial class MusicDiscoveryViewModel : ViewModelBase
{
    public LocalizationService Localization => LocalizationService.Current;
    private readonly IReadOnlyList<DiscoveryMusicItemViewModel> _previewMusics;
    private readonly ImageSource? _malePortrait = ImageSourceFactory.Create("/Assets/Discovery/friend-amir.jpg", 96);
    private readonly ImageSource? _femalePortrait = ImageSourceFactory.Create("/Assets/Discovery/friend-sara.jpg", 96);
    private string _genreFilter = string.Empty;

    public MusicDiscoveryViewModel()
    {
        _previewMusics =
        [
            Preview("Midnight Drive", "مسیر نیمه‌شب", "آرمان", "راد", "Pop", "پاپ", "midnight-drive.jpg", 224, 12840),
            Preview("After the Rain", "بعد از باران", "رها", "مهر", "Pop", "پاپ", "after-the-rain.jpg", 198, 8940),
            Preview("Blue Hour", "ساعت آبی", "ماهان", "نوری", "Electronic", "الکترونیک", "blue-hour.jpg", 256, 19320),
            Preview("Somewhere New", "یک جای تازه", "آوا", "پارسا", "Rock", "راک", "somewhere-new.jpg", 213, 6700),
            Preview("Quiet Room", "اتاق آرام", "نوید", "راد", "Ambient", "امبینت", "quiet-room.jpg", 282, 4680),
            Preview("Golden Days", "روزهای طلایی", "نیما", "مهر", "Pop", "پاپ", "golden-days.jpg", 242, 24650),
            Preview("City Lights", "چراغ‌های شهر", "آرمان", "راد", "Electronic", "الکترونیک", "city-lights.jpg", 208, 11240),
            Preview("Velvet Sky", "آسمان مخملی", "رها", "مهر", "Jazz", "جاز", "velvet-sky.jpg", 267, 7320),
            Preview("Slow Motion", "آهسته‌تر", "ماهان", "نوری", "Hip-Hop", "هیپ‌هاپ", "slow-motion.jpg", 187, 15760),
            Preview("Stay Awhile", "کمی بمان", "آوا", "پارسا", "Rock", "راک", "stay-awhile.jpg", 231, 9840),
            Preview("Ocean Air", "هوای دریا", "نوید", "راد", "Ambient", "امبینت", "ocean-air.jpg", 304, 5280),
            Preview("First Light", "اولین روشنایی", "نیما", "مهر", "Jazz", "جاز", "first-light.jpg", 219, 3760)
        ];
        Populate(_previewMusics);
        TopArtists.Add(new("/Assets/Discovery/friend-amir.jpg", "/Assets/Discovery/artist-stage-01.jpg",
            [0.48, 0.22, 0.56, 0.60, 0.91, 0.79, 0.64, 0.36, 0.20, 0.39, 0.22, 0.48, 0.15, 0.28, 0.23, 0.47, 0.37, 0.64, 0.58, 0.29, 0.42])
        {
            FirstName = "آرمان", LastName = "راد", GenreResourceKey = "MusicHome/ArtistGenreOne",
            FollowersCount = 21400, MonthlyListenersCount = 124800,
            AccentColor = global::Windows.UI.Color.FromArgb(255, 18, 166, 237)
        });
        TopArtists.Add(new("/Assets/Discovery/friend-sara.jpg", "/Assets/Discovery/artist-stage-02.jpg",
            [0.23, 0.34, 0.46, 0.21, 0.31, 0.29, 0.39, 0.66, 0.79, 0.82, 0.56, 0.62, 0.47, 0.22, 0.39, 0.16, 0.26, 0.34, 0.24, 0.47, 0.30])
        {
            FirstName = "رها", LastName = "مهر", GenreResourceKey = "MusicHome/ArtistGenreTwo",
            FollowersCount = 18700, MonthlyListenersCount = 98600,
            AccentColor = global::Windows.UI.Color.FromArgb(255, 236, 74, 114)
        });
        TopArtists.Add(new("/Assets/Discovery/artist-portrait-03.jpg", "/Assets/Discovery/slow-motion.jpg",
            [0.28, 0.48, 0.38, 0.17, 0.36, 0.63, 0.55, 0.70, 0.57, 0.34, 0.44, 0.27, 0.39, 0.32, 0.42, 0.22, 0.28, 0.36, 0.31, 0.24, 0.42])
        {
            FirstName = "ماهان", LastName = "نوری", GenreResourceKey = "MusicHome/ArtistGenreThree",
            FollowersCount = 15200, MonthlyListenersCount = 86400,
            AccentColor = global::Windows.UI.Color.FromArgb(255, 18, 166, 237)
        });
        ArtistSpotlights =
        [
            new("ms-appx:///Assets/Discovery/Models/MoeinBust.glb")
            {
                ModelAttributionKey = "MusicHome/MoeinAttribution",
                ModelYawInDegrees = 180, ModelViewScale = 1, ModelVerticalFocus = 0,
                Artist = TopArtists[0], SongCount = 42, Rating = 4.9,
                DescriptionKey = "MusicHome/SpotlightOneDescription",
                LatestMusics = GetArtistMusics(TopArtists[0])
            },
            new("ms-appx:///Assets/Discovery/Models/AyaScan.glb")
            {
                ModelAttributionKey = "MusicHome/AyaAttribution",
                ModelYawInDegrees = -75, ModelViewScale = 2.1f, ModelVerticalFocus = .5f,
                Artist = TopArtists[1], SongCount = 28, Rating = 4.8,
                DescriptionKey = "MusicHome/SpotlightTwoDescription",
                LatestMusics = GetArtistMusics(TopArtists[1])
            },
            new("ms-appx:///Assets/Discovery/Models/KanaScan.glb")
            {
                ModelAttributionKey = "MusicHome/KanaAttribution",
                ModelYawInDegrees = 0, ModelViewScale = 2.1f, ModelVerticalFocus = .5f,
                Artist = TopArtists[2], SongCount = 36, Rating = 4.9,
                DescriptionKey = "MusicHome/SpotlightThreeDescription",
                LatestMusics = GetArtistMusics(TopArtists[2])
            }
        ];
    }

    public ObservableCollection<DiscoveryMusicItemViewModel> TrendingMusics { get; } = [];
    public ObservableCollection<DiscoveryMusicItemViewModel> RecentMusics { get; } = [];
    public ObservableCollection<DiscoveryMusicItemViewModel> RecommendedMusics { get; } = [];
    public ObservableCollection<TopArtistItemViewModel> TopArtists { get; } = [];
    public WeeklyRhythmViewModel WeeklyRhythm { get; } = new();
    public IReadOnlyList<ArtistSpotlightItemViewModel> ArtistSpotlights { get; }

    public IReadOnlyList<DiscoveryMusicItemViewModel> GetArtistMusics(TopArtistItemViewModel artist) =>
        _previewMusics.Where(music => music.Contributors.Any(contributor =>
            contributor.DisplayName == artist.DisplayName)).ToArray();
    public bool IsDemo => true;
    public bool HasTrending => TrendingMusics.Count > 0;
    public bool HasRecent => RecentMusics.Count > 0;
    public bool HasRecommended => RecommendedMusics.Count > 0;
    public bool HasResults => HasRecommended;
    public bool IsReady => !IsLoading && HasResults;
    public bool IsEmpty => !IsLoading && !HasResults;
    public bool HasError => false;
    public string ErrorMessage => string.Empty;
    public string PreviewNotice { get => Localization.GetString(field); private set => SetField(ref field, value); } = "MusicHome/Demo";

    // This local preview is ready in the constructor. Synchronous filtering must
    // not hide all widgets behind a skeleton or replay their entrance animations.
    public bool IsLoading => false;

    public string SelectedGenreTitle { get => string.IsNullOrEmpty(field) ? Localization.GetString("MusicHome/AllGenres") : field; private set => SetField(ref field, value); } = string.Empty;

    public Task LoadAsync(bool force = false, Guid? genrePublicId = null, string? genreTitle = null)
    {
        var filter = genrePublicId.HasValue && !string.IsNullOrWhiteSpace(genreTitle) ? NormalizeGenre(genreTitle) : string.Empty;
        if (!force && _genreFilter == filter) return Task.CompletedTask;
        _genreFilter = filter;
        SelectedGenreTitle = string.IsNullOrWhiteSpace(genreTitle) ? string.Empty : genreTitle;
        IReadOnlyList<DiscoveryMusicItemViewModel> musics = _previewMusics;
        PreviewNotice = "MusicHome/Demo";
        if (genrePublicId is not null && !string.IsNullOrWhiteSpace(genreTitle))
        {
            var filtered = _previewMusics.Where(item => NormalizeGenre(item.GenreTitle) == filter
                || NormalizeGenre(item.PersianGenreTitle) == filter).ToArray();
            if (filtered.Length > 0)
            {
                musics = filtered;
                SelectedGenreTitle = filtered[0].PersianGenreTitle;
            }
            else PreviewNotice = "MusicHome/NoGenreDemo";
        }
        Populate(musics);
        return Task.CompletedTask;
    }

    public void RefreshLocalization()
    {
        foreach (var item in _previewMusics) item.RefreshLocalization();
        foreach (var item in TrendingMusics) item.RefreshLocalization();
        foreach (var item in RecentMusics) item.RefreshLocalization();
        foreach (var item in RecommendedMusics) item.RefreshLocalization();
        foreach (var artist in TopArtists) artist.RefreshLocalization();
        foreach (var spotlight in ArtistSpotlights) spotlight.RefreshLocalization();
        OnPropertyChanged(nameof(PreviewNotice));
        OnPropertyChanged(nameof(SelectedGenreTitle));
    }

    public void CancelLoading() { }

    private static string NormalizeGenre(string value) => value
        .Replace("-", string.Empty)
        .Replace(" ", string.Empty)
        .Replace("\u200C", string.Empty)
        .Replace('ي', 'ی')
        .Replace('ك', 'ک')
        .ToUpperInvariant();

    private void Populate(IReadOnlyList<DiscoveryMusicItemViewModel> musics)
    {
        Replace(TrendingMusics, musics.OrderByDescending(item => item.LikesCount).Take(6));
        Replace(RecentMusics, musics.Skip(2).Concat(musics.Take(2)).Take(6));
        Replace(RecommendedMusics, musics.Reverse().Take(6));
        OnPropertyChanged(nameof(HasTrending));
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(HasRecommended));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static void Replace(ObservableCollection<DiscoveryMusicItemViewModel> target, IEnumerable<DiscoveryMusicItemViewModel> source)
    {
        var index = 0;
        foreach (var sourceItem in source)
        {
            var rank = index + 1;
            if (index < target.Count && target[index].IsRankedFrom(sourceItem, rank))
            {
                index++;
                continue;
            }

            // Keep unchanged card instances, compiled bindings and their decoded
            // images alive. Only changed positions need a new ranked presentation.
            if (index < target.Count) target[index] = sourceItem.WithRank(rank);
            else target.Add(sourceItem.WithRank(rank));
            index++;
        }
        while (target.Count > index) target.RemoveAt(target.Count - 1);
    }

    private DiscoveryMusicItemViewModel Preview(string title, string persianTitle, string firstName,
        string lastName, string genre, string persianGenre, string asset, int seconds, int likes)
    {
        var contributor = new DiscoveryContributorItemViewModel { FirstName = firstName, LastName = lastName };
        // Stock portraits belong to fictional preview records, never to API profiles.
        contributor.SetProfileImage(firstName is "رها" or "آوا" ? _femalePortrait : _malePortrait);
        return new()
        {
            // Empty IDs deliberately prevent sample cards from navigating to a fabricated song.
            Title = title,
            PersianTitle = persianTitle,
            GenreTitle = genre,
            PersianGenreTitle = persianGenre,
            Duration = TimeSpan.FromSeconds(seconds),
            LikesCount = likes,
            CoverImagePath = "/Assets/Discovery/" + asset,
            Contributors = [contributor]
        };
    }
}
