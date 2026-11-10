using Microsoft.UI.Xaml.Media;
using Mizon.Windows.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mizon.Windows.ViewModels.Items;

public sealed partial class DiscoveryMusicItemViewModel : ViewModelBase
{
    private string _coverImagePath = string.Empty;
    private DiscoveryMusicItemViewModel? _rankSource;
    public Guid PublicId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? PersianTitle { get; init; }
    public string GenreTitle { get; init; } = string.Empty;
    public string PersianGenreTitle { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public int LikesCount { get; init; }
    public int Rank { get; init; }
    public Guid? CoverMediaPublicId { get; init; }
    public int CoverDecodeWidth { get; init; } = 480;
    public IReadOnlyList<DiscoveryContributorItemViewModel> Contributors { get; init; } = [];
    public string Artist => string.Join("، ", Contributors.Select(item => item.DisplayName));
    public ImageSource? ArtistProfileImage => Contributors.FirstOrDefault()?.ProfileImage;
    public DiscoveryContributorItemViewModel? PrimaryContributor => Contributors.Count > 0 ? Contributors[0] : null;
    public DiscoveryContributorItemViewModel? SecondaryContributor => Contributors.Count > 1 ? Contributors[1] : null;
    public string PrimaryProfileAutomationName => $"نمایش پروفایل {PrimaryArtistName}";
    public string SecondaryProfileAutomationName => $"نمایش پروفایل {SecondaryArtistName}";
    public Microsoft.UI.Xaml.Visibility PrimaryArtistVisibility => Contributors.Count > 0
        ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string PrimaryArtistName => Contributors.Count > 0 ? Contributors[0].DisplayName : string.Empty;
    public string SecondaryArtistName => Contributors.Count > 1 ? Contributors[1].DisplayName : string.Empty;
    public ImageSource? SecondaryArtistProfileImage => Contributors.Count > 1 ? Contributors[1].ProfileImage : null;
    public Microsoft.UI.Xaml.Visibility SecondaryArtistVisibility => Contributors.Count > 1
        ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string DurationText => Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss") : Duration.ToString(@"m\:ss");
    public string LikesCountText => LikesCount.ToString("N0", CultureInfo.CurrentCulture);
    public string DisplayTitle => string.IsNullOrWhiteSpace(PersianTitle) ? Title : PersianTitle;
    public string SecondaryTitle => string.Equals(DisplayTitle, Title, StringComparison.OrdinalIgnoreCase) ? string.Empty : Title;
    public Microsoft.UI.Xaml.Visibility SecondaryTitleVisibility => string.IsNullOrWhiteSpace(SecondaryTitle)
        ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public string CompactLikesText => LikesCount >= 1_000_000
        ? (LikesCount / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : LikesCount >= 1000
            ? (LikesCount / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "K"
            : LikesCountText;
    public string ChartSummaryText => $"{LikesCountText} پسند · {DurationText}";
    public string ChartAutomationName => $"{DisplayTitle}، {Artist}، {ChartSummaryText}";
    public string AutomationName => $"{DisplayTitle}، {Artist}، {PersianGenreTitle}، مدت {DurationText}، {LikesCountText} پسند";
    public string RankText => Rank.ToString("00", CultureInfo.InvariantCulture);
    public string RankChangeText => Rank % 3 == 0 ? "تازه‌وارد" : $"+{Rank + 2}";
    public ImageSource? CoverImage { get; private set; }

    public string CoverImagePath
    {
        get => _coverImagePath;
        internal set
        {
            if (!SetField(ref _coverImagePath, value)) return;
            CoverImage = ImageSourceFactory.Create(value, CoverDecodeWidth);
            OnPropertyChanged(nameof(CoverImage));
        }
    }

    internal bool IsRankedFrom(DiscoveryMusicItemViewModel source, int rank) =>
        Rank == rank && ReferenceEquals(_rankSource ?? this, source);

    internal DiscoveryMusicItemViewModel WithRank(int rank) => new()
    {
        _rankSource = _rankSource ?? this,
        PublicId = PublicId,
        Title = Title,
        PersianTitle = PersianTitle,
        GenreTitle = GenreTitle,
        PersianGenreTitle = PersianGenreTitle,
        Duration = Duration,
        LikesCount = LikesCount,
        Rank = rank,
        CoverMediaPublicId = CoverMediaPublicId,
        CoverDecodeWidth = CoverDecodeWidth,
        Contributors = Contributors,
        _coverImagePath = _coverImagePath,
        CoverImage = CoverImage
    };
}

public sealed partial class DiscoveryContributorItemViewModel : ViewModelBase
{
    public Guid PersonPublicId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public Guid? ProfileMediaPublicId { get; init; }
    public string DisplayName => string.Join(" ", new[] { FirstName, LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
    public string Initials => string.Concat(
        string.IsNullOrWhiteSpace(FirstName) ? string.Empty : FirstName[..1],
        string.IsNullOrWhiteSpace(LastName) ? string.Empty : LastName[..1]);
    public ImageSource? ProfileImage { get; private set; }

    internal void SetProfileImage(ImageSource? image)
    {
        if (ReferenceEquals(ProfileImage, image)) return;
        ProfileImage = image;
        OnPropertyChanged(nameof(ProfileImage));
    }
}
